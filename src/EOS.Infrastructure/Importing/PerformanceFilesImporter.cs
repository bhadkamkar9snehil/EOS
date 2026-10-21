using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using EOS.Domain.Capacity;
using EOS.Domain.People;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public enum ErpFileKind { Roster, ProjectMaster, MonthlySummary, DetailedTimesheet, TimesheetApproval, Attendance, TaskList, ReviewWorkbook }

/// <summary>People lookup shared by the file importers: exact name, code, then aliases and close spellings.</summary>
internal sealed class PeopleIndex
{
    private readonly PersonMatcher _matcher;
    private readonly Dictionary<string, Person> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Person> _byCode = new(StringComparer.OrdinalIgnoreCase);

    public List<Person> People { get; }

    private PeopleIndex(List<Person> people, List<PersonAlias> aliases)
    {
        People = people;
        _matcher = new PersonMatcher(people, aliases);
        foreach (var p in people) Register(p);
    }

    public static async Task<PeopleIndex> LoadAsync(EosDbContext db, CancellationToken ct) =>
        new(await db.People.ToListAsync(ct), await db.PersonAliases.AsNoTracking().ToListAsync(ct));

    public void Register(Person p)
    {
        _byName[PersonMatcher.Normalise(p.FullName)] = p;
        if (p.Code.Length > 0) _byCode[p.Code] = p;
    }

    public Person? Find(string? name, string? code = null)
    {
        if (!string.IsNullOrWhiteSpace(code) && _byCode.TryGetValue(code.Trim(), out var byCode)) return byCode;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(PersonMatcher.Normalise(name), out var p) ? p : _matcher.Resolve(name);
    }

    public bool CodeTaken(string code) => _byCode.ContainsKey(code);
}

/// <summary>
/// Imports every ERP export EOS knows, auto-detected from the file contents, with the reading rules the old EOS (V1) used:
/// employee roster, project master, monthly utilisation summary, detailed timesheet transactions, timesheet approval report,
/// attendance/leave/UAA export (also fills leave), project task list, and engineer review workbooks.
/// Accepts a single .xlsx or a .zip holding any mix of them.
/// </summary>
public sealed class PerformanceFilesImporter(EosDbContext db)
{
    private readonly Dictionary<(int Person, int Year, int Month), MonthlyPerformance> _perf = [];
    private readonly HashSet<(int Person, int Year, int Month)> _touched = [];
    private readonly HashSet<string> _unresolved = new(StringComparer.OrdinalIgnoreCase);
    private PerformanceWeights _weights = null!;

    public Task<ImportReport> ImportAsync(string path, bool commit, string actor, int? fallbackYear = null, int? fallbackMonth = null, CancellationToken ct = default) =>
        ImportRunner.RunAsync(db, "ERP report files", path, commit, actor, report => WorkAsync(path, (fallbackYear, fallbackMonth), report, ct), ct);

    public static ErpFileKind? Detect(string path)
    {
        using var wb = WorkbookReading.Open(path);
        return Detect(wb);
    }

