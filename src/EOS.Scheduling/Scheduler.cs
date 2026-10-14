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

public enum LinkType
{
    FinishToStart = 0,
    StartToStart = 1,
    FinishToFinish = 2,
    StartToFinish = 3,
}

/// <summary>A precedence relationship with a signed lag in working days (spec §14.4).</summary>
public sealed record TaskDependency(string PredecessorId, LinkType Type = LinkType.FinishToStart, int LagDays = 0);

/// <summary>
/// A schedulable task. Default behaviour is effort-driven (fixed work done by a crew). A fixed-duration task occupies
/// exactly <see cref="FixedDurationDays"/> working days whatever the crew (e.g. a customer-booked FAT).
/// A task with no work and no fixed duration is a milestone.
/// </summary>
public sealed record ScheduleTask(
    string Id,
    string Name,
    decimal WorkHours,
    IReadOnlyList<string> Predecessors,
    IReadOnlyList<CrewMember> Crew)
{
    /// <summary>Further dependencies (any link type, with lag). <see cref="Predecessors"/> are finish-to-start, lag 0.</summary>
    public IReadOnlyList<TaskDependency> Dependencies { get; init; } = [];
    public int? FixedDurationDays { get; init; }
    public DateOnly? StartNoEarlierThan { get; init; }

    public bool IsMilestone => WorkHours == 0m && (FixedDurationDays ?? 0) == 0;

    internal IEnumerable<TaskDependency> AllDependencies() =>
        Predecessors.Select(p => new TaskDependency(p)).Concat(Dependencies)
                    .GroupBy(d => (d.PredecessorId, d.Type, d.LagDays)).Select(g => g.First());
}

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

/// <summary>What happens on a day when a task has remaining work but no active crew (spec §14.2 rule 4).</summary>
public enum PickupRule
{
    /// <summary>No pickup: the task simply waits for a crew (and fails if none ever arrives).</summary>
    None = 0,

    /// <summary>
    /// The rest of the project's team (everyone with an active assignment that day) picks the task up
    /// after finishing their own tasks, i.e. all tasks they are crew on that do not depend on this one.
    /// </summary>
    AfterOwnTasks = 1,
}

public sealed record SchedulingOptions(PickupRule Pickup = PickupRule.AfterOwnTasks)
{
    public static SchedulingOptions Default { get; } = new();
}

/// <summary>
/// Deterministic CPM scheduler with effort-driven durations, the whole-day rule, four link types with lag,
/// fixed-duration tasks and start constraints (spec §14.2–14.6). A task occupies whole working days.
/// Pure: no clock, no I/O, no randomness.
/// </summary>
public static class Scheduler
{
    private const int HorizonDays = 3660;
    private const decimal Epsilon = 0.0001m;

    public static ScheduleResult Compute(
        IReadOnlyList<ScheduleTask> tasks, WorkCalendar calendar, DateOnly projectStart, SchedulingOptions? options = null)
    {
        options ??= SchedulingOptions.Default;
        return new Run(tasks, calendar, projectStart, options).Execute();
    }

    private sealed record Link(string Other, LinkType Type, int Lag);

    private sealed class Run
    {
        private readonly IReadOnlyList<ScheduleTask> _tasks;
        private readonly WorkCalendar _cal;
        private readonly DateOnly _projectStart;
        private readonly SchedulingOptions _options;
        private readonly Dictionary<string, ScheduleTask> _byId = new();
        private readonly Dictionary<string, List<Link>> _incoming = new();     // Other = predecessor
        private readonly Dictionary<string, List<Link>> _outgoing = new();     // Other = successor
        private readonly Dictionary<string, DateOnly> _start = new();
        private readonly Dictionary<string, DateOnly> _finish = new();
        private readonly HashSet<string> _computing = new();
        private readonly Dictionary<string, HashSet<string>> _descendants = new();
        private readonly Dictionary<string, List<string>> _tasksOfPerson = new();
        private DateOnly _floor;

        public Run(IReadOnlyList<ScheduleTask> tasks, WorkCalendar cal, DateOnly projectStart, SchedulingOptions options)
        {
            _tasks = tasks; _cal = cal; _projectStart = projectStart; _options = options;
        }

        public ScheduleResult Execute()
        {
            foreach (var t in _tasks)
                if (!_byId.TryAdd(t.Id, t)) throw new SchedulingException($"Duplicate task id '{t.Id}'.");
            foreach (var t in _tasks) { _incoming[t.Id] = []; _outgoing[t.Id] = []; }
            foreach (var t in _tasks)
                foreach (var d in t.AllDependencies())
                {
                    if (!_byId.ContainsKey(d.PredecessorId)) throw new SchedulingException($"Task '{t.Id}' has unknown predecessor '{d.PredecessorId}'.");
                    if (d.PredecessorId == t.Id) throw new SchedulingException($"Task '{t.Id}' depends on itself.");
                    _incoming[t.Id].Add(new Link(d.PredecessorId, d.Type, d.LagDays));
                    _outgoing[d.PredecessorId].Add(new Link(t.Id, d.Type, d.LagDays));
                }
            foreach (var t in _tasks)
                foreach (var person in t.Crew.Select(c => c.PersonId).Distinct())
                {
                    if (!_tasksOfPerson.TryGetValue(person, out var list)) _tasksOfPerson[person] = list = [];
                    list.Add(t.Id);
                }

            _floor = _cal.OnOrAfter(_projectStart);
            var order = TopologicalOrder();
            foreach (var t in order) Resolve(t);

            var projectFinish = _tasks.Count == 0 ? _floor : _finish.Values.Max();
            return Backward(order, projectFinish);
        }

