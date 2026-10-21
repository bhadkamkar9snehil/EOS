using EOS.Domain.Audit;
using EOS.Domain.Capacity;
using EOS.Domain.Planning;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Planning;

public sealed record PlannerTask(int Id, int? ParentId, string Wbs, int Level, string Name, bool IsSummary, bool IsMilestone, decimal WorkHours, decimal Percent);

public sealed record PlannerSkill(int Id, string Name, int Rating);

public sealed record SkillOption(int Id, string Name);

public sealed record SkillMismatch(int TaskId, int PersonId, string Skill, int Have, int Need);

public sealed record Requirement(int SkillId, int Level);

public sealed record PlannerPerson(int Id, string Name, string Initials, decimal Availability, int Experience, IReadOnlyList<PlannerSkill> Skills);

public sealed record PlannerAssignment(int TaskId, int PersonId, decimal Units, DateOnly? From, DateOnly? To);

public sealed record PlannerSpan(DateOnly Start, DateOnly Finish);

public sealed record PersonImpact(int PersonId, string Name, decimal PeakBefore, decimal PeakAfter, int OverBefore, int OverAfter);

public sealed record PlannerResult(
    IReadOnlyDictionary<int, PlannerSpan> Spans,
    IReadOnlyDictionary<int, PlannerSpan> BaseSpans,
    IReadOnlySet<int> Critical,
    DateOnly Finish,
    DateOnly BaseFinish,
    int SlipDays,
    IReadOnlyList<(int TaskId, int SlipDays)> Moved,
    IReadOnlyList<PersonImpact> People,
    IReadOnlyDictionary<int, decimal[]> WeekLoad,
    IReadOnlyDictionary<int, decimal[]> WeekCapacity,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SkillMismatch> Mismatches);

public sealed class PlannerSession
{
    internal BuiltPlan Built = null!;
    internal int UnassignedCrew;
    internal Dictionary<int, decimal[]> OtherWeeks = [];
    internal Dictionary<int, decimal[]> Capacity = [];
    internal Dictionary<int, int> Experience = [];
    internal Dictionary<int, string> Names = [];
    internal Dictionary<int, int> ParentOf = [];
    internal List<int> LeafIds = [];
    public IReadOnlyList<int> LeafTaskIds => LeafIds;

    public int ProjectId { get; internal set; }
    public string ProjectNo { get; internal set; } = "";
    public string ProjectName { get; internal set; } = "";
    public DateOnly Today { get; internal set; }
    public decimal HoursPerDay { get; internal set; }
    public IReadOnlyList<PlannerTask> Tasks { get; internal set; } = [];
    public IReadOnlyList<PlannerPerson> People { get; internal set; } = [];
    public IReadOnlyList<PlannerAssignment> Baseline { get; internal set; } = [];
    public IReadOnlyList<DateOnly> Weeks { get; internal set; } = [];
    public PlannerResult BaselineResult { get; internal set; } = null!;
    public IReadOnlyList<SkillOption> Skills { get; internal set; } = [];
    public IReadOnlyDictionary<int, Requirement> BaselineRequirements { get; internal set; } = new Dictionary<int, Requirement>();
    internal Dictionary<int, string> SkillNames = [];

    public int WorkingDays(PlannerSpan span)
    {
        var cal = Built.Calendar;
        return Math.Max(1, cal.WorkingDaysBetween(cal.OnOrAfter(span.Start), cal.OnOrBefore(span.Finish)) + 1);
    }

    public decimal FreeHours(PlannerResult result, int personId, PlannerSpan span)
    {
        var load = result.WeekLoad.TryGetValue(personId, out var l) ? l : OtherLoad.GetValueOrDefault(personId);
        var cap = Capacity.GetValueOrDefault(personId);
        if (load is null || cap is null) return 0m;
        var free = 0m;
        for (var i = 0; i < Weeks.Count; i++)
        {
            var from = Weeks[i]; var to = from.AddDays(6);
            if (to < span.Start || from > span.Finish) continue;
            var overlap = Math.Min(to.DayNumber, span.Finish.DayNumber) - Math.Max(from.DayNumber, span.Start.DayNumber) + 1;
            free += Math.Max(0m, cap[i] - load[i]) * overlap / 7m;
        }
        return free;
    }

    internal Dictionary<int, decimal[]> PeerWeeks = [];
    internal Dictionary<int, decimal[]> EffectiveOther = [];