    private static ErpFileKind? Detect(XLWorkbook wb)
    {
        var ws = wb.Worksheets.First();
        var lastRow = Math.Min(10, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastCol = Math.Min(40, ws.LastColumnUsed()?.ColumnNumber() ?? 1);
        var rows = Enumerable.Range(1, lastRow)
            .Select(r => Enumerable.Range(1, lastCol).Select(c => ws.Text(r, c)).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();
        if (rows.Any(x => x.Contains("Punch Duration") && x.Contains("UAA Status"))) return ErpFileKind.Attendance;
        if (rows.Any(x => x.Contains("Total Month Hours") && x.Contains("Utilization"))) return ErpFileKind.MonthlySummary;
        if (rows.Any(x => x.Contains("Project No") && x.Contains("Total work Hours"))) return ErpFileKind.DetailedTimesheet;
        if (rows.Any(x => x.Contains("Filled Date") && x.Contains("Total Task Hours") && x.Contains("Hrs") && x.Contains("Project No"))) return ErpFileKind.TimesheetApproval;
        if (ErpTaskListImporter.Matches(rows)) return ErpFileKind.TaskList;
        if (wb.Sheet("Template Metadata") is not null) return ErpFileKind.ReviewWorkbook;
        if (rows.Any(x => x.Contains("Employee No") && x.Contains("Full Name") && x.Contains("Band Level"))) return ErpFileKind.Roster;
        if (wb.Sheet("ProjectList") is not null || rows.Any(x => x.Contains("Project No") && x.Contains("Project Name") && x.Contains("Order Amount") && x.Contains("Main Status"))) return ErpFileKind.ProjectMaster;
        return null;
    }

    private async Task WorkAsync(string path, (int? Year, int? Month) fallback, ImportReport report, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"eos-files-{Guid.NewGuid():N}");
        try
        {
            var files = new List<string>();
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(path, temp);
                files.AddRange(Directory.EnumerateFiles(temp, "*.xlsx", SearchOption.AllDirectories).Where(f => !Path.GetFileName(f).StartsWith("~$")).Order());
            }
            else files.Add(path);

            _weights = await PerformanceWeights.LoadAsync(db, ct);
            var idx = await PeopleIndex.LoadAsync(db, ct);
            var perfRows = await db.MonthlyPerformance.ToListAsync(ct);
            foreach (var m in perfRows) _perf[(m.PersonId, m.Year, m.Month)] = m;

            var typed = new List<(string File, ErpFileKind Kind)>();
            foreach (var f in files)
            {
                ErpFileKind? kind;
                try { kind = Detect(f); }
                catch (Exception ex) when (ex is InvalidDataException or IOException) { report.Warnings.Add($"{Path.GetFileName(f)}: cannot be read ({ex.Message})."); continue; }
                if (kind is null) report.Warnings.Add($"{Path.GetFileName(f)}: not a recognised ERP report (roster, project list, utilisation summary, timesheets, timesheet approval, attendance, task list or review workbook); skipped.");
                else typed.Add((f, kind.Value));
            }

            var calendar = await Configuration.ConfigurationService.LoadCalendarAsync(db, null, ct);
            foreach (var (f, _) in typed.Where(t => t.Kind == ErpFileKind.Roster)) ImportRoster(f, idx, report);
            await db.SaveChangesAsync(ct);
            foreach (var (f, _) in typed.Where(t => t.Kind == ErpFileKind.ProjectMaster)) await new ErpProjectImporter(db).WorkAsync(f, report, ct);

            var projectCounts = new List<(Person Person, int Year, int Month, int Projects)>();
            foreach (var (f, _) in typed.Where(t => t.Kind is ErpFileKind.DetailedTimesheet or ErpFileKind.TimesheetApproval))
                projectCounts.AddRange(await ImportTimesheetFileAsync(f, idx, report, ct));
            await db.SaveChangesAsync(ct);
            foreach (var (f, _) in typed.Where(t => t.Kind == ErpFileKind.TaskList)) await new ErpTaskListImporter(db).ImportAsync(f, idx, calendar, report, ct);
            foreach (var (person, y, m, n) in projectCounts) { var row = Perf(person, y, m, report); row.UniqueProjects = n; _weights.Apply(row); }

            foreach (var (f, _) in typed.Where(t => t.Kind == ErpFileKind.MonthlySummary)) ImportSummary(f, idx, fallback, report);
            foreach (var (f, _) in typed.Where(t => t.Kind == ErpFileKind.Attendance)) await ImportAttendanceAsync(f, idx, calendar, report, ct);
            foreach (var (f, _) in typed.Where(t => t.Kind == ErpFileKind.ReviewWorkbook)) await ImportReviewAsync(f, idx, report, ct);

            await LinkTimesheetsToProjectsAsync(report, ct);
            report.Unresolved.AddRange(_unresolved.Order());
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    /// <summary>Timesheet entries imported before their project existed get linked once it does.</summary>
    private async Task LinkTimesheetsToProjectsAsync(ImportReport report, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var ids = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.ProjectNo, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var e in await db.Timesheets.Where(t => t.ProjectId == null && t.ProjectNo != null).ToListAsync(ct))
            if (ids.TryGetValue(e.ProjectNo!, out var id)) { e.ProjectId = id; report.Updated("Timesheet entry linked to project"); }
    }

    private MonthlyPerformance Perf(Person person, int year, int month, ImportReport report)
    {
        var key = (person.Id, year, month);
        if (!_perf.TryGetValue(key, out var row))
        {
            row = new MonthlyPerformance { PersonId = person.Id, Year = year, Month = month };
            db.MonthlyPerformance.Add(row); _perf[key] = row; _touched.Add(key);
            report.Added("Monthly performance");
        }
        else if (_touched.Add(key)) report.Updated("Monthly performance");
        return row;
    }

    // ---- header helpers (same tolerance as V1: header within the first 10 rows, matched case-insensitively) ----

    private static (int Row, Dictionary<string, int> Columns)? FindHeader(IXLWorksheet ws, string marker)
    {
        for (var r = 1; r <= Math.Min(10, ws.LastRowUsed()?.RowNumber() ?? 1); r++)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in ws.Row(r).CellsUsed()) cols.TryAdd(c.GetString().Replace(' ', ' ').Trim(), c.Address.ColumnNumber);
            if (cols.ContainsKey(marker)) return (r, cols);
        }
        return null;
    }

    private static bool Missing(Dictionary<string, int> cols, string file, ImportReport report, params string[] required)
    {
        var absent = required.Where(c => !cols.ContainsKey(c)).ToList();
        if (absent.Count == 0) return false;
        report.Warnings.Add($"{Path.GetFileName(file)}: required column(s) not found: {string.Join(", ", absent.Select(a => a.Replace("\n", " ")))}; skipped.");
        return true;
    }

    private static bool Flag(IXLWorksheet ws, int r, int c)
    {
        var cell = ws.Cell(r, c);
        if (cell.TryGetValue<bool>(out var b)) return b;
        var t = ws.Text(r, c);
        return t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1";
    }

    private static decimal Num(IXLWorksheet ws, int r, int c) => ws.Number(r, c) ?? 0;

    // ---- roster ----

    private void ImportRoster(string file, PeopleIndex idx, ImportReport report)
    {
        using var wb = WorkbookReading.Open(file);
        var ws = wb.Worksheets.First();
        if (FindHeader(ws, "Employee No") is not { } h || Missing(h.Columns, file, report, "Full Name", "Band Level")) return;
        var (hr, cols) = h;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var last = ws.LastRowUsed()?.RowNumber() ?? hr;
        for (var r = hr + 1; r <= last; r++)
        {
            var code = ws.Text(r, cols["Employee No"]); var name = ws.Text(r, cols["Full Name"]);
            if (code.Length == 0 || name.Length == 0) continue;     // "Admin Reporting: X" group header rows
            var band = ws.Text(r, cols["Band Level"]);
            var digits = new string(band.Where(char.IsDigit).ToArray());
            var level = int.TryParse(digits, out var parsed) && parsed is >= 1 and <= 99 ? parsed : 1;
            var email = cols.TryGetValue("Official Email", out var ec) ? ws.Text(r, ec).ToLowerInvariant() : "";
            var consultant = cols.TryGetValue("IsConsultant", out var cc) && Flag(ws, r, cc);
            // The probation flag lingers in the ERP export; the completion date is the reliable signal.
            var probationFlag = cols.TryGetValue("Flg Probation", out var pc) && Flag(ws, r, pc);
            DateOnly? probationEnd = cols.TryGetValue("Probation Complete Date", out var pd) ? ws.Date(r, pd) : null;
            var probation = probationFlag && (probationEnd is null || probationEnd >= today);

            var person = idx.Find(name, code);
            if (person is null)
            {
                person = new Person { Code = code, FullName = name, IsActive = true };
                db.People.Add(person); idx.People.Add(person); idx.Register(person);
                report.Added("Person");
            }
            var designation = cols.TryGetValue("Designation", out var dc) ? ws.Text(r, dc) : "";
            var before = (person.Seniority, person.Email, person.IsConsultant, person.IsOnProbation, person.Band, person.RoleTitle);
            if (designation.Length > 0 && string.IsNullOrWhiteSpace(person.RoleTitle)) person.RoleTitle = designation;
            person.Seniority = level;
            if (email.Length > 0) person.Email = email;
            person.IsConsultant = consultant; person.IsOnProbation = probation;
            if (band.Length > 0) person.Band = band;
            if (person.Id == 0) continue;
            if (before == (person.Seniority, person.Email, person.IsConsultant, person.IsOnProbation, person.Band, person.RoleTitle)) report.Unchanged("Person");
            else report.Updated("Person");
        }
    }

    // ---- detailed timesheet transactions ----

    /// <summary>
    /// Replaces, per person in the file, every stored entry inside the file's date window with the entries in the file.
    /// An entry edited in the ERP after an earlier export therefore replaces its old row instead of doubling it.
    /// </summary>
    internal async Task<IReadOnlyList<(Person Person, int Year, int Month, int Projects)>> ImportTimesheetFileAsync(
        string file, PeopleIndex idx, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(file);
        var ws = wb.Worksheets.First();
        if (FindHeader(ws, "Employee") is not { } h) { report.Warnings.Add($"{Path.GetFileName(file)}: no Employee column; skipped."); return []; }
        var (hr, cols) = h;
        // The timesheet approval report names the same columns "Start Date" and "Hrs".
        var dateHeader = cols.ContainsKey("Date") ? "Date" : "Start Date";
        var hoursHeader = cols.ContainsKey("Total work Hours") ? "Total work Hours" : "Hrs";
        if (Missing(cols, file, report, dateHeader, hoursHeader)) return [];

        var projects = await db.Projects.AsNoTracking().Select(p => new { p.Id, p.ProjectNo }).ToListAsync(ct);
        var byNo = projects.ToDictionary(p => p.ProjectNo, p => p.Id, StringComparer.OrdinalIgnoreCase);
        int? ProjectId(string no)
        {
            if (no.Length == 0) return null;
            if (byNo.TryGetValue(no, out var id)) return id;
            // Variant codes such as V_PRO2602059_2 carry the ERP number inside them.
            var hits = projects.Where(p => p.ProjectNo.Length >= 6 && no.Contains(p.ProjectNo, StringComparison.OrdinalIgnoreCase)).ToList();
            return hits.Count == 1 ? hits[0].Id : null;
        }

        string Get(int r, string h) => cols.TryGetValue(h, out var c) ? ws.Text(r, c) : "";
        var incoming = new List<(Person Person, TimesheetEntry Entry)>();
        var former = new Dictionary<string, Person>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var last = ws.LastRowUsed()?.RowNumber() ?? hr;
        for (var r = hr + 1; r <= last; r++)
        {
            var name = Get(r, "Employee");
            var date = ws.Date(r, cols[dateHeader]);
            var hours = ws.Number(r, cols[hoursHeader]);
            if (name.Length == 0 || date is null || hours is null || hours <= 0) continue;

            var norm = PersonMatcher.Normalise(name);
            var person = idx.Find(name);
            if (person is null && !former.TryGetValue(norm, out person))
            {
                var n = idx.People.Count + 1; string code;
                do { code = $"H{n++:000}"; } while (idx.CodeTaken(code));
                person = new Person { Code = code, FullName = name, IsActive = false, IsUnverified = true };
                db.People.Add(person); idx.People.Add(person); idx.Register(person); former[norm] = person;
                report.Added("Former engineer");
            }

            var projectNo = Get(r, "Project No"); var task = Get(r, "Task"); var activity = Get(r, "Activity"); var milestone = Get(r, "Milestone");
            var key = Hash($"{norm}|{date:O}|{Get(r, "Filled Date")}|{projectNo}|{hours}|{task}|{activity}|{milestone}|{Get(r, "Start Time")}");
            if (!seen.Add(key)) { report.Unchanged("Timesheet entry"); continue; }
            incoming.Add((person, new TimesheetEntry
            {
                Key = key, Date = date.Value, Hours = hours.Value,
                ProjectNo = projectNo.Length == 0 ? null : projectNo, ProjectName = NullIfEmpty(Get(r, "Project")), ProjectId = ProjectId(projectNo),
                Activity = NullIfEmpty(activity), Milestone = NullIfEmpty(milestone), Task = NullIfEmpty(task), TaskType = NullIfEmpty(Get(r, "Task Type")),
                Band = NullIfEmpty(Get(r, "Bandlevel")),
            }));
        }
        if (incoming.Count == 0) return [];

        await db.SaveChangesAsync(ct);   // gives people created above their ids

        var from = incoming.Min(x => x.Entry.Date); var to = incoming.Max(x => x.Entry.Date);
        var personIds = incoming.Select(x => x.Person.Id).Distinct().ToList();
        var existing = await db.Timesheets.Where(t => personIds.Contains(t.PersonId) && t.Date >= from && t.Date <= to).ToListAsync(ct);
        var incomingKeys = incoming.Select(x => x.Entry.Key).ToHashSet(StringComparer.Ordinal);
        var existingKeys = existing.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var e in existing)
        {
            if (incomingKeys.Contains(e.Key)) report.Unchanged("Timesheet entry");
            else { db.Timesheets.Remove(e); report.Updated("Timesheet entry replaced"); }
        }
        foreach (var (person, entry) in incoming.Where(x => !existingKeys.Contains(x.Entry.Key)))
        {
            entry.PersonId = person.Id;
            db.Timesheets.Add(entry);
            report.Added("Timesheet entry");
        }
        foreach (var g in incoming.Where(x => x.Entry.Band is not null).GroupBy(x => x.Person.Id))
            g.First().Person.Band = g.OrderByDescending(x => x.Entry.Date).First().Entry.Band;

        return incoming.GroupBy(x => (x.Person, x.Entry.Date.Year, x.Entry.Date.Month))
            .Select(g => (g.Key.Person, g.Key.Year, g.Key.Month, g.Select(x => x.Entry.ProjectNo).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .ToList();
    }

    // ---- monthly utilisation summary ----

    private void ImportSummary(string file, PeopleIndex idx, (int? Year, int? Month) fallback, ImportReport report)
    {
        using var wb = WorkbookReading.Open(file);
        var ws = wb.Worksheets.First();
        if (FindHeader(ws, "Employee Name") is not { } h
            || Missing(h.Columns, file, report, "Timsheet Compliance hours", "Total\nEntered Timesheet Hours", "Approved Timesheet Hours", "Billable Hours",
                "Non Billable Hours", "Sum of Training", "Sum of Office Working Hours", "Utilization")) return;
        var (hr, cols) = h;

        // The export is one aggregate for the period the ERP was asked for, stated in a "Month : a to b" header.
        // A period crossing a month boundary cannot be attributed to one month, so it is refused rather than guessed.
        int year, month;
        var label = ws.Text(1, 1); var range = ws.Text(1, 2).Split(" to ", StringSplitOptions.TrimEntries);
        if (label.Contains("Month", StringComparison.OrdinalIgnoreCase) && range.Length == 2
            && WorkbookReading.ParseDateText(range[0]) is { } start && WorkbookReading.ParseDateText(range[1]) is { } end)
        {
            if (start.Year != end.Year || start.Month != end.Month)
            {
                report.Warnings.Add($"{Path.GetFileName(file)}: this utilisation export covers {range[0]} to {range[1]}, more than one month. Re-export a single month; a multi-month summary cannot be split into monthly figures.");
                return;
            }
            (year, month) = (start.Year, start.Month);
        }
        else if (fallback is { Year: { } fy, Month: { } fm }) (year, month) = (fy, fm);
        else
        {
            report.Warnings.Add($"{Path.GetFileName(file)}: the reporting month is not stated in the file. Pass the month (for example --month 2026-10).");
            return;
        }

        var last = ws.LastRowUsed()?.RowNumber() ?? hr;
        for (var r = hr + 1; r <= last; r++)
        {
            var name = ws.Text(r, cols["Employee Name"]);
            if (name.Length == 0 || name.Equals("No data available", StringComparison.OrdinalIgnoreCase)) continue;
            var person = idx.Find(name);
            if (person is null || person.Id == 0) { _unresolved.Add(name); continue; }
            var row = Perf(person, year, month, report);
            row.ComplianceHours = Num(ws, r, cols["Timsheet Compliance hours"]);
            row.EnteredHours = Num(ws, r, cols["Total\nEntered Timesheet Hours"]);
            row.ApprovedHours = Num(ws, r, cols["Approved Timesheet Hours"]);
            row.BillableHours = Num(ws, r, cols["Billable Hours"]);
            row.NonBillableHours = Num(ws, r, cols["Non Billable Hours"]);
            row.TrainingHours = Num(ws, r, cols["Sum of Training"]);
            row.OfficeHours = Num(ws, r, cols["Sum of Office Working Hours"]);
            row.Utilization = Num(ws, r, cols["Utilization"]) * 100m;   // exported as a percentage-formatted fraction
            _weights.Apply(row);
        }
    }

    // ---- attendance / leave / UAA ----

    private async Task ImportAttendanceAsync(string file, PeopleIndex idx, WorkCalendar calendar, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(file);
        var ws = wb.Worksheets.First();
        if (FindHeader(ws, "Date") is not { } h
            || Missing(h.Columns, file, report, "Employee", "Emp No", "Attend Day", "Position", "Duty Type", "Leave status", "Punch Duration", "Timesheet Hrs",
                "Timesheet", "Flg Punch not Found", "Flg Late Coming", "Flg Early Going", "Flg Less Duration")) return;
        var (hr, cols) = h;

        var groups = new Dictionary<(Person Person, int Year, int Month), MonthlyPerformance>();
        var leaveDays = new List<(Person Person, DateOnly Date, string Kind, decimal Fraction, string? Note)>();
        var people = new HashSet<Person>();
        DateOnly? first = null, lastDay = null;
        var last = ws.LastRowUsed()?.RowNumber() ?? hr;
        for (var r = hr + 1; r <= last; r++)
        {
            var name = ws.Text(r, cols["Employee"]);
            if (name.Length == 0) continue;
            var person = idx.Find(name, ws.Text(r, cols["Emp No"]));
            if (person is null || person.Id == 0) { _unresolved.Add(name); continue; }
            var date = ws.Date(r, cols["Date"]);
            if (date is null) continue;
            people.Add(person);
            if (first is null || date < first) first = date;
            if (lastDay is null || date > lastDay) lastDay = date;
            var key = (person, date.Value.Year, date.Value.Month);
            if (!groups.TryGetValue(key, out var item)) groups[key] = item = new MonthlyPerformance();

            var attendDay = Num(ws, r, cols["Attend Day"]);
            var position = ws.Text(r, cols["Position"]); var duty = ws.Text(r, cols["Duty Type"]);
            var approvedLeave = ws.Text(r, cols["Leave status"]).Equals("Approved", StringComparison.OrdinalIgnoreCase);
            // Sunday is always a weekly off whatever the ERP tags it as; Saturday is accountable as a half day.
            var weekOff = date.Value.DayOfWeek == DayOfWeek.Sunday || duty.Equals("woff", StringComparison.OrdinalIgnoreCase);
            var weight = calendar.HoursOn(date.Value) is var hours && hours > 0 ? hours / calendar.HoursPerDay : 1m;
            // A week-off row never draws down leave, even when the ERP also tags it Position="Leave".
            if (!weekOff && (approvedLeave || position.Equals("Leave", StringComparison.OrdinalIgnoreCase))) item.LeaveDays += attendDay;
            if (!weekOff && approvedLeave)
            {
                var type = cols.TryGetValue("Leave Type", out var lt) ? ws.Text(r, lt) : "";
                var half = cols.TryGetValue("Leave Day Type", out var ld) && ws.Text(r, ld) is { Length: > 0 } dayType && !dayType.Equals("None", StringComparison.OrdinalIgnoreCase);
                var note = cols.TryGetValue("Leave Remarks", out var lr) ? ws.Text(r, lr) : "";
                leaveDays.Add((person, date.Value, type.Length == 0 ? "Leave" : type, half ? .5m : 1m, note.Length == 0 ? null : note));
            }
            if (!(attendDay > 0 && !weekOff && !position.Equals("Leave", StringComparison.OrdinalIgnoreCase))) continue;
            item.AttendanceDays += attendDay * weight;
            item.ExpectedTimesheetDays += weight;
            item.PunchHours += Num(ws, r, cols["Punch Duration"]);
            if (ws.Text(r, cols["Timesheet"]).Equals("Filled", StringComparison.OrdinalIgnoreCase)) item.TimesheetFilledDays += weight;
            if (Flag(ws, r, cols["Flg Punch not Found"])) item.MissingPunchDays++;
            if (Flag(ws, r, cols["Flg Late Coming"])) item.LateDays++;
            if (Flag(ws, r, cols["Flg Early Going"])) item.EarlyDays++;
            if (Flag(ws, r, cols["Flg Less Duration"])) item.LessDurationDays++;
        }

        foreach (var ((person, y, m), g) in groups)
        {
            var row = Perf(person, y, m, report);
            row.AttendanceDays = g.AttendanceDays; row.LeaveDays = g.LeaveDays; row.PunchHours = g.PunchHours;
            row.TimesheetFilledDays = g.TimesheetFilledDays; row.ExpectedTimesheetDays = g.ExpectedTimesheetDays;
            row.MissingPunchDays = g.MissingPunchDays; row.LateDays = g.LateDays; row.EarlyDays = g.EarlyDays; row.LessDurationDays = g.LessDurationDays;
            _weights.Apply(row);
        }

        await ReplaceLeaveAsync(leaveDays, people, first, lastDay, calendar, report, ct);
    }

    /// <summary>Replaces the ERP-origin leave of the people in the file inside the file's date window; leave entered in EOS is never touched.</summary>
    private async Task ReplaceLeaveAsync(List<(Person Person, DateOnly Date, string Kind, decimal Fraction, string? Note)> days, HashSet<Person> people,
        DateOnly? first, DateOnly? last, WorkCalendar calendar, ImportReport report, CancellationToken ct)
    {
        if (first is null || last is null) return;
        var ids = people.Select(p => p.Id).ToList();
        var stored = await db.Leaves.Where(l => l.Origin == "erp" && ids.Contains(l.PersonId) && l.ToDate >= first && l.FromDate <= last).ToListAsync(ct);

        // Consecutive working days of the same kind and share become one leave range.
        var ranges = new List<Leave>();
        foreach (var g in days.GroupBy(d => (d.Person.Id, d.Kind, d.Fraction)))
        {
            Leave? open = null;
            foreach (var d in g.OrderBy(x => x.Date))
            {
                if (open is not null && calendar.After(open.ToDate) == d.Date) { open.ToDate = d.Date; continue; }
                open = new Leave { PersonId = g.Key.Id, FromDate = d.Date, ToDate = d.Date, Kind = g.Key.Kind, Fraction = g.Key.Fraction, Origin = "erp", Note = d.Note };
                ranges.Add(open);
            }
        }

        bool Same(Leave a, Leave b) => a.PersonId == b.PersonId && a.FromDate == b.FromDate && a.ToDate == b.ToDate && a.Kind == b.Kind && a.Fraction == b.Fraction;
        foreach (var old in stored)
        {
            if (ranges.Any(n => Same(old, n))) report.Unchanged("Leave");
            else { db.Leaves.Remove(old); report.Updated("Leave replaced"); }
        }
        foreach (var n in ranges.Where(n => !stored.Any(o => Same(o, n)))) { db.Leaves.Add(n); report.Added("Leave"); }
    }

    // ---- engineer review workbooks (peer reviews) ----

    private async Task ImportReviewAsync(string file, PeopleIndex idx, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(file);
        var meta = wb.Sheet("Template Metadata"); var sheet = wb.Sheet("Peer Review");
        if (meta is null || sheet is null) { report.Warnings.Add($"{Path.GetFileName(file)}: no Peer Review sheet; skipped."); return; }

        string Meta(string key)
        {
            for (var r = 1; r <= (meta.LastRowUsed()?.RowNumber() ?? 0); r++)
                if (meta.Text(r, 1).Equals(key, StringComparison.OrdinalIgnoreCase)) return meta.Text(r, 2);
            return "";
        }
        var reviewerCode = Meta("EmployeeCode"); var reviewerName = Meta("EmployeeName");
        if (reviewerCode.Length == 0) return;
        if (!int.TryParse(Meta("Year"), out var year) || !int.TryParse(Meta("Month"), out var month))
        { report.Warnings.Add($"{Path.GetFileName(file)}: the review workbook has no valid reporting month in Template Metadata; skipped."); return; }

        var reviewer = idx.Find(reviewerName, reviewerCode);
        var existing = await db.PeerReviews.Where(p => p.Year == year && p.Month == month).ToListAsync(ct);
        var mine = existing.Where(p => (reviewer is not null && p.ReviewerPersonId == reviewer.Id)
            || PersonMatcher.Normalise(p.ReviewerName) == PersonMatcher.Normalise(reviewerName)).ToList();
        var kept = new HashSet<int>();

        const int headerRow = 6;
        decimal Rating(int r, int c) => Num(sheet, r, c) is >= 1 and <= 5 and var v ? v : 0;
        for (var r = headerRow + 1; r <= (sheet.LastRowUsed()?.RowNumber() ?? headerRow); r++)
        {
            var code = sheet.Text(r, 1); var name = sheet.Text(r, 2);
            if (code.Length == 0 && name.Length == 0) continue;
            decimal[] ratings = [Rating(r, 3), Rating(r, 4), Rating(r, 5), Rating(r, 6)];
            if (ratings.All(x => x == 0)) continue;      // a row the reviewer left blank is not feedback
            var subject = idx.Find(name, code);
            if (subject is null || subject.Id == 0) { _unresolved.Add(name.Length > 0 ? name : code); continue; }
            var comment = sheet.Text(r, 7);

            var row = mine.FirstOrDefault(p => p.SubjectPersonId == subject.Id);
            if (row is null)
            {
                row = new PeerReview { Year = year, Month = month, SubjectPersonId = subject.Id };
                db.PeerReviews.Add(row);
                report.Added("Peer review");
            }
            else report.Updated("Peer review");
            row.ReviewerName = reviewerName; row.ReviewerPersonId = reviewer?.Id;
            row.Collaboration = ratings[0]; row.Communication = ratings[1]; row.Reliability = ratings[2]; row.TechnicalHelp = ratings[3];
            row.Comment = comment.Length == 0 ? null : comment;
            kept.Add(subject.Id);
        }
        // Merge mode: a reviewer's earlier rows for the month that the new workbook no longer rates are dropped.
        foreach (var gone in mine.Where(p => !kept.Contains(p.SubjectPersonId))) { db.PeerReviews.Remove(gone); report.Updated("Peer review removed"); }
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
}
