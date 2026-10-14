using EOS.Domain.People;

namespace EOS.Infrastructure.Importing;

/// <summary>
/// Resolves a name written in a source (full name, first name, near-spelling, alias) to a person, never guessing:
/// an ambiguous or unknown name resolves to null and is reported (spec §6.4, risk R11).
/// Order: alias table, exact full name, unique first name, unique first name within one edit (typo/transposition).
/// </summary>
public sealed class PersonMatcher(IReadOnlyList<Person> people, IReadOnlyList<PersonAlias> aliases)
{
    public static string Normalise(string? s) =>
        string.Join(' ', (s ?? "").Replace(' ', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public Person? Resolve(string? raw)
    {
        var name = Normalise(raw);
        if (name.Length == 0) return null;

        var byAlias = aliases.Where(a => a.PersonId is not null && Normalise(a.Alias) == name)
                             .Select(a => a.PersonId!.Value).Distinct().ToList();
        if (byAlias.Count == 1) return people.FirstOrDefault(p => p.Id == byAlias[0]);

        var exact = people.Where(p => Normalise(p.FullName) == name).ToList();
        if (exact.Count == 1) return exact[0];
        if (exact.Count > 1) return null;

        if (name.Contains(' ')) return null;           // a multi-word name that matched nothing exactly is not guessed

        var sameFirst = people.Where(p => FirstToken(p) == name).ToList();
        if (sameFirst.Count == 1) return sameFirst[0];
        if (sameFirst.Count > 1) return null;

        var close = people.Where(p => OsaDistance(FirstToken(p), name) <= 1).ToList();
        return close.Count == 1 ? close[0] : null;
    }

    private static string FirstToken(Person p) => Normalise(p.FullName).Split(' ')[0];

    /// <summary>Optimal string alignment distance (insert, delete, substitute, adjacent transposition).</summary>
    public static int OsaDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[a.Length, b.Length];
    }
}
