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
/// Deterministic CPM scheduler with effort-driven durations and the whole-day rule (spec §14.2–14.6).
/// A task occupies whole working days; a successor starts on the next whole working day after its predecessor finishes.
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
        var run = new Run(tasks, calendar, projectStart, options);
        return run.Execute();
    }

    private sealed class Run
    {
        private readonly IReadOnlyList<ScheduleTask> _tasks;
        private readonly WorkCalendar _cal;
        private readonly DateOnly _projectStart;
        private readonly SchedulingOptions _options;
        private readonly Dictionary<string, ScheduleTask> _byId = new();
        private readonly Dictionary<string, List<string>> _successors = new();
        private readonly Dictionary<string, DateOnly> _start = new();
        private readonly Dictionary<string, DateOnly> _finish = new();
        private readonly HashSet<string> _computing = new();
        private readonly Dictionary<string, HashSet<string>> _descendants = new();
        private readonly Dictionary<string, List<string>> _tasksOfPerson = new();

        public Run(IReadOnlyList<ScheduleTask> tasks, WorkCalendar cal, DateOnly projectStart, SchedulingOptions options)
        {
            _tasks = tasks; _cal = cal; _projectStart = projectStart; _options = options;
        }

        public ScheduleResult Execute()
        {
            foreach (var t in _tasks)
                if (!_byId.TryAdd(t.Id, t)) throw new SchedulingException($"Duplicate task id '{t.Id}'.");
            foreach (var t in _tasks)
                foreach (var p in t.Predecessors)
                    if (!_byId.ContainsKey(p)) throw new SchedulingException($"Task '{t.Id}' has unknown predecessor '{p}'.");

            foreach (var t in _tasks) _successors[t.Id] = [];
            foreach (var t in _tasks)
                foreach (var p in t.Predecessors.Distinct()) _successors[p].Add(t.Id);
            foreach (var t in _tasks)
                foreach (var person in t.Crew.Select(c => c.PersonId).Distinct())
                {
                    if (!_tasksOfPerson.TryGetValue(person, out var list)) _tasksOfPerson[person] = list = [];
                    list.Add(t.Id);
                }

            var order = TopologicalOrder();
            foreach (var t in order) Resolve(t);

            var projectFinish = _finish.Values.Max();
            return Backward(order, projectFinish);
        }

        // ---- forward pass ---------------------------------------------------------------------------

        private void Resolve(ScheduleTask t)
        {
            if (_finish.ContainsKey(t.Id)) return;
            if (!_computing.Add(t.Id))
                throw new SchedulingException($"Pickup rule creates a circular dependency at task '{t.Id}'.");

            var earliest = _cal.OnOrAfter(_projectStart);
            foreach (var p in t.Predecessors)
            {
                Resolve(_byId[p]);
                var candidate = t.WorkHours == 0m ? _finish[p] : _cal.After(_finish[p]);
                if (candidate > earliest) earliest = candidate;
            }

            if (t.WorkHours == 0m)
            {
                _start[t.Id] = _finish[t.Id] = earliest;   // milestone
            }
            else
            {
                var (s, f) = Simulate(t, earliest);
                _start[t.Id] = s;
                _finish[t.Id] = f;
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
                if (!freeFrom.TryGetValue(person, out var from))
                {
                    freeFrom[person] = from = FreeFrom(task, taskIds);
                }
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
            var stack = new Stack<string>(_successors[id]);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!seen.Add(n)) continue;
                foreach (var s in _successors[n]) stack.Push(s);
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
                var lf = projectFinish;
                foreach (var sId in _successors[t.Id])
                {
                    var cap = _byId[sId].WorkHours == 0m ? lateStart[sId] : _cal.Before(lateStart[sId]);
                    if (cap < lf) lf = cap;
                }
                var duration = Duration(_start[t.Id], _finish[t.Id]);
                lateFinish[t.Id] = lf;
                lateStart[t.Id] = t.WorkHours == 0m ? lf : _cal.AddWorkingDays(lf, -(duration - 1));
            }

            var result = new Dictionary<string, TaskSchedule>();
            foreach (var t in _tasks)
            {
                var duration = Duration(_start[t.Id], _finish[t.Id]);
                var floatDays = _cal.WorkingDaysBetween(_finish[t.Id], lateFinish[t.Id]);
                result[t.Id] = new TaskSchedule(t.Id, _start[t.Id], _finish[t.Id], duration, lateFinish[t.Id], floatDays);
            }
            return new ScheduleResult(result, projectFinish);
        }

        private int Duration(DateOnly start, DateOnly finish) =>
            finish < start ? 0 : _cal.WorkingDaysBetween(start, finish) + 1;

        private List<ScheduleTask> TopologicalOrder()
        {
            var indegree = _tasks.ToDictionary(t => t.Id, t => t.Predecessors.Distinct().Count());
            var ready = new Queue<string>(_tasks.Where(t => indegree[t.Id] == 0).Select(t => t.Id));
            var order = new List<ScheduleTask>(_tasks.Count);
            while (ready.Count > 0)
            {
                var id = ready.Dequeue();
                order.Add(_byId[id]);
                foreach (var s in _successors[id])
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
