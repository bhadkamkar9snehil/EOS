using EOS.Domain.Capacity;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed class LeaveImporter(EosDbContext db)
{
    public Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default) =>
        ImportRunner.RunAsync(db, "leave", path, commit, actor, report => WorkAsync(path, report, ct), ct);

    private async Task WorkAsync(string path, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(path);
        var ws = wb.Worksheets.First();
        var headerRow = 0; var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r <= 10 && headerRow == 0; r++)
        {
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= 12; c++) { var t = ws.Text(r, c).Trim(); if (t.Length > 0) found[t] = c; }
            if ((found.ContainsKey("person") || found.ContainsKey("name") || found.ContainsKey("engineer")) && found.ContainsKey("from") && found.ContainsKey("to")) { headerRow = r; cols = found; }
        }
        if (headerRow == 0) { report.Warnings.Add("Expected columns: Person (or Name, Engineer), From, To, and optionally Kind and Note."); return; }
        var personCol = cols.GetValueOrDefault("person", cols.GetValueOrDefault("name", cols.GetValueOrDefault("engineer")));

        var people = await db.People.AsNoTracking().ToListAsync(ct);
        var aliases = await db.PersonAliases.AsNoTracking().ToListAsync(ct);
        var matcher = new Importing.PersonMatcher(people, aliases);
        var existing = await db.Leaves.ToListAsync(ct);
        var unresolved = new HashSet<string>();

        for (var r = headerRow + 1; r <= (ws.LastRowUsed()?.RowNumber() ?? headerRow); r++)
        {
            var raw = ws.Text(r, personCol).Trim();
            if (raw.Length == 0) continue;
            var from = ws.Date(r, cols["from"]); var to = ws.Date(r, cols["to"]) ?? from;
            if (from is null) { report.Warnings.Add($"Row {r}: no start date for {raw}."); continue; }
            var person = matcher.Resolve(raw);
            if (person is null) { unresolved.Add(raw); continue; }
            var kind = cols.TryGetValue("kind", out var kc) && ws.Text(r, kc).Trim() is { Length: > 0 } k ? k : "Leave";
            var note = cols.TryGetValue("note", out var nc) ? ws.Text(r, nc).Trim() : "";

            var hit = existing.FirstOrDefault(l => l.PersonId == person.Id && l.FromDate == from && l.ToDate == to);
            if (hit is null)
            {
                var leave = new Leave { PersonId = person.Id, FromDate = from.Value, ToDate = to!.Value, Kind = kind, Note = note.Length == 0 ? null : note };
                db.Leaves.Add(leave); existing.Add(leave);
                report.Added("Leave");
            }
            else if (hit.Kind != kind) { hit.Kind = kind; report.Updated("Leave"); }
            else report.Unchanged("Leave");
        }
        report.Unresolved.AddRange(unresolved.OrderBy(x => x));
    }
}