    public IReadOnlyDictionary<int, decimal[]> OtherLoad => EffectiveOther.Count > 0 ? EffectiveOther : OtherWeeks;
    public IReadOnlyDictionary<int, decimal[]> WeekCapacityOf => Capacity;
    public IReadOnlyDictionary<int, decimal[]> CommittedWeeks => BaselineLoad;

    public IReadOnlyDictionary<int, decimal[]> DraftWeekly(IReadOnlyList<PlannerAssignment> draft, PlannerResult result) => WeeklyHours(draft, result.Spans, Built.Calendar);

    public void SetPeers(IReadOnlyDictionary<int, decimal[]> adjustment)
    {
        PeerWeeks = adjustment.ToDictionary(kv => kv.Key, kv => kv.Value);
        EffectiveOther = OtherWeeks.ToDictionary(kv => kv.Key, kv =>
        {
            var sum = (decimal[])kv.Value.Clone();
            if (PeerWeeks.TryGetValue(kv.Key, out var adj)) for (var i = 0; i < sum.Length && i < adj.Length; i++) sum[i] = Math.Max(0m, sum[i] + adj[i]);
            return sum;
        });
    }

    public IReadOnlyList<PlannerSpan> Split(PlannerSpan span, int parts)
    {
        var cal = Built.Calendar;
        var days = Math.Max(1, cal.WorkingDaysBetween(cal.OnOrAfter(span.Start), cal.OnOrBefore(span.Finish)) + 1);
        var result = new List<PlannerSpan>();
        var cursor = cal.OnOrAfter(span.Start);
        for (var i = 0; i < parts; i++)
        {
            var take = Math.Max(1, (int)Math.Round((double)days * (i + 1) / parts) - (int)Math.Round((double)days * i / parts));
            var end = i == parts - 1 ? span.Finish : cal.AddWorkingDays(cursor, take - 1);
            result.Add(new PlannerSpan(cursor, end));
            cursor = cal.After(end);
        }
        return result;
    }