        // ---- forward pass ---------------------------------------------------------------------------

        private DateOnly Shift(DateOnly day, int lag)
        {
            if (lag == 0) return day;
            var shifted = _cal.AddWorkingDays(_cal.OnOrAfter(day), lag);
            return shifted < _floor ? _floor : shifted;
        }

        private void Resolve(ScheduleTask t)
        {
            if (_finish.ContainsKey(t.Id)) return;
            if (!_computing.Add(t.Id))
                throw new SchedulingException($"Pickup rule creates a circular dependency at task '{t.Id}'.");

            var earliest = _floor;
            if (t.StartNoEarlierThan is { } snet && _cal.OnOrAfter(snet) > earliest) earliest = _cal.OnOrAfter(snet);
            DateOnly? finishBound = null;

            foreach (var link in _incoming[t.Id])
            {
                Resolve(_byId[link.Other]);
                switch (link.Type)
                {
                    case LinkType.FinishToStart:
                        var fs = Shift(t.IsMilestone ? _finish[link.Other] : _cal.After(_finish[link.Other]), link.Lag);
                        if (fs > earliest) earliest = fs;
                        break;
                    case LinkType.StartToStart:
                        var ss = Shift(_start[link.Other], link.Lag);
                        if (ss > earliest) earliest = ss;
                        break;
                    case LinkType.FinishToFinish:
                        var ff = Shift(_finish[link.Other], link.Lag);
                        if (finishBound is null || ff > finishBound) finishBound = ff;
                        break;
                    case LinkType.StartToFinish:
                        var sf = Shift(_start[link.Other], link.Lag);
                        if (finishBound is null || sf > finishBound) finishBound = sf;
                        break;
                }
            }
            earliest = _cal.OnOrAfter(earliest);

            if (t.IsMilestone)
            {
                var day = finishBound is { } b && b > earliest ? b : earliest;
                _start[t.Id] = _finish[t.Id] = _cal.OnOrAfter(day);
            }
            else if (t.FixedDurationDays is int n and > 0)
            {
                var start = earliest;
                var finish = _cal.AddWorkingDays(start, n - 1);
                if (finishBound is { } fb && fb > finish)
                {
                    finish = _cal.OnOrAfter(fb);
                    start = _cal.AddWorkingDays(finish, -(n - 1));
                    if (start < earliest) { start = earliest; finish = _cal.AddWorkingDays(start, n - 1); }
                }
                _start[t.Id] = start; _finish[t.Id] = finish;
            }
            else
            {
                var (s, f) = Simulate(t, earliest);
                for (var guard = 0; finishBound is { } fb2 && f < fb2 && guard < HorizonDays; guard++)
                {
                    earliest = _cal.After(earliest);
                    (s, f) = Simulate(t, earliest);
                }
                _start[t.Id] = s; _finish[t.Id] = f;
            }
            _computing.Remove(t.Id);
        }

        /// <summary>Day-by-day simulation of fixed work against the crew active each day (plus pickup).</summary>
        private (DateOnly Start, DateOnly Finish) Simulate(ScheduleTask task, DateOnly earliest)
        {
            var remaining = task.WorkHours;
            DateOnly? started = null;
            var day = earliest;
            Dictionary<string, DateOnly>? freeFrom = null;

            for (var i = 0; i < HorizonDays; i++, day = _cal.After(day))
            {
                var rate = 0m;
                foreach (var m in task.Crew)
                    if (m.ActiveOn(day)) rate += _cal.HoursPerDay * m.Units * m.Factor;

                if (rate <= 0m && _options.Pickup == PickupRule.AfterOwnTasks)
                {
                    freeFrom ??= [];
                    rate = PickupRate(task, day, freeFrom);
                }
                if (rate <= 0m) continue;

                started ??= day;
                remaining -= rate;
                if (remaining <= Epsilon) return (started.Value, day);
            }
            throw new SchedulingException(
                $"Task '{task.Id}' cannot be completed within {HorizonDays} days: its crew has no capacity (check assignments and dates).");
        }

        /// <summary>Hours the rest of the team contributes on <paramref name="day"/> to a task that has lost all its crew.</summary>
        private decimal PickupRate(ScheduleTask task, DateOnly day, Dictionary<string, DateOnly> freeFrom)
        {
            var rate = 0m;
            foreach (var (person, taskIds) in _tasksOfPerson)
            {
                var active = ActiveMemberOn(person, day);
                if (active is null) continue;                       // not on the project that day (e.g. has left)
                if (!freeFrom.TryGetValue(person, out var from)) freeFrom[person] = from = FreeFrom(task, taskIds);
                if (day >= from) rate += _cal.HoursPerDay * active.Units * active.Factor;
            }
            return rate;
        }

