namespace EOS.Scheduling;

public sealed record ProjectPlan(string Id, string Name, DateOnly Start, IReadOnlyList<ScheduleTask> Tasks);

public sealed record MoveRequest(string PersonId, string FromProjectId, string ToProjectId, DateOnly Date, decimal Percent = 1m);

public sealed record TaskImpact(
    string ProjectId, string TaskId,
    DateOnly BaselineStart, DateOnly BaselineFinish,
    DateOnly ScenarioStart, DateOnly ScenarioFinish,
    int SlipWorkingDays);

public sealed record ProjectImpact(
    string ProjectId, DateOnly BaselineFinish, DateOnly ScenarioFinish, int SlipWorkingDays,
    IReadOnlyList<TaskImpact> Tasks);

public sealed record ScenarioOutcome(
    IReadOnlyList<ProjectPlan> ScenarioPlans,
    IReadOnlyList<ProjectImpact> Projects);

public sealed record ReleaseRow(
    string PersonId,
    string ProjectId,
    DateOnly LastDayOnCurrentWork,
    DateOnly? EarliestNoDelayMove,
    int CostIfMovedOnAnalysisDate,
    int GainToTargetIfMovedOnAnalysisDate);

public static class WhatIf
{
    public static ScenarioOutcome Run(
        IReadOnlyList<ProjectPlan> baseline, IReadOnlyList<MoveRequest> moves, WorkCalendar cal, SchedulingOptions? options = null)
    {
        var plans = baseline.ToDictionary(p => p.Id);
        var baselineSchedules = baseline.ToDictionary(p => p.Id, p => Scheduler.Compute(p.Tasks, cal, p.Start, options));

        foreach (var move in moves.OrderBy(m => m.Date))
        {
            if (!plans.TryGetValue(move.FromProjectId, out var source)) throw new SchedulingException($"Unknown project '{move.FromProjectId}'.");
            if (!plans.TryGetValue(move.ToProjectId, out var dest)) throw new SchedulingException($"Unknown project '{move.ToProjectId}'.");
            if (move.Percent is <= 0m or > 1m) throw new SchedulingException("Move percent must be in (0, 1].");

            plans[source.Id] = source with { Tasks = Leave(source.Tasks, move.PersonId, move.Date, move.Percent) };

            var targetNow = Scheduler.Compute(dest.Tasks, cal, dest.Start, options);
            plans[dest.Id] = dest with { Tasks = Join(dest.Tasks, move.PersonId, move.Date, move.Percent, targetNow) };
        }

        var scenarioPlans = baseline.Select(p => plans[p.Id]).ToList();
        var impacts = new List<ProjectImpact>();
        foreach (var p in scenarioPlans)
        {
            var before = baselineSchedules[p.Id];
            var after = Scheduler.Compute(p.Tasks, cal, p.Start, options);
            var tasks = p.Tasks.Select(t =>
            {
                var b = before.Tasks[t.Id];
                var a = after.Tasks[t.Id];
                return new TaskImpact(p.Id, t.Id, b.Start, b.Finish, a.Start, a.Finish, cal.WorkingDaysBetween(b.Finish, a.Finish));
            }).ToList();
            impacts.Add(new ProjectImpact(p.Id, before.Finish, after.Finish, cal.WorkingDaysBetween(before.Finish, after.Finish), tasks));
        }
        return new ScenarioOutcome(scenarioPlans, impacts);
    }

    public static IReadOnlyList<ReleaseRow> ReleaseAnalysis(
        IReadOnlyList<ProjectPlan> plans, string targetProjectId, DateOnly analysisDate, WorkCalendar cal, SchedulingOptions? options = null)
    {
        var target = plans.Single(p => p.Id == targetProjectId);
        var targetBaseline = Scheduler.Compute(target.Tasks, cal, target.Start, options);
        var start = cal.OnOrAfter(analysisDate);
        var rows = new List<ReleaseRow>();

        foreach (var plan in plans.Where(p => p.Id != targetProjectId))
        {
            var baseline = Scheduler.Compute(plan.Tasks, cal, plan.Start, options);
            var people = plan.Tasks.SelectMany(t => t.Crew).Select(c => c.PersonId).Distinct().OrderBy(x => x, StringComparer.Ordinal);
            foreach (var person in people)
            {
                var lastDay = plan.Tasks.Where(t => t.Crew.Any(c => c.PersonId == person))
                                        .Max(t => baseline.Tasks[t.Id].Finish);

                int Slip(DateOnly on)
                {
                    var left = plan with { Tasks = Leave(plan.Tasks, person, on, 1m) };
                    return cal.WorkingDaysBetween(baseline.Finish, Scheduler.Compute(left.Tasks, cal, left.Start, options).Finish);
                }

                var cost = Slip(start);
                DateOnly? earliest = null;
                for (var d = start; d <= cal.After(baseline.Finish); d = cal.After(d))
                {
                    if (Slip(d) <= 0) { earliest = d; break; }
                }

                var joined = target with { Tasks = Join(target.Tasks, person, start, 1m, targetBaseline) };
                var gain = cal.WorkingDaysBetween(targetBaseline.Finish, Scheduler.Compute(joined.Tasks, cal, joined.Start, options).Finish);

                rows.Add(new ReleaseRow(person, plan.Id, lastDay, earliest, cost, gain));
            }
        }
        return rows;
    }

    public static IReadOnlyList<ScheduleTask> Leave(IReadOnlyList<ScheduleTask> tasks, string person, DateOnly date, decimal percent)
    {
        var result = new List<ScheduleTask>(tasks.Count);
        foreach (var t in tasks)
        {
            var crew = new List<CrewMember>(t.Crew.Count + 1);
            foreach (var m in t.Crew)
            {
                if (m.PersonId != person || (m.To is not null && m.To < date)) { crew.Add(m); continue; }

                if (m.From is null || m.From < date) crew.Add(m with { To = date.AddDays(-1) });
                if (percent < 1m)
                    crew.Add(m with { From = m.From is { } f && f > date ? f : date, Units = m.Units * (1m - percent) });
            }
            result.Add(t with { Crew = crew });
        }
        return result;
    }

    public static IReadOnlyList<ScheduleTask> Join(
        IReadOnlyList<ScheduleTask> tasks, string person, DateOnly date, decimal percent, ScheduleResult currentSchedule)
    {
        var result = new List<ScheduleTask>(tasks.Count);
        foreach (var t in tasks)
        {
            var active = t.WorkHours > 0m && currentSchedule.Tasks[t.Id].Finish >= date;
            var alreadyThere = t.Crew.Any(c => c.PersonId == person && (c.To is null || c.To >= date));
            result.Add(active && !alreadyThere
                ? t with { Crew = [.. t.Crew, new CrewMember(person, percent, From: date)] }
                : t);
        }
        return result;
    }
}
