using EOS.Domain.Audit;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Domain.Settings;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Planning;

/// <summary>
/// Authors and schedules project plans (spec §16.7): a WBS built in EOS or imported then extended, dependencies,
/// assignments, progress, baselines. Scheduling uses the pure engine; results are cached on the tasks and
/// summaries are rolled up from their children.
/// </summary>
public sealed class PlanService(EosDbContext db)
{
    // ---- structure ---------------------------------------------------------------------------------

    public async Task<PlanTask> AddTaskAsync(
        int projectId, string name, string actor, int? parentId = null, int? afterTaskId = null,
        decimal workHours = 0m, string kind = TaskKinds.Task, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new PlanException("A task needs a name.");
        if (workHours < 0) throw new PlanException("Work hours cannot be negative.");
        if (kind is not (TaskKinds.Task or TaskKinds.Milestone)) throw new PlanException($"Unknown task kind '{kind}'.");
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new PlanException($"Project {projectId} not found.");

        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).ToListAsync(ct);
        if (parentId is { } pid && tasks.All(t => t.Id != pid)) throw new PlanException($"Parent task {pid} is not in this project.");
        if (afterTaskId is { } aid)
        {
            var after = tasks.SingleOrDefault(t => t.Id == aid) ?? throw new PlanException($"Task {aid} is not in this project.");
            parentId = after.ParentId;
        }

