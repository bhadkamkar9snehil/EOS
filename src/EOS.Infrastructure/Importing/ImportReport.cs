using System.Text;

namespace EOS.Infrastructure.Importing;

public sealed record EntityCounts(int Added, int Updated, int Unchanged);

public sealed class ImportReport(string kind, string file, bool committed)
{
    private readonly Dictionary<string, int[]> _counts = new(StringComparer.Ordinal);

    public string Kind { get; } = kind;
    public string File { get; } = file;
    public bool Committed { get; set; } = committed;
    public List<string> Warnings { get; } = [];

    public List<string> Unresolved { get; } = [];

    public IReadOnlyDictionary<string, EntityCounts> Counts =>
        _counts.ToDictionary(kv => kv.Key, kv => new EntityCounts(kv.Value[0], kv.Value[1], kv.Value[2]));

    public void Added(string entity) => Bump(entity, 0);
    public void Updated(string entity) => Bump(entity, 1);
    public void Unchanged(string entity) => Bump(entity, 2);

    private void Bump(string entity, int slot)
    {
        if (!_counts.TryGetValue(entity, out var c)) _counts[entity] = c = new int[3];
        c[slot]++;
    }

    public EntityCounts Of(string entity) =>
        _counts.TryGetValue(entity, out var c) ? new EntityCounts(c[0], c[1], c[2]) : new EntityCounts(0, 0, 0);

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{(Committed ? "IMPORTED" : "PREVIEW (nothing saved)")}  {Kind}  {System.IO.Path.GetFileName(File)}");
        foreach (var (entity, c) in Counts.OrderBy(x => x.Key))
            sb.AppendLine($"  {entity,-18} +{c.Added} added, ~{c.Updated} updated, ={c.Unchanged} unchanged");
        if (Unresolved.Count > 0) sb.AppendLine($"  Unresolved people/columns ({Unresolved.Count}): {string.Join(", ", Unresolved)}");
        foreach (var w in Warnings) sb.AppendLine($"  WARN {w}");
        return sb.ToString();
    }
}