    public PlannerResult Simulate(IReadOnlyList<PlannerAssignment> draft, bool experience, IReadOnlyDictionary<int, Requirement>? requirements = null)
    {
        requirements ??= BaselineRequirements;
        var cal = Built.Calendar;
        var tasks = Built.Plan.Tasks.Select(t =>
        {
            var id = int.Parse(t.Id);
            var crew = draft.Where(a => a.TaskId == id)
                .Select(a => new CrewMember(a.PersonId.ToString(), a.Units, experience ? Factor(a.PersonId) : 1m, a.From, a.To)).ToList();
            if (crew.Count == 0 && t.WorkHours > 0m && t.FixedDurationDays is null)
                crew = Enumerable.Range(1, Math.Max(1, UnassignedCrew)).Select(i => new CrewMember($"unassigned-{i}")).ToList();
            return t with { Crew = crew };
        }).ToList();

        var schedule = Scheduler.Compute(tasks, cal, Built.Plan.Start, Built.Options);
        var spans = new Dictionary<int, PlannerSpan>();
        var critical = new HashSet<int>();
        foreach (var (key, s) in schedule.Tasks)
        {
            var id = int.Parse(key);
            spans[id] = new PlannerSpan(s.Start, s.Finish);
            if (s.IsCritical) critical.Add(id);
        }
        RollUp(spans);

        var baseSpans = BaselineResult?.Spans ?? spans;
        var baseFinish = BaselineResult?.Finish ?? schedule.Finish;
        var moved = new List<(int, int)>();
        foreach (var (id, span) in spans)
        {
            if (!baseSpans.TryGetValue(id, out var b) || b.Finish == span.Finish) continue;
            moved.Add((id, cal.WorkingDaysBetween(b.Finish, span.Finish)));
        }

        var draftWeeks = WeeklyHours(draft, spans, cal);
        var baseWeeks = BaselineResult is null ? draftWeeks : BaselineLoad;
        var load = new Dictionary<int, decimal[]>();
        var capacity = new Dictionary<int, decimal[]>();
        var impacts = new List<PersonImpact>();
        var involved = draft.Select(a => a.PersonId).Concat(Baseline.Select(a => a.PersonId)).Distinct().ToList();
        var warnings = new List<string>();
        foreach (var pid in involved)
        {
            var other = OtherWeeks.GetValueOrDefault(pid) ?? new decimal[Weeks.Count];
            var otherNow = EffectiveOther.GetValueOrDefault(pid) ?? other;
            var cap = Capacity.GetValueOrDefault(pid) ?? new decimal[Weeks.Count];
            var total = new decimal[Weeks.Count];
            var before = new decimal[Weeks.Count];
            for (var i = 0; i < Weeks.Count; i++)
            {
                total[i] = otherNow[i] + (draftWeeks.GetValueOrDefault(pid)?[i] ?? 0m);
                before[i] = other[i] + (baseWeeks.GetValueOrDefault(pid)?[i] ?? 0m);
            }
            load[pid] = total; capacity[pid] = cap;
            decimal Peak(decimal[] w) => Enumerable.Range(0, Weeks.Count).Where(i => cap[i] > 0).Select(i => w[i] / cap[i]).DefaultIfEmpty(0m).Max();
            int Over(decimal[] w) => Enumerable.Range(0, Weeks.Count).Count(i => cap[i] > 0 && w[i] > cap[i] + 0.01m);
            var name = Names.GetValueOrDefault(pid, "?");
            var overAfter = Over(total);
            impacts.Add(new PersonImpact(pid, name, Peak(before), Peak(total), Over(before), overAfter));
            if (overAfter > 0 && draft.Any(a => a.PersonId == pid))
                warnings.Add($"{name} is over capacity in {overAfter} week(s){(Over(before) > 0 && overAfter <= Over(before) ? " (already was)" : "")}.");
        }
        var mismatches = new List<SkillMismatch>();
        foreach (var a in draft)
        {
            if (!requirements.TryGetValue(a.TaskId, out var need)) continue;
            var have = People.FirstOrDefault(p => p.Id == a.PersonId)?.Skills.FirstOrDefault(s => s.Id == need.SkillId)?.Rating ?? 0;
            if (have >= need.Level) continue;
            mismatches.Add(new SkillMismatch(a.TaskId, a.PersonId, SkillNames.GetValueOrDefault(need.SkillId, "skill"), have, need.Level));
        }
        foreach (var g in mismatches.GroupBy(m => m.PersonId))
            warnings.Add($"{Names.GetValueOrDefault(g.Key, "?")} is below the required skill level on {g.Count()} task(s).");
        var unstaffed = tasks.Count(t => t.Crew.Any(c => c.PersonId.StartsWith("unassigned-", StringComparison.Ordinal)));
        if (unstaffed > 0) warnings.Add($"{unstaffed} task(s) have no named engineer; a default crew is assumed.");

        return new PlannerResult(spans, baseSpans, critical, schedule.Finish, baseFinish, cal.WorkingDaysBetween(baseFinish, schedule.Finish),
            moved.OrderByDescending(m => Math.Abs(m.Item2)).ToList(), impacts, load, capacity, warnings, mismatches);
    }

    internal Dictionary<int, decimal[]> BaselineLoad = [];

    internal Dictionary<int, decimal[]> WeeklyHours(IReadOnlyList<PlannerAssignment> assignments, IReadOnlyDictionary<int, PlannerSpan> spans, WorkCalendar cal)
    {
        var result = new Dictionary<int, decimal[]>();
        var start = Weeks[0];
        foreach (var a in assignments)
        {
            if (!spans.TryGetValue(a.TaskId, out var span)) continue;
            var from = a.From is { } f && f > span.Start ? f : span.Start;
            var to = a.To is { } t && t < span.Finish ? t : span.Finish;
            if (!result.TryGetValue(a.PersonId, out var arr)) result[a.PersonId] = arr = new decimal[Weeks.Count];
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!cal.IsWorkingDay(d)) continue;
                var w = (d.DayNumber - start.DayNumber) / 7;
                if (w >= 0 && w < arr.Length) arr[w] += a.Units * cal.HoursOn(d);
            }
        }
        return result;
    }

    private decimal Factor(int personId)
    {
        var level = Experience.GetValueOrDefault(personId);
        return level == 0 ? 1m : Math.Clamp(0.7m + 0.1m * level, 0.7m, 1.2m);
    }

    private void RollUp(Dictionary<int, PlannerSpan> spans)
    {
        foreach (var t in Tasks.OrderByDescending(t => t.Level))
        {
            if (!t.IsSummary) continue;
            var kids = Tasks.Where(k => k.ParentId == t.Id && spans.ContainsKey(k.Id)).ToList();
            if (kids.Count == 0) continue;
            spans[t.Id] = new PlannerSpan(kids.Min(k => spans[k.Id].Start), kids.Max(k => spans[k.Id].Finish));
        }
    }
}

