using EOS.Domain.Audit;
using EOS.Domain.People;
using EOS.Domain.Training;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed class TrainingWorkbookImporter(EosDbContext db)
{
    public async Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default)
    {
        var report = new ImportReport("training workbook", path, commit);
        using var wb = WorkbookReading.Open(path);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var matcher = await ImportModulesAsync(wb, report, ct);
        await ImportDoubtsAsync(wb, report, matcher, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = commit ? "import.commit" : "import.preview",
            SubjectKind = "training-workbook", SubjectId = Path.GetFileName(path),
        });
        await db.SaveChangesAsync(ct);
        if (commit) await tx.CommitAsync(ct);
        else { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); }
        return report;
    }

    private async Task<PersonMatcher> ImportModulesAsync(ClosedXML.Excel.XLWorkbook wb, ImportReport report, CancellationToken ct)
    {
        var people = await db.People.ToListAsync(ct);
        var aliases = await db.PersonAliases.ToListAsync(ct);
        var matcher = new PersonMatcher(people, aliases);

        var ws = wb.Sheet("Module");
        if (ws is null) { report.Warnings.Add("Sheet 'Module' not found."); return matcher; }
        var header = ws.FindHeaderRow("Sr No", 5);
        if (header is null) { report.Warnings.Add("Module: header row 'Sr No' not found."); return matcher; }

        var engineerColumns = new Dictionary<int, Person>();
        for (var c = 5; c <= ws.LastColumn(header.Value); c++)
        {
            var title = ws.Text(header.Value, c);
            if (title.Length == 0) continue;
            var person = matcher.Resolve(title);
            if (person is null)
            {
                person = new Person
                {
                    Code = "TR-" + new string(title.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()),
                    FullName = title, IsUnverified = true,
                };
                if (await db.People.AnyAsync(p => p.Code == person.Code, ct) || people.Any(p => p.Code == person.Code))
                    person = people.First(p => p.Code == person.Code);
                else
                {
                    db.People.Add(person);
                    people.Add(person);
                    report.Added("Person");
                }
                report.Unresolved.Add(title);
            }
            engineerColumns[c] = person;
        }
        await db.SaveChangesAsync(ct);

        foreach (var (col, person) in engineerColumns)
        {
            var title = ws.Text(header.Value, col);
            if (!aliases.Any(a => a.Alias == title && a.Source == "training"))
            {
                var alias = new PersonAlias { Alias = title, Kind = AliasKind.ShortName, Source = "training", PersonId = person.Id };
                db.PersonAliases.Add(alias);
                aliases.Add(alias);
            }
        }

        var modules = await db.Modules.ToListAsync(ct);
        var states = await db.PersonModules.ToDictionaryAsync(x => (x.PersonId, x.ModuleId), ct);
        var sort = 0;

        for (var r = header.Value + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var product = ws.Text(r, 2);
            var moduleName = ws.Text(r, 3);
            var sub = ws.Text(r, 4);
            if (product.Length == 0 && moduleName.Length == 0 && sub.Length == 0) continue;
            sort++;

            var module = modules.FirstOrDefault(m => m.Product == product && m.Name == moduleName && m.SubModule == sub);
            if (module is null)
            {
                module = new Module { Product = product, Name = moduleName, SubModule = sub, SortOrder = sort };
                db.Modules.Add(module);
                modules.Add(module);
                await db.SaveChangesAsync(ct);
                report.Added("Module");
            }
            else report.Unchanged("Module");

            foreach (var (col, person) in engineerColumns)
            {
                var text = ws.Text(r, col);
                if (text.Length == 0) continue;
                var state = ParseState(text);
                if (state is null) { report.Warnings.Add($"Module row {r}: unknown state '{text}' for {person.FullName}; skipped."); continue; }

                if (states.TryGetValue((person.Id, module.Id), out var existing))
                {
                    if (existing.State == state) report.Unchanged("KnowledgeState");
                    else { existing.State = state.Value; report.Updated("KnowledgeState"); }
                }
                else
                {
                    var created = new PersonModule { PersonId = person.Id, ModuleId = module.Id, State = state.Value };
                    db.PersonModules.Add(created);
                    states[(person.Id, module.Id)] = created;
                    report.Added("KnowledgeState");
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return new PersonMatcher(people, aliases);
    }

    private async Task ImportDoubtsAsync(ClosedXML.Excel.XLWorkbook wb, ImportReport report, PersonMatcher matcher, CancellationToken ct)
    {
        var ws = wb.Sheet("Need to Discuss");
        if (ws is null) { report.Warnings.Add("Sheet 'Need to Discuss' not found."); return; }
        var header = ws.FindHeaderRow("Sr No", 5);
        if (header is null) { report.Warnings.Add("Need to Discuss: header row 'Sr No' not found."); return; }

        var existing = await db.Doubts.ToDictionaryAsync(d => d.SourceRow, ct);
        for (var r = header.Value + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            if (ws.Number(r, 1) is not { } srNo) continue;
            var sr = (int)srNo;
            var raisedBy = ws.Text(r, 6);
            var assignedTo = ws.Text(r, 8);

            var doubt = new Doubt
            {
                SourceRow = sr,
                Module = ws.Text(r, 2), SubModule = ws.Text(r, 3), Text = ws.Text(r, 4),
                RaisedOn = ws.Date(r, 5), RaisedByName = Nullable(raisedBy), RaisedByPersonId = matcher.Resolve(raisedBy)?.Id,
                AssignedOn = ws.Date(r, 7), AssignedToName = Nullable(assignedTo), AssignedToPersonId = matcher.Resolve(assignedTo)?.Id,
                SolvedOn = ws.Date(r, 9), Result = ParseResult(ws.Text(r, 10)), Remarks = Nullable(ws.Text(r, 11)),
            };

            if (!existing.TryGetValue(sr, out var current)) { db.Doubts.Add(doubt); report.Added("Doubt"); continue; }

            var same = current.Module == doubt.Module && current.SubModule == doubt.SubModule && current.Text == doubt.Text
                       && current.RaisedOn == doubt.RaisedOn && current.RaisedByName == doubt.RaisedByName
                       && current.AssignedOn == doubt.AssignedOn && current.AssignedToName == doubt.AssignedToName
                       && current.SolvedOn == doubt.SolvedOn && current.Result == doubt.Result && current.Remarks == doubt.Remarks;
            if (same) { report.Unchanged("Doubt"); continue; }

            current.Module = doubt.Module; current.SubModule = doubt.SubModule; current.Text = doubt.Text;
            current.RaisedOn = doubt.RaisedOn; current.RaisedByName = doubt.RaisedByName; current.RaisedByPersonId = doubt.RaisedByPersonId;
            current.AssignedOn = doubt.AssignedOn; current.AssignedToName = doubt.AssignedToName; current.AssignedToPersonId = doubt.AssignedToPersonId;
            current.SolvedOn = doubt.SolvedOn; current.Result = doubt.Result; current.Remarks = doubt.Remarks;
            report.Updated("Doubt");
        }
        await db.SaveChangesAsync(ct);
    }

    public static KnowledgeState? ParseState(string text)
    {
        var s = text.Trim().ToLowerInvariant().Replace('’', '\'');
        if (s.StartsWith("need", StringComparison.Ordinal)) return KnowledgeState.NeedToDiscuss;
        if (s is "know") return KnowledgeState.Know;
        if (s is "don't know" or "dont know" or "do not know") return KnowledgeState.DontKnow;
        return null;
    }

    public static DoubtResult ParseResult(string text) => text.Trim().ToLowerInvariant() switch
    {
        "solved" => DoubtResult.Solved,
        "assign" or "assigned" => DoubtResult.Assigned,
        "hold" => DoubtResult.Hold,
        _ => DoubtResult.Open,
    };

    private static string? Nullable(string s) => s.Length == 0 ? null : s;
}