        private CrewMember? ActiveMemberOn(string person, DateOnly day)
        {
            foreach (var tid in _tasksOfPerson[person])
                foreach (var m in _byId[tid].Crew)
                    if (m.PersonId == person && m.ActiveOn(day)) return m;
            return null;
        }

        /// <summary>First day a person is free: the working day after they finish all their tasks that do not depend on <paramref name="orphan"/>.</summary>
        private DateOnly FreeFrom(ScheduleTask orphan, List<string> ownTaskIds)
        {
            var descendants = Descendants(orphan.Id);
            DateOnly? last = null;
            foreach (var tid in ownTaskIds)
            {
                if (tid == orphan.Id || descendants.Contains(tid)) continue;
                Resolve(_byId[tid]);
                if (last is null || _finish[tid] > last) last = _finish[tid];
            }
            return last is null ? DateOnly.MinValue : _cal.After(last.Value);
        }

        private HashSet<string> Descendants(string id)
        {
            if (_descendants.TryGetValue(id, out var cached)) return cached;
            var seen = new HashSet<string>();
            var stack = new Stack<string>(_outgoing[id].Select(l => l.Other));
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!seen.Add(n)) continue;
                foreach (var s in _outgoing[n]) stack.Push(s.Other);
            }
            return _descendants[id] = seen;
        }

        // ---- backward pass --------------------------------------------------------------------------

        private ScheduleResult Backward(List<ScheduleTask> order, DateOnly projectFinish)
        {
            var lateStart = new Dictionary<string, DateOnly>();
            var lateFinish = new Dictionary<string, DateOnly>();
            for (var i = order.Count - 1; i >= 0; i--)
            {
                var t = order[i];
                var duration = Duration(t);
                var lf = projectFinish;
                DateOnly? lsCap = null;

                foreach (var link in _outgoing[t.Id])
                {
                    var s = _byId[link.Other];
                    switch (link.Type)
                    {
                        case LinkType.FinishToStart:
                            var edge = s.IsMilestone ? lateStart[s.Id] : _cal.Before(lateStart[s.Id]);
                            lf = Min(lf, Back(edge, link.Lag));
                            break;
                        case LinkType.FinishToFinish:
                            lf = Min(lf, Back(lateFinish[s.Id], link.Lag));
                            break;
                        case LinkType.StartToStart:
                            lsCap = MinN(lsCap, Back(lateStart[s.Id], link.Lag));
                            break;
                        case LinkType.StartToFinish:
                            lsCap = MinN(lsCap, Back(lateFinish[s.Id], link.Lag));
                            break;
                    }
                }

                var ls = duration == 0 ? lf : _cal.AddWorkingDays(lf, -(duration - 1));
                if (lsCap is { } cap && cap < ls)
                {
                    ls = cap;
                    lf = duration == 0 ? ls : _cal.AddWorkingDays(ls, duration - 1);
                }
                lateStart[t.Id] = ls;
                lateFinish[t.Id] = lf;
            }

            var result = new Dictionary<string, TaskSchedule>();
            foreach (var t in _tasks)
            {
                var floatDays = _cal.WorkingDaysBetween(_finish[t.Id], lateFinish[t.Id]);
                result[t.Id] = new TaskSchedule(t.Id, _start[t.Id], _finish[t.Id], Duration(t), lateFinish[t.Id], floatDays);
            }
            return new ScheduleResult(result, projectFinish);
        }

        private DateOnly Back(DateOnly day, int lag) => lag == 0 ? day : _cal.AddWorkingDays(_cal.OnOrBefore(day), -lag);
        private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
        private static DateOnly MinN(DateOnly? a, DateOnly b) => a is { } x && x < b ? x : b;

        private int Duration(ScheduleTask t) =>
            t.IsMilestone || _finish[t.Id] < _start[t.Id] ? 0 : _cal.WorkingDaysBetween(_start[t.Id], _finish[t.Id]) + 1;

        private List<ScheduleTask> TopologicalOrder()
        {
            var indegree = _tasks.ToDictionary(t => t.Id, t => _incoming[t.Id].Select(l => l.Other).Distinct().Count());
            var ready = new Queue<string>(_tasks.Where(t => indegree[t.Id] == 0).Select(t => t.Id));
            var order = new List<ScheduleTask>(_tasks.Count);
            while (ready.Count > 0)
            {
                var id = ready.Dequeue();
                order.Add(_byId[id]);
                foreach (var s in _outgoing[id].Select(l => l.Other).Distinct())
                    if (--indegree[s] == 0) ready.Enqueue(s);
            }
            if (order.Count != _tasks.Count)
            {
                var stuck = _tasks.Where(t => indegree[t.Id] > 0).Select(t => t.Id);
                throw new SchedulingException("Dependency cycle involving: " + string.Join(", ", stuck));
            }
            return order;
        }
    }
}
