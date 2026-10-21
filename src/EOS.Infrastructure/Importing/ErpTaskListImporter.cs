using System.Text.RegularExpressions;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

/// <summary>
/// The ERP project task list ("List_WBS_Project_TaskList_Timesheet_DepReport"): one row per task, one engineer per task,
/// grouped under "Department:" and "Employee:" header rows, with budget hours, hours booked and ERP status.
/// Tasks of known projects become plan tasks (origin "erp") under a module group per milestone; the engineer becomes the assignment.
/// Tasks without a project (common support, general tasks, visit reports) are not part of any plan and are only counted.
/// </summary>
internal sealed partial class ErpTaskListImporter(EosDbContext db)
{
    private sealed record Row(string TaskNo, string Milestone, string Activity, string TaskName, DateOnly? Start, DateOnly? End,
        decimal Budget, decimal Filled, string Status, string Employee, string ProjectNo);

    public static bool Matches(IReadOnlyCollection<HashSet<string>> headerRows) =>
        headerRows.Any(r => r.Contains("Task No") && r.Contains("Timesheet Filled Hours") && r.Contains("Project Task Status"));

    public async Task ImportAsync(string file, PeopleIndex idx, WorkCalendar cal, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(file);
        var ws = wb.Worksheets.First();

        var headerRow = 0; var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r <= Math.Min(10, ws.LastRowUsed()?.RowNumber() ?? 1) && headerRow == 0; r++)
        {
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in ws.Row(r).CellsUsed()) found.TryAdd(Whitespace().Replace(c.GetString(), " ").Trim(), c.Address.ColumnNumber);
            if (found.ContainsKey("Task No")) { headerRow = r; cols = found; }
        }
        string[] required = ["Task No", "Milestone", "Task Name", "Start Date", "End Date", "Total Hour", "Timesheet Filled Hours", "Project Task Status", "Employee", "Project No"];
        var absent = required.Where(c => !cols.ContainsKey(c)).ToList();
        if (headerRow == 0 || absent.Count > 0)
        {
            report.Warnings.Add($"{Path.GetFileName(file)}: ERP task list columns not found{(headerRow == 0 ? "" : ": " + string.Join(", ", absent))}; skipped.");
            return;
        }

        var rows = new List<Row>();
        var nonProject = 0;
        for (var r = headerRow + 1; r <= (ws.LastRowUsed()?.RowNumber() ?? headerRow); r++)
        {
            var no = ws.Text(r, cols["Task No"]);
            if (no.Length == 0 || !no.All(char.IsDigit)) continue;      // "Department:" / "Employee:" group rows
            var projectNo = ws.Text(r, cols["Project No"]);
            if (projectNo.Length == 0 || projectNo == "0") { nonProject++; continue; }
            var activity = cols.TryGetValue("Activity", out var ac) ? ws.Text(r, ac) : "";
            rows.Add(new Row(no, ws.Text(r, cols["Milestone"]), activity, ws.Text(r, cols["Task Name"]),
                ws.Date(r, cols["Start Date"]), ws.Date(r, cols["End Date"]), ws.Number(r, cols["Total Hour"]) ?? 0m,
                ws.Number(r, cols["Timesheet Filled Hours"]) ?? 0m, ws.Text(r, cols["Project Task Status"]), ws.Text(r, cols["Employee"]), projectNo));
        }
        for (var i = 0; i < nonProject; i++) report.Unchanged("Non-project task (not planned)");

        var projects = await db.Projects.ToDictionaryAsync(p => p.ProjectNo, StringComparer.OrdinalIgnoreCase, ct);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in rows.GroupBy(r => r.ProjectNo, StringComparer.OrdinalIgnoreCase))
        {
            if (!projects.TryGetValue(group.Key, out var project))
            {
                // A project the ERP project list does not carry (service orders, contracts): keep its tasks under a stub that the next project-list import completes.
                var name = await db.Timesheets.Where(t => t.ProjectNo == group.Key && t.ProjectName != null).Select(t => t.ProjectName).FirstOrDefaultAsync(ct);
                project = new Project { ProjectNo = group.Key, Name = name ?? group.Key };
                db.Projects.Add(project);
                await db.SaveChangesAsync(ct);
                projects[group.Key] = project;
                report.Added("Project (stub from task list)");
            }
            await ImportProjectAsync(project, group.ToList(), idx, cal, unresolved, report, ct);
        }

        report.Unresolved.AddRange(unresolved.Order());
    }

    private async Task ImportProjectAsync(Project project, List<Row> tasks, PeopleIndex idx, WorkCalendar cal, HashSet<string> unresolved, ImportReport report, CancellationToken ct)
    {
        var existing = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync(ct);
        var byKey = existing.Where(t => t.ExternalKey is not null).ToDictionary(t => t.ExternalKey!, StringComparer.Ordinal);
        var taskIds = existing.Select(t => t.Id).ToList();
        var assigned = (await db.TaskAssignments.Where(a => taskIds.Contains(a.TaskId)).ToListAsync(ct)).Select(a => (a.TaskId, a.PersonId)).ToHashSet();
        var sort = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder);

        foreach (var row in tasks)
        {
            PlanTask? parent = null;
            if (row.Milestone.Length > 0)
            {
                var groupKey = $"erp:{project.ProjectNo}:{row.Milestone}";
                if (!byKey.TryGetValue(groupKey, out parent))
                {
                    parent = new PlanTask { ProjectId = project.Id, Name = row.Milestone, Module = row.Milestone, Origin = TaskOrigins.Erp, ExternalKey = groupKey, SortOrder = ++sort };
                    db.Tasks.Add(parent);
                    await db.SaveChangesAsync(ct);
                    byKey[groupKey] = parent;
                    report.Added("Module group");
                }
            }

            var key = $"erp:{row.TaskNo}";
            var isNew = !byKey.TryGetValue(key, out var task);
            if (isNew)
            {
                task = new PlanTask { ProjectId = project.Id, Origin = TaskOrigins.Erp, ExternalKey = key, SortOrder = ++sort };
                db.Tasks.Add(task);
                byKey[key] = task;
            }
            var locked = (task!.LockedFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            int? fixedDays = null;
            if (row.Start is { } s0 && row.End is { } e0)
                fixedDays = e0 < s0 ? 1 : Math.Max(1, cal.WorkingDaysBetween(cal.OnOrAfter(s0), cal.OnOrBefore(e0)) + 1);

            var status = row.Status.Contains("complet", StringComparison.OrdinalIgnoreCase) ? "Completed"
                : row.Status.Contains("running", StringComparison.OrdinalIgnoreCase) ? "In Progress" : "Pending";

            if (!locked.Contains("name")) task.Name = row.TaskName.Length > 0 ? row.TaskName : row.Activity.Length > 0 ? row.Activity : $"Task {row.TaskNo}";
            task.ParentId = parent?.Id;
            task.Module = row.Milestone.Length > 0 ? row.Milestone : null;
            if (!locked.Contains("fixedDuration")) task.FixedDurationDays = fixedDays;
            if (!locked.Contains("snet")) task.StartNoEarlierThan = row.Start;
            task.PlannedStart = row.Start; task.PlannedFinish = row.End;
            if (!locked.Contains("work"))
            {
                if (row.Budget > 0) { task.WorkHours = row.Budget; task.WorkIsEstimated = false; }
                else { task.WorkHours = (fixedDays ?? 1) * cal.HoursPerDay; task.WorkIsEstimated = true; }
            }
            task.ActualHours = row.Filled;
            if (!locked.Contains("status")) task.Status = status;
            if (!locked.Contains("progress") && status != "In Progress") task.PercentComplete = status == "Completed" ? 1m : 0m;

            if (isNew) report.Added("Task");
            else
            {
                db.ChangeTracker.DetectChanges();
                if (db.Entry(task).State == EntityState.Modified) report.Updated("Task"); else report.Unchanged("Task");
            }
            await db.SaveChangesAsync(ct);

            var person = row.Employee.Length == 0 ? null : idx.Find(row.Employee);
            if (row.Employee.Length > 0 && person is null) unresolved.Add(row.Employee);
            if (person is not null && assigned.Add((task.Id, person.Id)))
            {
                db.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, PersonId = person.Id, Units = 1m });
                report.Added("Assignment");
            }
        }
        await db.SaveChangesAsync(ct);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
