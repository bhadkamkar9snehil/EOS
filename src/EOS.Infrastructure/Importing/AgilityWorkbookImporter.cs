using EOS.Domain.Audit;
using EOS.Domain.People;
using EOS.Domain.Skills;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed class AgilityWorkbookImporter(EosDbContext db)
{
    private const string ImportSource = "import:agility";

    public async Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default)
    {
        var report = new ImportReport("agility workbook", path, commit);
        using var wb = WorkbookReading.Open(path);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await ImportEngineersAsync(wb, report, ct);
        await ImportSkillsAsync(wb, report, ct);
        await ImportMatrixAsync(wb, report, actor, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = commit ? "import.commit" : "import.preview",
            SubjectKind = "agility-workbook", SubjectId = Path.GetFileName(path),
        });
        await db.SaveChangesAsync(ct);
        if (commit) await tx.CommitAsync(ct);
        else { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); }
        return report;
    }

    private async Task ImportEngineersAsync(ClosedXML.Excel.XLWorkbook wb, ImportReport report, CancellationToken ct)
    {
        var ws = wb.Sheet("Engineers");
        if (ws is null) { report.Warnings.Add("Sheet 'Engineers' not found."); return; }
        var header = ws.FindHeaderRow("Emp ID");
        if (header is null) { report.Warnings.Add("Engineers: header row 'Emp ID' not found."); return; }

        var teams = await db.Teams.ToDictionaryAsync(t => t.Name, StringComparer.OrdinalIgnoreCase, ct);
        var people = await db.People.Include(p => p.Team).ToListAsync(ct);

        for (var r = header.Value + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var code = ws.Text(r, 1);
            var name = ws.Text(r, 2);
            if (code.Length == 0 || name.Length == 0) continue;

            var teamName = ws.Text(r, 3);
            Team? team = null;
            if (teamName.Length > 0 && !teams.TryGetValue(teamName, out team))
            {
                team = new Team { Name = teamName };
                db.Teams.Add(team);
                teams[teamName] = team;
                report.Added("Team");
            }

            var person = people.FirstOrDefault(p => p.Code == code)
                         ?? people.FirstOrDefault(p => PersonMatcher.Normalise(p.FullName) == PersonMatcher.Normalise(name));
            var role = ws.Text(r, 4);
            var location = ws.Text(r, 5);
            var availability = ws.Number(r, 6);

            if (person is null)
            {
                person = new Person
                {
                    Code = code, FullName = name, Team = team, RoleTitle = Nullable(role), BaseLocation = Nullable(location),
                    AvailabilityFraction = Clamp01(availability ?? 1m),
                    IsUnverified = true,
                };
                db.People.Add(person);
                people.Add(person);
                report.Added("Person");
                continue;
            }

            var changed = false;
            if (person.Code != code && person.Code.StartsWith("TR-", StringComparison.Ordinal)) { person.Code = code; changed = true; }
            if (person.FullName != name) { person.FullName = name; changed = true; }
            if (team is not null && person.Team != team) { person.Team = team; changed = true; }
            if (person.IsUnverified)
            {
                if (person.RoleTitle != Nullable(role)) { person.RoleTitle = Nullable(role); changed = true; }
                if (person.BaseLocation != Nullable(location)) { person.BaseLocation = Nullable(location); changed = true; }
                if (availability is not null && person.AvailabilityFraction != Clamp01(availability.Value))
                { person.AvailabilityFraction = Clamp01(availability.Value); changed = true; }
            }
            if (changed) report.Updated("Person"); else report.Unchanged("Person");
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportSkillsAsync(ClosedXML.Excel.XLWorkbook wb, ImportReport report, CancellationToken ct)
    {
        var ws = wb.Sheet("Skills");
        if (ws is null) { report.Warnings.Add("Sheet 'Skills' not found."); return; }
        var header = ws.FindHeaderRow("Skill ID");
        if (header is null) { report.Warnings.Add("Skills: header row 'Skill ID' not found."); return; }

        var skills = await db.Skills.ToDictionaryAsync(s => s.Code, StringComparer.OrdinalIgnoreCase, ct);
        var order = 0;
        for (var r = header.Value + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var code = ws.Text(r, 1);
            var name = ws.Text(r, 2);
            if (code.Length == 0 || name.Length == 0) continue;
            order++;
            var category = Nullable(ws.Text(r, 3));
            var description = Nullable(ws.Text(r, 4));

            if (!skills.TryGetValue(code, out var skill))
            {
                skill = new Skill { Code = code, Name = name, Category = category, Description = description, SortOrder = order };
                db.Skills.Add(skill);
                skills[code] = skill;
                report.Added("Skill");
            }
            else if (skill.Name != name || skill.Category != category || skill.Description != description)
            {
                skill.Name = name; skill.Category = category; skill.Description = description;
                report.Updated("Skill");
            }
            else report.Unchanged("Skill");
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportMatrixAsync(ClosedXML.Excel.XLWorkbook wb, ImportReport report, string actor, CancellationToken ct)
    {
        var ws = wb.Sheet("Skill_Matrix");
        if (ws is null) { report.Warnings.Add("Sheet 'Skill_Matrix' not found."); return; }
        var header = ws.FindHeaderRow("Emp ID");
        if (header is null) { report.Warnings.Add("Skill_Matrix: header row 'Emp ID' not found."); return; }

        var skills = await db.Skills.ToListAsync(ct);
        var people = await db.People.ToListAsync(ct);
        var current = await db.PersonSkills.ToDictionaryAsync(x => (x.PersonId, x.SkillId), ct);
        var lastSource = (await db.SkillTransactions.Where(t => t.Kind != SkillTransactionKind.EvidenceNote).ToListAsync(ct))
            .GroupBy(t => (t.PersonId, t.SkillId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.AtUtc).ThenByDescending(t => t.Id).First().Source);

        var columns = new Dictionary<int, Skill>();
        for (var c = 4; c <= ws.LastColumn(header.Value); c++)
        {
            var title = ws.Text(header.Value, c);
            if (title.Length == 0 || title.Equals("Avg Rating", StringComparison.OrdinalIgnoreCase)) break;
            var skill = skills.FirstOrDefault(s => string.Equals(s.Name, title, StringComparison.OrdinalIgnoreCase));
            if (skill is null) report.Warnings.Add($"Skill_Matrix: column '{title}' matches no skill; skipped.");
            else columns[c] = skill;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var r = header.Value + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var code = ws.Text(r, 1);
            var name = ws.Text(r, 2);
            if (name.Length == 0) continue;

            var person = people.FirstOrDefault(p => PersonMatcher.Normalise(p.FullName) == PersonMatcher.Normalise(name))
                         ?? people.FirstOrDefault(p => p.Code == code);
            if (person is null) { report.Unresolved.Add(name); continue; }
            if (code.Length > 0 && person.Code != code)
                report.Warnings.Add($"Skill_Matrix: '{name}' is {code} in the matrix but {person.Code} on the Engineers sheet; matched by name.");

            foreach (var (col, skill) in columns)
            {
                var value = ws.Number(r, col);
                if (value is null) continue;
                var rating = (int)value.Value;
                if (rating < 1) continue;

                if (current.TryGetValue((person.Id, skill.Id), out var existing))
                {
                    if (existing.Rating == rating) { report.Unchanged("SkillRating"); continue; }
                    if (lastSource.TryGetValue((person.Id, skill.Id), out var src) && src is not null && !src.StartsWith("import:", StringComparison.Ordinal))
                    {
                        report.Warnings.Add($"Kept manual rating for {person.FullName} / {skill.Name} ({existing.Rating}); file says {rating}.");
                        continue;
                    }
                    var old = existing.Rating;
                    existing.Rating = rating;
                    AddTransaction(person, skill, old, rating, actor);
                    report.Updated("SkillRating");
                }
                else
                {
                    var created = new PersonSkill
                    {
                        PersonId = person.Id, SkillId = skill.Id, Rating = rating, AssessedOn = today, AssessedBy = "initial import",
                    };
                    db.PersonSkills.Add(created);
                    current[(person.Id, skill.Id)] = created;
                    AddTransaction(person, skill, null, rating, actor);
                    report.Added("SkillRating");
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private void AddTransaction(Person person, Skill skill, int? old, int rating, string actor) =>
        db.SkillTransactions.Add(new SkillTransaction
        {
            PersonId = person.Id, SkillId = skill.Id, Kind = SkillTransactionKind.Assessment, OldRating = old, NewRating = rating,
            Reason = "Assessment loaded from the Operational Agility workbook", Source = ImportSource, ChangedBy = actor, AtUtc = DateTime.UtcNow,
        });

    private static string? Nullable(string s) => s.Length == 0 ? null : s;
    private static decimal Clamp01(decimal v) => Math.Clamp(v, 0m, 1m);
}
