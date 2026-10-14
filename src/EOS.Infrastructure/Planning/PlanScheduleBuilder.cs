using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Domain.Settings;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Planning;

/// <summary>Everything needed to run (and write back) a project schedule: entities plus the engine input.</summary>
internal sealed record BuiltPlan(
    Project Project,
    List<PlanTask> Tasks,
    List<PlanTask> Leaves,
    HashSet<int> HasChildren,
    ProjectPlan Plan,
    WorkCalendar Calendar,
    SchedulingOptions Options,
    List<string> Warnings);

/// <summary>
/// The single place that turns a stored project plan into engine input. Plan recompute, scenarios and release analysis all
/// use it, so they can never disagree about how a task is scheduled (spec §14, §15).
/// </summary>
internal static class PlanScheduleBuilder
{
    public static async Task<BuiltPlan> BuildAsync(EosDbContext db, int projectId, DateOnly today, CancellationToken ct)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new PlanException($"Project {projectId} not found.");
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var links = await db.TaskLinks.Where(l => l.ProjectId == projectId).ToListAsync(ct);
        var taskIds = tasks.Select(t => t.Id).ToHashSet();
        var assignments = await db.TaskAssignments.Where(a => taskIds.Contains(a.TaskId)).ToListAsync(ct);
        var warnings = new List<string>();

        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var chain = SettingsResolver.Chain(projectId: projectId.ToString());
        var estimatedDrives = resolver.Get<bool>(SettingKeys.EstimatedWorkDrivesSchedule, chain, today);
        var defaultDuration = resolver.Get<int>(SettingKeys.DefaultTaskDurationDays, chain, today);
        var unassignedCrew = resolver.Get<int>(SettingKeys.UnassignedCrewSize, chain, today);
        var pickup = resolver.Get<string>(SettingKeys.LostAllEngineersRule, chain, today) == "none" ? PickupRule.None : PickupRule.AfterOwnTasks;
        var cal = await ConfigurationService.LoadCalendarAsync(db, project.CalendarId, ct);

        var hasChildren = tasks.Where(t => t.ParentId is not null).Select(t => t.ParentId!.Value).ToHashSet();
        var leaves = tasks.Where(t => !hasChildren.Contains(t.Id)).ToList();
        var leafIds = leaves.Select(t => t.Id).ToHashSet();

        var scheduleTasks = new List<ScheduleTask>();
        var unassigned = 0;
        foreach (var t in leaves)
        {
            var crew = assignments.Where(a => a.TaskId == t.Id && a.PersonId is not null)
                .Select(a => new CrewMember(a.PersonId!.Value.ToString(), a.Units, 1m, a.FromDate, a.ToDate)).ToList();
            if (assignments.Any(a => a.TaskId == t.Id && a.PersonId is null))
                warnings.Add($"Task '{t.Name}' has a placeholder skill need but no named engineer.");

            var remainingFactor = t.PercentComplete is > 0 and < 1 ? 1m - t.PercentComplete : 1m;
            var work = t.Kind == TaskKinds.Milestone ? 0m : t.WorkHours * remainingFactor;
            int? fixedDays = t.Kind == TaskKinds.Milestone ? null : t.FixedDurationDays;
            if (fixedDays is not null && t.WorkIsEstimated && estimatedDrives) fixedDays = null;
            if (fixedDays is not null && t.PercentComplete is > 0 and < 1) fixedDays = Math.Max(1, (int)Math.Ceiling(fixedDays.Value * (double)remainingFactor));

            var snet = t.StartNoEarlierThan;
            if (t.PercentComplete is > 0 and < 1 && (snet is null || snet < today)) snet = today;

            if (t.PercentComplete >= 1m && t.Kind == TaskKinds.Task)
            {
                // Finished work keeps its real dates as fixed facts.
                var s = t.ActualStart ?? t.PlannedStart ?? t.StartNoEarlierThan;
                var f = t.ActualFinish ?? t.PlannedFinish ?? s;
                if (s is { } sd && f is { } fd && fd >= sd)
                {
                    fixedDays = Math.Max(1, cal.WorkingDaysBetween(cal.OnOrAfter(sd), cal.OnOrBefore(fd)) + 1);
                    snet = sd; work = 0m;
                }
                else { fixedDays = 1; work = 0m; }
            }
            else if (t.Kind == TaskKinds.Task && work == 0m && fixedDays is null)
            {
                fixedDays = defaultDuration;
                warnings.Add($"Task '{t.Name}' has no work or duration; assumed {defaultDuration} day(s).");
            }

            if (crew.Count == 0 && work > 0m && fixedDays is null)
            {
                unassigned++;
                crew = Enumerable.Range(1, Math.Max(1, unassignedCrew)).Select(i => new CrewMember($"unassigned-{i}")).ToList();
            }

            var deps = links.Where(l => l.SuccessorId == t.Id && leafIds.Contains(l.PredecessorId))
                .Select(l => new TaskDependency(l.PredecessorId.ToString(), (LinkType)l.Type, l.LagDays)).ToList();
            scheduleTasks.Add(new ScheduleTask(t.Id.ToString(), t.Name, work, [], crew)
            {
                Dependencies = deps, FixedDurationDays = fixedDays, StartNoEarlierThan = snet,
            });
        }
        if (unassigned > 0) warnings.Add($"{unassigned} task(s) have no assigned engineer; a default crew of {unassignedCrew} is assumed.");

        var projectStart = project.PoStart
            ?? tasks.Where(t => t.PlannedStart is not null).Select(t => t.PlannedStart!.Value).DefaultIfEmpty(today).Min();

        return new BuiltPlan(project, tasks, leaves, hasChildren,
            new ProjectPlan(projectId.ToString(), project.Name, projectStart, scheduleTasks), cal, new SchedulingOptions(pickup), warnings);
    }
}