public sealed class PlannerService(EosDbContext db)
{
    public async Task<PlannerSession> PrepareAsync(int projectId, DateOnly today, CancellationToken ct = default)
    {
        var built = await PlanScheduleBuilder.BuildAsync(db, projectId, today, ct);
        var cal = built.Calendar;
        var session = new PlannerSession
        {
            Built = built, ProjectId = projectId, ProjectNo = built.Project.ProjectNo, ProjectName = built.Project.Name, Today = today, HoursPerDay = cal.HoursPerDay,
        };
        session.UnassignedCrew = built.Plan.Tasks.SelectMany(t => t.Crew).Count(c => c.PersonId.StartsWith("unassigned-", StringComparison.Ordinal)) > 0
            ? Math.Max(1, built.Plan.Tasks.Select(t => t.Crew.Count(c => c.PersonId.StartsWith("unassigned-", StringComparison.Ordinal))).Max()) : 1;

        var sorted = built.Tasks.OrderBy(t => t.SortOrder).ToList();
        var children = sorted.ToLookup(t => t.ParentId);
        var plannerTasks = new List<PlannerTask>();
        void Walk(int? parent, string prefix, int level)
        {
            var i = 1;
            foreach (var t in children[parent])
            {
                var wbs = prefix.Length == 0 ? i.ToString() : prefix + "." + i;
                plannerTasks.Add(new PlannerTask(t.Id, t.ParentId, wbs, level, t.Name, built.HasChildren.Contains(t.Id), t.Kind == TaskKinds.Milestone, t.WorkHours, t.PercentComplete));
                session.ParentOf[t.Id] = t.ParentId ?? 0;
                Walk(t.Id, wbs, level + 1);
                i++;
            }
        }
        Walk(null, "", 0);
        session.Tasks = plannerTasks;
        session.LeafIds = plannerTasks.Where(t => !t.IsSummary).Select(t => t.Id).ToList();

        var taskIds = built.Tasks.Select(t => t.Id).ToList();
        session.Baseline = (await db.TaskAssignments.AsNoTracking().Where(a => a.PersonId != null && taskIds.Contains(a.TaskId)).ToListAsync(ct))
            .Select(a => new PlannerAssignment(a.TaskId, a.PersonId!.Value, a.Units, a.FromDate, a.ToDate)).ToList();

        var skillRows = await db.Skills.AsNoTracking().OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        session.Skills = skillRows.Select(s => new SkillOption(s.Id, s.Name)).ToList();
        session.SkillNames = skillRows.ToDictionary(s => s.Id, s => s.Name);
        session.BaselineRequirements = built.Tasks.Where(t => t.RequiredSkillId is not null && t.RequiredLevel is not null)
            .ToDictionary(t => t.Id, t => new Requirement(t.RequiredSkillId!.Value, t.RequiredLevel!.Value));

        var people = await db.People.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.FullName).ToListAsync(ct);
        var ratings = await db.PersonSkills.AsNoTracking().Join(db.Skills, r => r.SkillId, s => s.Id, (r, s) => new { r.PersonId, SkillId = s.Id, s.Name, r.Rating }).Where(x => x.Rating > 0).ToListAsync(ct);
        session.People = people.Select(p =>
        {
            var mine = ratings.Where(r => r.PersonId == p.Id).OrderByDescending(r => r.Rating).ToList();
            var exp = mine.Count == 0 ? 0 : (int)Math.Round(mine.Average(r => r.Rating));
            session.Experience[p.Id] = exp; session.Names[p.Id] = p.FullName;
            return new PlannerPerson(p.Id, p.FullName, Initials(p.FullName), p.AvailabilityFraction, exp, mine.Select(r => new PlannerSkill(r.SkillId, r.Name, r.Rating)).ToList());
        }).ToList();

        var baseSchedule = Scheduler.Compute(built.Plan.Tasks, cal, built.Plan.Start, built.Options);
        var winStart = Monday(new[] { baseSchedule.Start(), today.AddDays(-56) }.Max());
        var winEnd = Monday(new[] { baseSchedule.Finish, today }.Max());
        var weekCount = Math.Clamp(((winEnd.DayNumber - winStart.DayNumber) / 7) + 12, 16, 70);
        session.Weeks = Enumerable.Range(0, weekCount).Select(i => winStart.AddDays(i * 7)).ToList();

