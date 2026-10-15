using System.Security.Cryptography;
using System.Text;
using EOS.Domain.People;
using EOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed class LegacyEosImporter(EosDbContext db)
{
    public static string DefaultDatabasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EOS", "Data", "engineering-performance.db");

    public Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default) =>
        ImportRunner.RunAsync(db, "legacy eos", path, commit, actor, report => WorkAsync(path, report, ct), ct);

    private async Task WorkAsync(string path, ImportReport report, CancellationToken ct)
    {
        if (!File.Exists(path)) { report.Warnings.Add($"Old EOS database not found at {path}."); return; }
        var temp = Path.Combine(Path.GetTempPath(), $"eos-v1-{Guid.NewGuid():N}.db");
        File.Copy(path, temp, true);
        try
        {
            await using var con = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Pooling=False");
            await con.OpenAsync(ct);

            var people = await db.People.ToListAsync(ct);
            var aliases = await db.PersonAliases.AsNoTracking().ToListAsync(ct);
            var matcher = new PersonMatcher(people, aliases);
            var byName = new Dictionary<string, Person>(StringComparer.Ordinal);
            foreach (var p in people) byName[PersonMatcher.Normalise(p.FullName)] = p;
            var codes = people.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

            await ReadEmployeesAsync(con, people, matcher, byName, codes, report, ct);
            await db.SaveChangesAsync(ct);

            var files = new List<string>();
            await using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "select distinct StoredPath from imported_source_file where ReportType = 2";
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct)) files.Add(rd.GetString(0));
            }
            await ImportTimesheetsAsync(files, people, byName, codes, report, ct);
        }
        finally { try { File.Delete(temp); } catch (IOException) { } }
    }

    private async Task ReadEmployeesAsync(
        SqliteConnection con, List<Person> people, PersonMatcher matcher, Dictionary<string, Person> byName, HashSet<string> codes, ImportReport report, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "select EmployeeCode, Name, SeniorityLevel, IsActive, Email, IsConsultant, IsOnProbation, IsNonBillable from employee";
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var code = rd.GetString(0).Trim(); var name = rd.GetString(1).Trim();
            if (name.Length == 0) continue;
            var seniority = rd.IsDBNull(2) ? (int?)null : rd.GetInt32(2);
            var active = rd.GetInt32(3) != 0;
            var email = rd.IsDBNull(4) ? null : rd.GetString(4).Trim().ToLowerInvariant();
            var person = matcher.Resolve(name);
            if (person is null)
            {
                var first = PersonMatcher.Normalise(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
                var shortNamed = people.Where(p => !p.FullName.Trim().Contains(' ') && PersonMatcher.Normalise(p.FullName) == first).ToList();
                if (shortNamed.Count == 1) { person = shortNamed[0]; person.FullName = name; }
            }
            if (person is null)
            {
                var useCode = codes.Contains(code) ? $"V1-{code}" : code;
                person = new Person { Code = useCode, FullName = name, IsActive = active };
                db.People.Add(person); people.Add(person); codes.Add(useCode);
                report.Added("Person");
            }
            else report.Updated("Person");
            person.Email ??= email;
            person.Seniority = seniority;
            person.IsConsultant = rd.GetInt32(5) != 0; person.IsOnProbation = rd.GetInt32(6) != 0; person.IsNonBillable = rd.GetInt32(7) != 0;
            if (!active) person.IsActive = false;
            byName[PersonMatcher.Normalise(name)] = person;
        }
    }

    private async Task ImportTimesheetsAsync(
        List<string> files, List<Person> people, Dictionary<string, Person> byName, HashSet<string> codes, ImportReport report, CancellationToken ct)
    {
        var keys = (await db.Timesheets.AsNoTracking().Select(t => t.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.ProjectNo, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);
        var bands = new Dictionary<int, string>();
        var created = new Dictionary<string, Person>(StringComparer.Ordinal);
        var pending = new List<(Person Person, TimesheetEntry Entry)>();
        var missing = 0;

        foreach (var file in files.Where(File.Exists))
        {
            using var wb = WorkbookReading.Open(file);
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws is null) continue;
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= 25; c++) { var h = ws.Text(1, c).Trim(); if (h.Length > 0) cols[h] = c; }
            if (!cols.ContainsKey("Employee") || !cols.ContainsKey("Date") || !cols.ContainsKey("Total work Hours")) { report.Warnings.Add($"{Path.GetFileName(file)}: not a detailed timesheet export; skipped."); continue; }

            string Get(int r, string h) => cols.TryGetValue(h, out var c) ? ws.Text(r, c).Trim() : "";
            var last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (var r = 2; r <= last; r++)
            {
                var name = Get(r, "Employee");
                var date = cols.TryGetValue("Date", out var dc) ? ws.Date(r, dc) : null;
                var hours = cols.TryGetValue("Total work Hours", out var hc) ? ws.Number(r, hc) : null;
                if (name.Length == 0 || date is null || hours is null || hours <= 0) continue;

                var norm = PersonMatcher.Normalise(name);
                if (!byName.TryGetValue(norm, out var person))
                {
                    if (!created.TryGetValue(norm, out person))
                    {
                        var n = people.Count + 1; string code;
                        do { code = $"H{n++:000}"; } while (codes.Contains(code));
                        person = new Person { Code = code, FullName = name, IsActive = false, IsUnverified = true };
                        db.People.Add(person); people.Add(person); codes.Add(code); created[norm] = person; byName[norm] = person;
                        report.Added("Former engineer");
                    }
                }

                var projectNo = Get(r, "Project No");
                var task = Get(r, "Task"); var activity = Get(r, "Activity"); var milestone = Get(r, "Milestone");
                var key = Hash($"{norm}|{date:O}|{Get(r, "Filled Date")}|{projectNo}|{hours}|{task}|{activity}|{milestone}|{Get(r, "Start Time")}");
                if (!keys.Add(key)) { report.Unchanged("Timesheet entry"); continue; }

                var band = Get(r, "Bandlevel");
                var entry = new TimesheetEntry
                {
                    Key = key, Date = date.Value, Hours = hours.Value,
                    ProjectNo = projectNo.Length == 0 ? null : projectNo, ProjectName = NullIfEmpty(Get(r, "Project")),
                    ProjectId = projectNo.Length > 0 && projects.TryGetValue(projectNo, out var pid) ? pid : null,
                    Activity = NullIfEmpty(activity), Milestone = NullIfEmpty(milestone), Task = NullIfEmpty(task), TaskType = NullIfEmpty(Get(r, "Task Type")),
                    Band = NullIfEmpty(band),
                };
                pending.Add((person, entry));
                report.Added("Timesheet entry");
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var (person, entry) in pending) { entry.PersonId = person.Id; db.Timesheets.Add(entry); }
        foreach (var g in pending.Where(x => x.Entry.Band is not null).GroupBy(x => x.Person.Id))
            people.First(p => p.Id == g.Key).Band = g.OrderByDescending(x => x.Entry.Date).First().Entry.Band;
        if (missing > 0) report.Warnings.Add($"{missing} rows skipped.");
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
}