        var task = new PlanTask
        {
            ProjectId = projectId, ParentId = parentId, Name = name.Trim(), Kind = kind,
            WorkHours = kind == TaskKinds.Milestone ? 0m : workHours, Origin = TaskOrigins.Eos,
        };
        // Place after the sibling (or at the end of the parent's children): use a fractional SortOrder, then normalise.
        var siblings = tasks.Where(t => t.ParentId == parentId).OrderBy(t => t.SortOrder).ToList();
        task.SortOrder = afterTaskId is { } a2
            ? tasks.Single(t => t.Id == a2).SortOrder + 1
            : (siblings.Count == 0 ? (parentId is { } p2 ? tasks.Single(t => t.Id == p2).SortOrder + 1 : 1) : Subtree(tasks, siblings[^1].Id).Max(t => t.SortOrder) + 1);
        foreach (var t in tasks.Where(t => t.SortOrder >= task.SortOrder)) t.SortOrder++;
        db.Tasks.Add(task);
        Audit("plan.task.add", actor, projectId, name);
        await db.SaveChangesAsync(ct);
        await NormaliseAsync(projectId, ct);
        return task;
    }

    public async Task UpdateTaskAsync(int taskId, TaskPatch patch, string actor, CancellationToken ct = default)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new PlanException($"Task {taskId} not found.");
        var edited = new List<string>();

        if (patch.Name is not null) { if (string.IsNullOrWhiteSpace(patch.Name)) throw new PlanException("A task needs a name."); task.Name = patch.Name.Trim(); edited.Add("name"); }
        if (patch.Module is not null) { task.Module = patch.Module.Length == 0 ? null : patch.Module; edited.Add("module"); }
        if (patch.Kind is not null)
        {
            if (patch.Kind is not (TaskKinds.Task or TaskKinds.Milestone)) throw new PlanException($"Unknown task kind '{patch.Kind}'.");
            task.Kind = patch.Kind; edited.Add("kind");
            if (patch.Kind == TaskKinds.Milestone) { task.WorkHours = 0; task.FixedDurationDays = null; }
        }
        if (patch.WorkHours is { } w)
        {
            if (w < 0) throw new PlanException("Work hours cannot be negative.");
            task.WorkHours = w; task.WorkIsEstimated = false; edited.Add("work");
        }
        if (patch.ClearFixedDuration) { task.FixedDurationDays = null; edited.Add("fixedDuration"); }
        else if (patch.FixedDurationDays is { } d)
        {
            if (d < 0) throw new PlanException("Duration cannot be negative.");
            task.FixedDurationDays = d; edited.Add("fixedDuration");
        }
        if (patch.ClearStartNoEarlierThan) { task.StartNoEarlierThan = null; edited.Add("snet"); }
        else if (patch.StartNoEarlierThan is { } s) { task.StartNoEarlierThan = s; edited.Add("snet"); }
        if (patch.PercentComplete is { } pc)
        {
            if (pc is < 0 or > 1) throw new PlanException("Percent complete must be between 0 and 1.");
            task.PercentComplete = pc; edited.Add("progress");
            if (patch.Status is null) task.Status = pc >= 1 ? "Completed" : pc > 0 ? "In Progress" : task.Status == "Completed" ? "Pending" : task.Status;
        }
        if (patch.Status is not null)
        {
            if (!await ConfigurationService.IsValidAsync(db, EOS.Domain.Configuration.Vocabularies.TaskStatus, patch.Status, ct))
                throw new PlanException($"'{patch.Status}' is not an allowed task status.");
            task.Status = patch.Status; edited.Add("status");
        }
        if (patch.Priority is not null) { task.Priority = patch.Priority.Length == 0 ? null : patch.Priority; edited.Add("priority"); }
        if (patch.Notes is not null) { task.Notes = patch.Notes; edited.Add("notes"); }
        if (patch.ActualStart is { } as_) { task.ActualStart = as_; edited.Add("actualStart"); }
        if (patch.ActualFinish is { } af) { task.ActualFinish = af; edited.Add("actualFinish"); }

        if (task.Origin != TaskOrigins.Eos) task.LockedFields = string.Join(',', (task.LockedFields?.Split(',') ?? []).Union(edited));
        Audit("plan.task.update", actor, task.ProjectId, $"#{taskId}: {string.Join(',', edited)}");
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteTaskAsync(int taskId, string actor, CancellationToken ct = default)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new PlanException($"Task {taskId} not found.");
        var all = await db.Tasks.Where(t => t.ProjectId == task.ProjectId).ToListAsync(ct);
        var doomed = Subtree(all, taskId).ToList();
        var ids = doomed.Select(t => t.Id).ToHashSet();
        db.TaskLinks.RemoveRange(db.TaskLinks.Where(l => ids.Contains(l.PredecessorId) || ids.Contains(l.SuccessorId)));
        db.TaskAssignments.RemoveRange(db.TaskAssignments.Where(a => ids.Contains(a.TaskId)));
        foreach (var t in doomed.OrderByDescending(t => t.SortOrder)) db.Tasks.Remove(t);   // children before parents
        Audit("plan.task.delete", actor, task.ProjectId, $"#{taskId} ({doomed.Count} task(s))");
        await db.SaveChangesAsync(ct);
        await NormaliseAsync(task.ProjectId, ct);
    }

    public async Task MoveTaskAsync(int taskId, MoveDirection direction, string actor, CancellationToken ct = default)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new PlanException($"Task {taskId} not found.");
        var all = await db.Tasks.Where(t => t.ProjectId == task.ProjectId).ToListAsync(ct);
        var siblings = all.Where(t => t.ParentId == task.ParentId).OrderBy(t => t.SortOrder).ToList();
        var index = siblings.FindIndex(t => t.Id == taskId);

        switch (direction)
        {
            case MoveDirection.Up when index > 0:
                (siblings[index].SortOrder, siblings[index - 1].SortOrder) = (siblings[index - 1].SortOrder, siblings[index].SortOrder);
                // Swap whole subtrees: shift by subtree sizes.
                SwapSubtrees(all, siblings[index - 1], siblings[index]);
                break;
            case MoveDirection.Down when index >= 0 && index < siblings.Count - 1:
                SwapSubtrees(all, siblings[index], siblings[index + 1]);
                break;
            case MoveDirection.Indent when index > 0:
                task.ParentId = siblings[index - 1].Id;                                   // becomes the last child of the previous sibling
                task.SortOrder = Subtree(all, siblings[index - 1].Id).Max(t => t.SortOrder) + 1;
                foreach (var t in all.Where(t => t.Id != task.Id && !Subtree(all, task.Id).Contains(t) && t.SortOrder >= task.SortOrder)) t.SortOrder += 10_000;
                break;
            case MoveDirection.Outdent when task.ParentId is { } parentId:
                var parent = all.Single(t => t.Id == parentId);
                task.ParentId = parent.ParentId;                                          // becomes the next sibling of its parent
                task.SortOrder = Subtree(all, parent.Id).Max(t => t.SortOrder) + 1;
                break;
            default:
                throw new PlanException($"Cannot move task {taskId} {direction}.");
        }
        Audit("plan.task.move", actor, task.ProjectId, $"#{taskId} {direction}");
        await db.SaveChangesAsync(ct);
        await NormaliseAsync(task.ProjectId, ct);
    }

    // ---- links and assignments -----------------------------------------------------------------------

    public async Task<TaskLink> AddLinkAsync(int predecessorId, int successorId, int type, int lagDays, string actor, CancellationToken ct = default)
    {
        if (predecessorId == successorId) throw new PlanException("A task cannot depend on itself.");
        if (type is < 0 or > 3) throw new PlanException("Link type must be 0 (FS), 1 (SS), 2 (FF) or 3 (SF).");
        var pred = await db.Tasks.SingleOrDefaultAsync(t => t.Id == predecessorId, ct) ?? throw new PlanException($"Task {predecessorId} not found.");
        var succ = await db.Tasks.SingleOrDefaultAsync(t => t.Id == successorId, ct) ?? throw new PlanException($"Task {successorId} not found.");
        if (pred.ProjectId != succ.ProjectId) throw new PlanException("Links must stay inside one project (separate plans per project).");
        if (await db.Tasks.AnyAsync(t => t.ParentId == predecessorId || t.ParentId == successorId, ct))
            throw new PlanException("Link tasks, not summaries: pick the detailed tasks.");

        var links = await db.TaskLinks.Where(l => l.ProjectId == pred.ProjectId).ToListAsync(ct);
        if (links.Any(l => l.PredecessorId == predecessorId && l.SuccessorId == successorId && l.Type == type))
            throw new PlanException("That link already exists.");
        if (Reaches(links, successorId, predecessorId)) throw new PlanException("That link would create a dependency cycle.");

        var link = new TaskLink { ProjectId = pred.ProjectId, PredecessorId = predecessorId, SuccessorId = successorId, Type = type, LagDays = lagDays };
        db.TaskLinks.Add(link);
        Audit("plan.link.add", actor, pred.ProjectId, $"{predecessorId}->{successorId} type {type} lag {lagDays}");
        await db.SaveChangesAsync(ct);
        return link;
    }

    public async Task RemoveLinkAsync(int linkId, string actor, CancellationToken ct = default)
    {
        var link = await db.TaskLinks.SingleOrDefaultAsync(l => l.Id == linkId, ct) ?? throw new PlanException($"Link {linkId} not found.");
        db.TaskLinks.Remove(link);
        Audit("plan.link.remove", actor, link.ProjectId, $"#{linkId}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<TaskAssignment> AssignAsync(
        int taskId, int? personId, decimal units, string actor, DateOnly? from = null, DateOnly? to = null,
        int? placeholderSkillId = null, int? placeholderMinLevel = null, CancellationToken ct = default)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new PlanException($"Task {taskId} not found.");
        if (personId is null && placeholderSkillId is null) throw new PlanException("Assign a person or a placeholder skill need.");
        if (units is <= 0 or > 1) throw new PlanException("Units must be in (0, 1].");
        if (personId is { } p && !await db.People.AnyAsync(x => x.Id == p, ct)) throw new PlanException($"Person {p} not found.");
        if (to < from) throw new PlanException("The assignment ends before it starts.");

        var assignment = new TaskAssignment
        {
            TaskId = taskId, PersonId = personId, Units = units, FromDate = from, ToDate = to,
            PlaceholderSkillId = placeholderSkillId, PlaceholderMinLevel = placeholderMinLevel,
        };
        db.TaskAssignments.Add(assignment);
        Audit("plan.assign", actor, task.ProjectId, $"task {taskId} person {personId?.ToString() ?? "placeholder"} units {units}");
        await db.SaveChangesAsync(ct);
        return assignment;
    }

    public async Task UnassignAsync(int assignmentId, string actor, CancellationToken ct = default)
    {
        var a = await db.TaskAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw new PlanException($"Assignment {assignmentId} not found.");
        var projectId = await db.Tasks.Where(t => t.Id == a.TaskId).Select(t => t.ProjectId).SingleAsync(ct);
        db.TaskAssignments.Remove(a);
        Audit("plan.unassign", actor, projectId, $"#{assignmentId}");
        await db.SaveChangesAsync(ct);
    }

    // ---- scheduling ------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the engine over the project's detailed tasks, caches dates/float/critical on every task, rolls up summaries,
    /// and returns the full plan. <paramref name="statusDate"/> is "today" for in-progress work (injectable for tests).
    /// </summary>
    public async Task<PlanView> RecomputeAsync(int projectId, DateOnly? statusDate = null, CancellationToken ct = default)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new PlanException($"Project {projectId} not found.");
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var links = await db.TaskLinks.Where(l => l.ProjectId == projectId).ToListAsync(ct);
        var taskIds = tasks.Select(t => t.Id).ToHashSet();
        var assignments = await db.TaskAssignments.Where(a => taskIds.Contains(a.TaskId)).ToListAsync(ct);
        var warnings = new List<string>();
        var today = statusDate ?? DateOnly.FromDateTime(DateTime.Today);

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

        DateOnly projectStart = project.PoStart
            ?? tasks.Where(t => t.PlannedStart is not null).Select(t => t.PlannedStart!.Value).DefaultIfEmpty(today).Min();

        ScheduleResult schedule;
        try { schedule = Scheduler.Compute(scheduleTasks, cal, projectStart, new SchedulingOptions(pickup)); }
        catch (SchedulingException ex) { throw new PlanException(ex.Message); }

        foreach (var t in leaves)
        {
            var r = schedule.Tasks[t.Id.ToString()];
            t.ScheduledStart = r.Start; t.ScheduledFinish = r.Finish;
            t.TotalFloatDays = r.TotalFloatDays; t.IsCritical = r.IsCritical;
        }
        RollUp(tasks, hasChildren);
        await db.SaveChangesAsync(ct);
        return await BuildViewAsync(project, warnings, ct);
    }

    public async Task<PlanView> GetPlanAsync(int projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new PlanException($"Project {projectId} not found.");
        return await BuildViewAsync(project, [], ct);
    }

    // ---- baselines -------------------------------------------------------------------------------------

    public async Task<Baseline> CreateBaselineAsync(int projectId, string name, string actor, CancellationToken ct = default)
    {
        var plan = await GetPlanAsync(projectId, ct);
        if (plan.Tasks.All(t => t.Start is null)) throw new PlanException("Schedule the plan (recompute) before saving a baseline.");
        var number = (await db.Baselines.Where(b => b.ProjectId == projectId).Select(b => (int?)b.Number).MaxAsync(ct) ?? -1) + 1;
        var baseline = new Baseline { ProjectId = projectId, Number = number, Name = string.IsNullOrWhiteSpace(name) ? $"Baseline {number}" : name, CreatedUtc = DateTime.UtcNow, CreatedBy = actor };
        db.Baselines.Add(baseline);
        await db.SaveChangesAsync(ct);
        db.BaselineTasks.AddRange(plan.Tasks.Select(t => new BaselineTask
        {
            BaselineId = baseline.Id, TaskId = t.Id, WbsCode = t.Wbs, Name = t.Name, Start = t.Start, Finish = t.Finish,
            WorkHours = t.WorkHours, PercentComplete = t.PercentComplete,
        }));
        Audit("plan.baseline", actor, projectId, baseline.Name);
        await db.SaveChangesAsync(ct);
        return baseline;
    }

    public async Task<BaselineVariance> GetBaselineVarianceAsync(int projectId, int? baselineNumber = null, CancellationToken ct = default)
    {
        var baseline = baselineNumber is { } n
            ? await db.Baselines.SingleOrDefaultAsync(b => b.ProjectId == projectId && b.Number == n, ct)
            : await db.Baselines.Where(b => b.ProjectId == projectId).OrderByDescending(b => b.Number).FirstOrDefaultAsync(ct);
        if (baseline is null) throw new PlanException("No baseline exists for this project.");

        var cal = await ConfigurationService.LoadCalendarAsync(db, (await db.Projects.SingleAsync(p => p.Id == projectId, ct)).CalendarId, ct);
        var plan = await GetPlanAsync(projectId, ct);
        var current = plan.Tasks.ToDictionary(t => t.Id);
        var rows = new List<BaselineVarianceRow>();
        foreach (var b in await db.BaselineTasks.Where(x => x.BaselineId == baseline.Id).ToListAsync(ct))
        {
            current.TryGetValue(b.TaskId, out var c);
            int? variance = b.Finish is { } bf && c?.Finish is { } cf ? cal.WorkingDaysBetween(bf, cf) : null;
            rows.Add(new BaselineVarianceRow(b.TaskId, b.WbsCode, b.Name, b.Start, b.Finish, c?.Start, c?.Finish, variance, (c?.WorkHours ?? 0m) - b.WorkHours));
        }
        var baselineFinish = rows.Where(r => r.BaselineFinish is not null).Select(r => r.BaselineFinish!.Value).DefaultIfEmpty().Max();
        DateOnly? bFinish = baselineFinish == default ? null : baselineFinish;
        return new BaselineVariance(projectId, baseline.Number, baseline.Name, bFinish, plan.Finish,
            bFinish is { } x && plan.Finish is { } y ? cal.WorkingDaysBetween(x, y) : null, rows);
    }

    // ---- internals --------------------------------------------------------------------------------------

    private async Task<PlanView> BuildViewAsync(Project project, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == project.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var links = await db.TaskLinks.AsNoTracking().Where(l => l.ProjectId == project.Id).ToListAsync(ct);
        var ids = tasks.Select(t => t.Id).ToHashSet();
        var assignments = await db.TaskAssignments.AsNoTracking().Where(a => ids.Contains(a.TaskId)).ToListAsync(ct);
        var people = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        var skills = await db.Skills.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var wbs = ComputeWbs(tasks, out var levels, out var hasChildren);
        var views = tasks.Select(t => new PlanTaskView(
            t.Id, t.ParentId, wbs[t.Id], levels[t.Id], t.Name, t.Module, t.Kind, hasChildren.Contains(t.Id),
            t.WorkHours, t.WorkIsEstimated, t.FixedDurationDays, t.StartNoEarlierThan, t.PercentComplete, t.Status, t.Priority, t.Notes,
            t.ScheduledStart, t.ScheduledFinish, t.TotalFloatDays, t.IsCritical,
            t.PlannedStart, t.PlannedFinish, t.ActualStart, t.ActualFinish, t.Origin, t.ExternalKey,
            assignments.Where(a => a.TaskId == t.Id).Select(a => new AssignmentView(
                a.Id, a.PersonId, a.PersonId is { } pid && people.TryGetValue(pid, out var n) ? n : null, a.Units, a.FromDate, a.ToDate,
                a.PlaceholderSkillId is { } sid && skills.TryGetValue(sid, out var sn) ? sn : null, a.PlaceholderMinLevel)).ToList())).ToList();

        var top = tasks.Where(t => t.ParentId is null).ToList();
        var start = top.Where(t => t.ScheduledStart is not null).Select(t => t.ScheduledStart!.Value).DefaultIfEmpty().Min();
        var finish = top.Where(t => t.ScheduledFinish is not null).Select(t => t.ScheduledFinish!.Value).DefaultIfEmpty().Max();
        var work = top.Sum(t => t.WorkHours);
        var progress = work > 0 ? top.Sum(t => t.WorkHours * t.PercentComplete) / work : (top.Count == 0 ? 0m : top.Average(t => t.PercentComplete));

        return new PlanView(project.Id, project.ProjectNo, project.Name,
            start == default ? null : start, finish == default ? null : finish, progress,
            views, links.Select(l => new LinkView(l.Id, l.PredecessorId, l.SuccessorId, l.Type, l.LagDays)).ToList(), warnings);
    }

    /// <summary>Rolls start/finish/work/progress/float/critical up from children to summaries, bottom-up.</summary>
    private static void RollUp(List<PlanTask> tasks, HashSet<int> hasChildren)
    {
        var byParent = tasks.Where(t => t.ParentId is not null).GroupBy(t => t.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var t in tasks.OrderByDescending(t => t.SortOrder).Where(t => hasChildren.Contains(t.Id)))
        {
            var kids = byParent[t.Id];
            var starts = kids.Where(k => k.ScheduledStart is not null).Select(k => k.ScheduledStart!.Value).ToList();
            var finishes = kids.Where(k => k.ScheduledFinish is not null).Select(k => k.ScheduledFinish!.Value).ToList();
            t.ScheduledStart = starts.Count > 0 ? starts.Min() : null;
            t.ScheduledFinish = finishes.Count > 0 ? finishes.Max() : null;
            t.WorkHours = kids.Sum(k => k.WorkHours);
            t.PercentComplete = t.WorkHours > 0 ? kids.Sum(k => k.WorkHours * k.PercentComplete) / t.WorkHours : kids.Average(k => k.PercentComplete);
            var floats = kids.Where(k => k.TotalFloatDays is not null).Select(k => k.TotalFloatDays!.Value).ToList();
            t.TotalFloatDays = floats.Count > 0 ? floats.Min() : null;
            t.IsCritical = kids.Any(k => k.IsCritical);
            t.Status = kids.All(k => k.Status == "Completed") ? "Completed" : kids.Any(k => k.Status == "Delayed") ? "Delayed"
                : kids.Any(k => k.Status is "In Progress" or "Completed") ? "In Progress" : "Pending";
        }
    }

    private static Dictionary<int, string> ComputeWbs(List<PlanTask> tasks, out Dictionary<int, int> levels, out HashSet<int> hasChildren)
    {
        var wbs = new Dictionary<int, string>();
        levels = [];
        hasChildren = tasks.Where(t => t.ParentId is not null).Select(t => t.ParentId!.Value).ToHashSet();
        var byParent = tasks.GroupBy(t => t.ParentId).ToDictionary(g => g.Key ?? 0, g => g.OrderBy(t => t.SortOrder).ToList());
        var lv = levels;
        void Walk(int parent, string prefix, int level)
        {
            if (!byParent.TryGetValue(parent, out var kids)) return;
            for (var i = 0; i < kids.Count; i++)
            {
                var code = prefix.Length == 0 ? (i + 1).ToString() : $"{prefix}.{i + 1}";
                wbs[kids[i].Id] = code;
                lv[kids[i].Id] = level;
                Walk(kids[i].Id, code, level + 1);
            }
        }
        Walk(0, "", 1);
        return wbs;
    }

    /// <summary>Renumbers SortOrder 1..n depth-first from the parent/order structure.</summary>
    private async Task NormaliseAsync(int projectId, CancellationToken ct)
    {
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).ToListAsync(ct);
        var byParent = tasks.GroupBy(t => t.ParentId).ToDictionary(g => g.Key ?? 0, g => g.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList());
        var n = 0;
        void Walk(int parent)
        {
            if (!byParent.TryGetValue(parent, out var kids)) return;
            foreach (var k in kids) { k.SortOrder = ++n; Walk(k.Id); }
        }
        Walk(0);
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<PlanTask> Subtree(List<PlanTask> all, int rootId)
    {
        var byParent = all.Where(t => t.ParentId is not null).GroupBy(t => t.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var stack = new Stack<int>([rootId]);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            yield return all.Single(t => t.Id == id);
            if (byParent.TryGetValue(id, out var kids)) foreach (var k in kids) stack.Push(k.Id);
        }
    }

    private static void SwapSubtrees(List<PlanTask> all, PlanTask first, PlanTask second)
    {
        var a = Subtree(all, first.Id).OrderBy(t => t.SortOrder).ToList();
        var b = Subtree(all, second.Id).OrderBy(t => t.SortOrder).ToList();
        var start = a.Min(t => t.SortOrder);
        var order = b.Concat(a).ToList();                      // second subtree now comes first
        for (var i = 0; i < order.Count; i++) order[i].SortOrder = start + i;
    }

    private static bool Reaches(List<TaskLink> links, int from, int to)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>([from]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == to) return true;
            if (!seen.Add(n)) continue;
            foreach (var l in links.Where(l => l.PredecessorId == n)) stack.Push(l.SuccessorId);
        }
        return false;
    }

    private void Audit(string action, string actor, int projectId, string detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = action, SubjectKind = "project", SubjectId = projectId.ToString(), Detail = detail,
        });
}
