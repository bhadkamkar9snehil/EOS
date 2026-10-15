using EOS.Domain.People;

namespace EOS.Infrastructure.Importing;

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

        if (name.Contains(' ')) return null;

        var sameFirst = people.Where(p => FirstToken(p) == name).ToList();
        if (sameFirst.Count == 1) return sameFirst[0];
        if (sameFirst.Count > 1) return null;

        var close = people.Where(p => OsaDistance(FirstToken(p), name) <= 1).ToList();
        return close.Count == 1 ? close[0] : null;
    }

    private static string FirstToken(Person p) => Normalise(p.FullName).Split(' ')[0];

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
