namespace EOS.Scheduling;

/// <summary>
/// A crew member's assignment to a task. Active on every working day in [From, To] (inclusive; null = open).
/// A "move on date D" is expressed as To = D-1 on the source task and From = D on the target task (spec §15.4).
/// <see cref="Factor"/> is the productivity factor (skill × experience, spec §9.6); 1 = full speed (Compat profile).
/// </summary>
public sealed record CrewMember(string PersonId, decimal Units = 1m, decimal Factor = 1m, DateOnly? From = null, DateOnly? To = null)
{
    public bool ActiveOn(DateOnly day) => (From is null || day >= From) && (To is null || day <= To);
}

/// <summary>An effort-driven task: fixed work (hours) done by a crew; predecessors are finish-to-start.</summary>
public sealed record ScheduleTask(
    string Id,
    string Name,
    decimal WorkHours,
    IReadOnlyList<string> Predecessors,
    IReadOnlyList<CrewMember> Crew);

public sealed record TaskSchedule(
    string Id,
    DateOnly Start,
    DateOnly Finish,
    int DurationDays,
    DateOnly LateFinish,
    int TotalFloatDays)
{
    public bool IsCritical => TotalFloatDays <= 0;
}

public sealed record ScheduleResult(IReadOnlyDictionary<string, TaskSchedule> Tasks, DateOnly Finish);

public sealed class SchedulingException(string message) : Exception(message);

/// <summary>
/// Deterministic CPM scheduler with effort-driven durations and the whole-day rule (spec §14.2–14.5).
/// A task occupies whole working days; a successor starts on the next whole working day after its predecessor finishes.
/// Pure: no clock, no I/O, no randomness.
/// </summary>
public static class Scheduler
{
    private const int HorizonDays = 3660;
    private const decimal Epsilon = 0.0001m;

    public static ScheduleResult Compute(IReadOnlyList<ScheduleTask> tasks, WorkCalendar calendar, DateOnly projectStart)
    {
        var byId = new Dictionary<string, ScheduleTask>();
        foreach (var t in tasks)
            if (!byId.TryAdd(t.Id, t)) throw new SchedulingException($"Duplicate task id '{t.Id}'.");
        foreach (var t in tasks)
            foreach (var p in t.Predecessors)
                if (!byId.ContainsKey(p)) throw new SchedulingException($"Task '{t.Id}' has unknown predecessor '{p}'.");

        var order = TopologicalOrder(tasks, byId);
        var start = new Dictionary<string, DateOnly>();
        var finish = new Dictionary<string, DateOnly>();
        var earliestStart = calendar.OnOrAfter(projectStart);

        // Forward pass.
        foreach (var t in order)
        {
            var earliest = earliestStart;
            foreach (var p in t.Predecessors)
            {
                var candidate = t.WorkHours == 0m ? finish[p] : calendar.After(finish[p]);
                if (candidate > earliest) earliest = candidate;
            }

            if (t.WorkHours == 0m)
            {
                start[t.Id] = finish[t.Id] = earliest;   // milestone
                continue;
            }

            var (s, f) = Simulate(t, calendar, earliest);
            start[t.Id] = s;
            finish[t.Id] = f;
        }

        var projectFinish = finish.Values.Max();

        // Backward pass (reverse topological order).
        var successors = byId.Keys.ToDictionary(k => k, _ => new List<string>());
        foreach (var t in tasks)
            foreach (var p in t.Predecessors) successors[p].Add(t.Id);

        var lateStart = new Dictionary<string, DateOnly>();
        var lateFinish = new Dictionary<string, DateOnly>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var t = order[i];
            var lf = projectFinish;
            foreach (var sId in successors[t.Id])
            {
                var cap = byId[sId].WorkHours == 0m ? lateStart[sId] : calendar.Before(lateStart[sId]);
                if (cap < lf) lf = cap;
            }
            var duration = Duration(calendar, start[t.Id], finish[t.Id]);
            lateFinish[t.Id] = lf;
            lateStart[t.Id] = t.WorkHours == 0m ? lf : calendar.AddWorkingDays(lf, -(duration - 1));
        }

        var result = new Dictionary<string, TaskSchedule>();
        foreach (var t in tasks)
        {
            var duration = Duration(calendar, start[t.Id], finish[t.Id]);
            var floatDays = calendar.WorkingDaysBetween(finish[t.Id], lateFinish[t.Id]);
            result[t.Id] = new TaskSchedule(t.Id, start[t.Id], finish[t.Id], duration, lateFinish[t.Id], floatDays);
        }
        return new ScheduleResult(result, projectFinish);
    }

    private static int Duration(WorkCalendar cal, DateOnly start, DateOnly finish) =>
        finish < start ? 0 : cal.WorkingDaysBetween(start, finish) + 1;

    /// <summary>Day-by-day simulation of fixed work against the crew active each day.</summary>
    private static (DateOnly Start, DateOnly Finish) Simulate(ScheduleTask task, WorkCalendar cal, DateOnly earliest)
    {
        var remaining = task.WorkHours;
        DateOnly? started = null;
        var day = earliest;
        for (var i = 0; i < HorizonDays; i++, day = cal.After(day))
        {
            var rate = 0m;
            foreach (var m in task.Crew)
                if (m.ActiveOn(day)) rate += cal.HoursPerDay * m.Units * m.Factor;
            if (rate <= 0m) continue;

            started ??= day;
            remaining -= rate;
            if (remaining <= Epsilon) return (started.Value, day);
        }
        throw new SchedulingException(
            $"Task '{task.Id}' cannot be completed within {HorizonDays} days: its crew has no capacity (check assignments and dates).");
    }

    private static List<ScheduleTask> TopologicalOrder(IReadOnlyList<ScheduleTask> tasks, Dictionary<string, ScheduleTask> byId)
    {
        var indegree = tasks.ToDictionary(t => t.Id, t => t.Predecessors.Distinct().Count());
        var successors = tasks.ToDictionary(t => t.Id, _ => new List<string>());
        foreach (var t in tasks)
            foreach (var p in t.Predecessors.Distinct()) successors[p].Add(t.Id);

        var ready = new Queue<string>(tasks.Where(t => indegree[t.Id] == 0).Select(t => t.Id));
        var order = new List<ScheduleTask>(tasks.Count);
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            order.Add(byId[id]);
            foreach (var s in successors[id])
                if (--indegree[s] == 0) ready.Enqueue(s);
        }
        if (order.Count != tasks.Count)
        {
            var stuck = tasks.Where(t => indegree[t.Id] > 0).Select(t => t.Id);
            throw new SchedulingException("Dependency cycle involving: " + string.Join(", ", stuck));
        }
        return order;
    }
}
