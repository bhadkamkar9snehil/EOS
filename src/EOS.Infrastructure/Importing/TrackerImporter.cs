using System.Text.RegularExpressions;
using EOS.Domain.Configuration;
using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed partial class TrackerImporter(EosDbContext db)
{
    private static readonly HashSet<string> NonProjectSheets = new(StringComparer.OrdinalIgnoreCase) { "Master", "LPP", "AMC" };
    private static readonly HashSet<string> NotAPerson = new(StringComparer.OrdinalIgnoreCase) { "all", "user", "customer", "client", "team", "-", "na", "n/a", "tbd" };

    public Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default) =>
        ImportRunner.RunAsync(db, "task tracker workbook", path, commit, actor, report => WorkAsync(path, report, actor, ct), ct);

    private async Task WorkAsync(string path, ImportReport report, string actor, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(path);
        var people = await db.People.ToListAsync(ct);
        var aliases = await db.PersonAliases.ToListAsync(ct);
        var matcher = new PersonMatcher(people, aliases);
        var projects = await db.Projects.ToDictionaryAsync(p => p.ProjectNo, StringComparer.OrdinalIgnoreCase, ct);
        var cal = await ConfigurationService.LoadCalendarAsync(db, null, ct);
        var ctx = new Context(people, aliases, matcher, projects, cal, report);

        ImportProjectList(wb, ctx);
        await db.SaveChangesAsync(ct);

        var imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plans = new PlanService(db);
        foreach (var ws in wb.Worksheets.Where(s => !NonProjectSheets.Contains(s.Name)))
        {
            var projectId = await ImportPlanSheetAsync(ws, ctx, imported, ct);
            if (projectId is { } id) await plans.NormaliseAsync(id, ct);
        }

        var amc = wb.Sheet("AMC");
        if (amc is not null) await ImportAmcAsync(amc, ctx, ct);
        _ = actor;
    }

    private sealed record Context(List<Person> People, List<PersonAlias> Aliases, PersonMatcher Matcher,
        Dictionary<string, Project> Projects, WorkCalendar Calendar, ImportReport Report);

    private void ImportProjectList(ClosedXML.Excel.XLWorkbook wb, Context ctx)
    {
        var ws = wb.Sheet("LPP");
        if (ws is null) { ctx.Report.Warnings.Add("Sheet 'LPP' not found."); return; }
        var cols = ErpProjectImporter.HeaderMap(ws, 1);
        int Col(string name, int fallback) => cols.TryGetValue(name, out var c) ? c : fallback;
        var cName = Col("name", 3); var cProduct = Col("product name", 4); var cStatus = Col("status", 5);
        var cTitle = Col("project name", 6); var cTm = Col("technical manager", 7); var cPm = Col("pm", 8); var cNotes = Col("notes / remarks", 10);

        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var no = ws.Text(r, 2);                                   // the second "P. No" column holds the project number
            if (no.Length == 0) continue;
            var title = StripNumberPrefix(ws.Text(r, cTitle), no);
            var isNew = !ctx.Projects.TryGetValue(no, out var p);
            if (isNew)
            {
                p = new Project { ProjectNo = no, Name = title.Length > 0 ? title : no };
                db.Projects.Add(p);
                ctx.Projects[no] = p;
                ctx.Report.Added("Project");
            }
            p!.Customer = Nullable(ws.Text(r, cName)) ?? p.Customer;
            p.ProductLine = Nullable(ws.Text(r, cProduct)) ?? p.ProductLine;
            p.TechnicalManager = Nullable(ws.Text(r, cTm)) ?? p.TechnicalManager;
            p.ProjectManager = Nullable(ws.Text(r, cPm)) ?? p.ProjectManager;
            if (Nullable(ws.Text(r, cNotes)) is { } notes && string.IsNullOrEmpty(p.Notes)) p.Notes = notes;

            var status = ws.Text(r, cStatus);
            if (status.Length > 0)
            {
                if (ConfigurationService.IsValidAsync(db, Vocabularies.TrackerStatus, status).GetAwaiter().GetResult()) p.TrackerStatus = status;
                else ctx.Report.Warnings.Add($"LPP: status '{status}' of {no} is not in the tracker-status vocabulary; left unchanged.");
            }
            if (!isNew)
            {
                db.ChangeTracker.DetectChanges();
                if (db.Entry(p).State == EntityState.Modified) ctx.Report.Updated("Project"); else ctx.Report.Unchanged("Project");
            }
        }
    }

    private async Task<int?> ImportPlanSheetAsync(ClosedXML.Excel.IXLWorksheet ws, Context ctx, Dictionary<string, string> imported, CancellationToken ct)
    {
        var header = FindTaskHeaderRow(ws);
        if (header is null) { ctx.Report.Warnings.Add($"Sheet '{ws.Name}' is not a task tracker (no 'Sr' header row); skipped."); return null; }

        var cols = ErpProjectImporter.HeaderMap(ws, header.Value);
        int? Col(params string[] names) => names.Select(n => cols.TryGetValue(n, out var c) ? (int?)c : null).FirstOrDefault(c => c is not null);
        var cName = Col("task name", "task"); var cModule = Col("module"); var cStart = Col("start date"); var cEnd = Col("end date");
        var cDuration = Col("duration (days)", "duration(days)", "duration"); var cProgress = Col("progress %", "progress%");
        var cStatus = Col("status"); var cPriority = Col("priority"); var cOwner = Col("owner", "engineer"); var cRemarks = Col("remarks");
        if (cName is null) { ctx.Report.Warnings.Add($"Sheet '{ws.Name}': no task-name column; skipped."); return null; }

        var rows = new List<int>();
        for (var r = header.Value + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
            if (ws.Number(r, 1) is not null && ws.Text(r, cName.Value).Length > 0) rows.Add(r);
        if (rows.Count == 0) { ctx.Report.Warnings.Add($"Sheet '{ws.Name}' has no tasks; skipped."); return null; }

        var projectNo = ReadLabel(ws, "Project Number");
        if (projectNo is null) { ctx.Report.Warnings.Add($"Sheet '{ws.Name}': 'Project Number' not found; skipped."); return null; }
        if (imported.TryGetValue(projectNo, out var firstSheet))
        {
            ctx.Report.Warnings.Add($"Sheet '{ws.Name}' repeats project number {projectNo} already imported from '{firstSheet}'; skipped (check for a copied template).");
            return null;
        }
        imported[projectNo] = ws.Name;

        var title = ReadLabel(ws, "Project Name");
        if (!ctx.Projects.TryGetValue(projectNo, out var project))
        {
            project = new Project { ProjectNo = projectNo, Name = StripNumberPrefix(title ?? "", projectNo) is { Length: > 0 } n ? n : projectNo };
            db.Projects.Add(project);
            ctx.Projects[projectNo] = project;
            ctx.Report.Added("Project");
        }
        project.ProjectManager ??= ReadLabel(ws, "Project Manager");
        project.TechnicalManager ??= ReadLabel(ws, "Technical Lead");
        if (ReadLabelDate(ws, "Planned Completion") is { } planned) project.PlannedEnd = planned;
        if (ReadLabelDate(ws, "Last Updated") is { } updated) project.LastStatusUpdate = updated;
        await db.SaveChangesAsync(ct);

        var existing = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync(ct);
        var byKey = existing.Where(t => t.ExternalKey is not null).ToDictionary(t => t.ExternalKey!, StringComparer.Ordinal);
        var summaries = new Dictionary<string, PlanTask>(StringComparer.OrdinalIgnoreCase);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var sort = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder);
        var assigned = (await db.TaskAssignments.Where(a => existing.Select(t => t.Id).Contains(a.TaskId)).ToListAsync(ct))
            .Select(a => (a.TaskId, a.PersonId)).ToHashSet();

        foreach (var r in rows)
        {
            var sr = (int)ws.Number(r, 1)!.Value;
            var module = cModule is { } cm ? ws.Text(r, cm) : "";
            PlanTask? parent = null;
            if (module.Length > 0)
            {
                var moduleKey = $"{ws.Name}:module:{module}";
                if (!summaries.TryGetValue(module, out parent))
                {
                    if (!byKey.TryGetValue(moduleKey, out parent))
                    {
                        parent = new PlanTask { ProjectId = project.Id, Name = module, Module = module, Origin = TaskOrigins.Tracker, ExternalKey = moduleKey, SortOrder = ++sort };
                        db.Tasks.Add(parent);
                        await db.SaveChangesAsync(ct);
                        byKey[moduleKey] = parent;
                        ctx.Report.Added("Module group");
                    }
                    summaries[module] = parent;
                }
            }

            var key = $"{ws.Name}:{sr}";
            if (!usedKeys.Add(key))
            {
                ctx.Report.Warnings.Add($"{ws.Name} row {r}: Sr {sr} is repeated; imported as a separate task.");
                key = $"{ws.Name}:{sr}#r{r}";
                usedKeys.Add(key);
            }
            var start = cStart is { } cs ? ws.Date(r, cs) : null;
            var end = cEnd is { } ce ? ws.Date(r, ce) : null;
            var durationText = cDuration is { } cd ? ws.Number(r, cd) : null;
            var owners = cOwner is { } co ? SplitOwners(ws.Text(r, co)) : [];
            var people = owners.Select(o => ResolveOwner(o, ctx)).Where(p => p is not null).Select(p => p!).DistinctBy(p => p.Id).ToList();

            int? fixedDays = null;
            if (start is { } s0 && end is { } e0)
            {
                if (e0 < s0) { ctx.Report.Warnings.Add($"{ws.Name} row {r}: end before start; assumed 1 day."); fixedDays = 1; }
                else fixedDays = Math.Max(1, ctx.Calendar.WorkingDaysBetween(ctx.Calendar.OnOrAfter(s0), ctx.Calendar.OnOrBefore(e0)) + 1);
            }
            else if (durationText is { } d0 and > 0) fixedDays = (int)Math.Ceiling(d0);

            var progress = cProgress is { } cp ? ws.Number(r, cp) ?? 0m : 0m;
            if (progress > 1m) progress /= 100m;
            progress = Math.Clamp(progress, 0m, 1m);
            var status = MapStatus(cStatus is { } cst ? ws.Text(r, cst) : "", progress);
            var estimatedWork = fixedDays is { } fd ? fd * ctx.Calendar.HoursPerDay * Math.Max(1, owners.Count(o => !NotAPerson.Contains(o))) : 0m;

            var isNew = !byKey.TryGetValue(key, out var task);
            if (isNew)
            {
                task = new PlanTask { ProjectId = project.Id, Origin = TaskOrigins.Tracker, ExternalKey = key, SortOrder = ++sort };
                db.Tasks.Add(task);
                byKey[key] = task;
            }
            var locked = (task!.LockedFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (!locked.Contains("name")) task.Name = ws.Text(r, cName.Value);
            task.ParentId = parent?.Id;
            task.Module = Nullable(module);
            if (!locked.Contains("fixedDuration")) task.FixedDurationDays = fixedDays;
            if (!locked.Contains("snet")) task.StartNoEarlierThan = start;
            task.PlannedStart = start; task.PlannedFinish = end;
            if (!locked.Contains("work")) { task.WorkHours = estimatedWork; task.WorkIsEstimated = true; }
            if (!locked.Contains("progress")) task.PercentComplete = progress;
            if (!locked.Contains("status")) task.Status = status;
            if (!locked.Contains("priority") && cPriority is { } cpr) task.Priority = Nullable(ws.Text(r, cpr));
            if (!locked.Contains("notes") && cRemarks is { } cr) task.Notes = Nullable(ws.Text(r, cr));

            if (isNew) ctx.Report.Added("Task");
            else
            {
                db.ChangeTracker.DetectChanges();
                if (db.Entry(task).State == EntityState.Modified) ctx.Report.Updated("Task"); else ctx.Report.Unchanged("Task");
            }
            await db.SaveChangesAsync(ct);

            foreach (var person in people.Where(p => !assigned.Contains((task.Id, p.Id))))
            {
                db.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, PersonId = person.Id, Units = 1m });
                assigned.Add((task.Id, person.Id));
                ctx.Report.Added("Assignment");
            }
        }
        await db.SaveChangesAsync(ct);
        return project.Id;
    }

    private Person? ResolveOwner(string token, Context ctx)
    {
        if (NotAPerson.Contains(token)) return null;
        var isInitials = token.Length <= 3 && token.All(char.IsLetter) && token.ToUpperInvariant() == token;

        var found = isInitials ? ctx.Aliases.Where(a => a.PersonId is not null && a.Alias.Equals(token, StringComparison.OrdinalIgnoreCase)).Select(a => ctx.People.FirstOrDefault(p => p.Id == a.PersonId)).FirstOrDefault(p => p is not null)
                               : ctx.Matcher.Resolve(token);
        if (found is not null) return found;

        if (isInitials)
        {
            var matches = ctx.People.Where(p => InitialsOf(p.FullName).Equals(token, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1)
            {
                AddAlias(ctx, token, matches[0].Id);
                return matches[0];
            }
            AddAlias(ctx, token, null);
            var why = matches.Count > 1 ? $"ambiguous: {string.Join(" / ", matches.Select(m => m.FullName))}" : "no match";
            var label = $"{token} ({why})";
            if (!ctx.Report.Unresolved.Contains(label)) ctx.Report.Unresolved.Add(label);
            return null;
        }
        AddAlias(ctx, token, null);
        if (!ctx.Report.Unresolved.Contains(token)) ctx.Report.Unresolved.Add(token);
        return null;
    }

    private void AddAlias(Context ctx, string token, int? personId)
    {
        var existing = ctx.Aliases.FirstOrDefault(a => a.Alias.Equals(token, StringComparison.OrdinalIgnoreCase) && a.Source == "tracker");
        if (existing is null)
        {
            existing = new PersonAlias { Alias = token, Kind = AliasKind.Initials, Source = "tracker", PersonId = personId };
            db.PersonAliases.Add(existing);
            ctx.Aliases.Add(existing);
        }
        else if (existing.PersonId is null && personId is not null) existing.PersonId = personId;
    }

    internal static string InitialsOf(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts.Length == 1 ? parts[0][..1] : $"{parts[0][0]}{parts[^1][0]}";
    }

    internal static List<string> SplitOwners(string text) =>
        text.Split([',', '/', '&', ';', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static string MapStatus(string text, decimal progress)
    {
        var s = text.Trim().ToLowerInvariant();
        if (s.Contains("complet") || s.Contains("done")) return "Completed";
        if (s.Contains("delay")) return "Delayed";
        if (s.Contains("progress")) return "In Progress";
        if (progress >= 1m) return "Completed";
        return progress > 0m ? "In Progress" : "Pending";
    }

    private async Task ImportAmcAsync(ClosedXML.Excel.IXLWorksheet ws, Context ctx, CancellationToken ct)
    {
        var cols = ErpProjectImporter.HeaderMap(ws, 1);
        if (!cols.ContainsKey("report issue")) { ctx.Report.Warnings.Add("AMC: header 'Report Issue' not found; skipped."); return; }
        string Get(int r, string h) => cols.TryGetValue(h, out var c) ? ws.Text(r, c) : "";
        DateOnly? Dt(int r, string h) => cols.TryGetValue(h, out var c) ? ws.Date(r, c) : null;

        var existing = await db.AmcTickets.ToListAsync(ct);
        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var issue = Get(r, "report issue");
            if (issue.Length == 0) continue;
            var no = Nullable(Get(r, "p. no"));
            var raised = Dt(r, "raised date");
            var key = existing.FirstOrDefault(t => t.ProjectNo == no && t.RaisedOn == raised && t.Issue == issue);
            var isNew = key is null;
            key ??= new AmcTicket { ProjectNo = no, RaisedOn = raised, Issue = issue };
            if (isNew) { db.AmcTickets.Add(key); existing.Add(key); }

            key.Product = Nullable(Get(r, "product name"));
            key.InAmc = Get(r, "in amc (y/n)").StartsWith('Y');
            key.RaisedBy = Nullable(Get(r, "raised by"));
            key.AssignedOn = Dt(r, "assign date");
            key.AssignedBy = Nullable(Get(r, "assign by"));
            key.ResolvedOn = Dt(r, "resolve date");
            key.AssignedEngineers = Nullable(Get(r, "assigned engineer(s)"));
            key.RootCause = Nullable(Get(r, "root cause"));
            key.Notes = Nullable(Get(r, "notes/remarks"));
            var status = Get(r, "status");
            key.Status = status.Length > 0 ? status : key.ResolvedOn is not null ? "Resolved" : key.AssignedOn is not null ? "Assigned" : "Open";
            key.ProjectId ??= ctx.Projects.TryGetValue(no ?? "", out var p) ? p.Id : null;

            if (isNew) ctx.Report.Added("AMC ticket");
            else
            {
                db.ChangeTracker.DetectChanges();
                if (db.Entry(key).State == EntityState.Modified) ctx.Report.Updated("AMC ticket"); else ctx.Report.Unchanged("AMC ticket");
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static int? FindTaskHeaderRow(ClosedXML.Excel.IXLWorksheet ws)
    {
        for (var r = 1; r <= 20; r++)
        {
            var a = ws.Text(r, 1).ToLowerInvariant().TrimEnd('.');
            if (a is "sr" or "sr no" or "sr. no") return r;
        }
        return null;
    }

    private static string? ReadLabel(ClosedXML.Excel.IXLWorksheet ws, string label)
    {
        for (var r = 1; r <= 12; r++)
        {
            if (!ws.Text(r, 1).Equals(label, StringComparison.OrdinalIgnoreCase)) continue;
            for (var c = 2; c <= 8; c++)
                if (ws.Text(r, c) is { Length: > 0 } v) return v;
        }
        return null;
    }

    private static DateOnly? ReadLabelDate(ClosedXML.Excel.IXLWorksheet ws, string label)
    {
        for (var r = 1; r <= 12; r++)
        {
            for (var c = 1; c <= 16; c++)
            {
                if (!ws.Text(r, c).Equals(label, StringComparison.OrdinalIgnoreCase)) continue;
                for (var d = c + 1; d <= c + 5; d++)
                    if (ws.Date(r, d) is { } date) return date;
            }
        }
        return null;
    }

    private static string StripNumberPrefix(string title, string no) =>
        Regex.Replace(title.Trim(), "^" + Regex.Escape(no) + @"\s*[-–]\s*", "", RegexOptions.IgnoreCase).Trim();

    private static string? Nullable(string s) => s.Length == 0 ? null : s;
}