        var leaves = await db.Leaves.AsNoTracking().ToListAsync(ct);
        foreach (var p in people)
        {
            var cap = new decimal[weekCount];
            for (var w = 0; w < weekCount; w++)
            {
                var hours = Enumerable.Range(0, 7).Select(d => session.Weeks[w].AddDays(d)).Sum(d => cal.HoursOn(d) * (1m - Leave.DayFraction(leaves, p.Id, d)));
                cap[w] = hours * p.AvailabilityFraction;
            }
            session.Capacity[p.Id] = cap;
            session.OtherWeeks[p.Id] = new decimal[weekCount];
        }

        var otherTasks = await db.Tasks.AsNoTracking()
            .Where(t => t.ProjectId != projectId && t.ScheduledStart != null && t.ScheduledFinish != null && t.PercentComplete < 1m)
            .Select(t => new { t.Id, Start = t.ScheduledStart!.Value, Finish = t.ScheduledFinish!.Value }).ToListAsync(ct);
        var otherSpans = otherTasks.ToDictionary(t => t.Id, t => new PlannerSpan(t.Start, t.Finish));
        var otherAssignments = await db.TaskAssignments.AsNoTracking().Where(a => a.PersonId != null).ToListAsync(ct);
        var otherHours = session.WeeklyHours(otherAssignments
            .Where(a => session.OtherWeeks.ContainsKey(a.PersonId!.Value))
            .Select(a => new PlannerAssignment(a.TaskId, a.PersonId!.Value, a.Units, a.FromDate, a.ToDate)).ToList(), otherSpans, cal);
        foreach (var personId in session.OtherWeeks.Keys.ToList())
            if (otherHours.TryGetValue(personId, out var hours)) session.OtherWeeks[personId] = hours;

        var baseSpans = new Dictionary<int, PlannerSpan>();
        foreach (var (key, s) in baseSchedule.Tasks) baseSpans[int.Parse(key)] = new PlannerSpan(s.Start, s.Finish);
        var probe = session.Simulate(session.Baseline, false);
        session.BaselineResult = probe with { BaseSpans = probe.Spans, BaseFinish = probe.Finish, SlipDays = 0, Moved = [] };
        session.BaselineLoad = session.WeeklyHours(session.Baseline, session.BaselineResult.Spans, cal);
        return session;
    }

    public async Task<int> CommitAsync(
        PlannerSession session, IReadOnlyList<PlannerAssignment> draft, IReadOnlyDictionary<int, Requirement> requirements, string actor, CancellationToken ct = default)
    {
        static string Key(PlannerAssignment a) => $"{a.PersonId}|{a.Units}|{a.From}|{a.To}";
        var changed = session.LeafIds.Where(id =>
            !draft.Where(a => a.TaskId == id).Select(Key).OrderBy(x => x).SequenceEqual(session.Baseline.Where(a => a.TaskId == id).Select(Key).OrderBy(x => x))).ToList();
        var reqChanged = session.LeafIds.Where(id => requirements.GetValueOrDefault(id) != session.BaselineRequirements.GetValueOrDefault(id)).ToList();
        if (changed.Count == 0 && reqChanged.Count == 0) return 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var task in await db.Tasks.Where(t => reqChanged.Contains(t.Id)).ToListAsync(ct))
        {
            var r = requirements.GetValueOrDefault(task.Id);
            task.RequiredSkillId = r?.SkillId; task.RequiredLevel = r?.Level;
        }
        var existing = await db.TaskAssignments.Where(a => changed.Contains(a.TaskId) && a.PersonId != null).ToListAsync(ct);
        db.TaskAssignments.RemoveRange(existing);
        foreach (var a in draft.Where(a => changed.Contains(a.TaskId)))
            db.TaskAssignments.Add(new TaskAssignment { TaskId = a.TaskId, PersonId = a.PersonId, Units = a.Units, FromDate = a.From, ToDate = a.To });
        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = "planner.commit", SubjectKind = "project", SubjectId = session.ProjectNo, Detail = $"{changed.Count} task(s) reassigned",
        });
        await db.SaveChangesAsync(ct);
        await new PlanService(db).RecomputeAsync(session.ProjectId, session.Today, ct);
        await tx.CommitAsync(ct);
        return changed.Count + reqChanged.Except(changed).Count();
    }

    private static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant() : ($"{parts[0][0]}{parts[^1][0]}").ToUpperInvariant();
    }
}

internal static class ScheduleResultExtensions
{
    public static DateOnly Start(this ScheduleResult r) => r.Tasks.Count == 0 ? r.Finish : r.Tasks.Values.Min(t => t.Start);
}
