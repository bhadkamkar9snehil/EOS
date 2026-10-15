namespace EOS.Scheduling;

public sealed class WorkCalendar
{
    private const int SearchLimitDays = 3660;
    private readonly bool[] _works = new bool[7];
    private readonly HashSet<DateOnly> _holidays;
    private readonly HashSet<DateOnly> _workingOverrides;

    public WorkCalendar(decimal hoursPerDay, IEnumerable<DayOfWeek> workDays, IEnumerable<DateOnly>? holidays = null,
        IEnumerable<DateOnly>? workingOverrides = null)
    {
        if (hoursPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(hoursPerDay), "Hours per day must be positive.");
        HoursPerDay = hoursPerDay;
        foreach (var d in workDays) _works[(int)d] = true;
        if (!_works.Any(x => x)) throw new ArgumentException("A calendar needs at least one working weekday.", nameof(workDays));
        _holidays = [.. holidays ?? []];
        _workingOverrides = [.. workingOverrides ?? []];
    }

    public decimal HoursPerDay { get; }

    public static WorkCalendar MondayToSaturday(decimal hoursPerDay = 8m) => new(hoursPerDay,
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]);

    public static WorkCalendar MondayToFriday(decimal hoursPerDay = 8m) => new(hoursPerDay,
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);

    public bool IsWorkingDay(DateOnly date) =>
        !_holidays.Contains(date) && (_works[(int)date.DayOfWeek] || _workingOverrides.Contains(date));

    public DateOnly OnOrAfter(DateOnly date)
    {
        for (var i = 0; i < SearchLimitDays; i++, date = date.AddDays(1))
            if (IsWorkingDay(date)) return date;
        throw new InvalidOperationException("Calendar has no working day within the search horizon.");
    }

    public DateOnly OnOrBefore(DateOnly date)
    {
        for (var i = 0; i < SearchLimitDays; i++, date = date.AddDays(-1))
            if (IsWorkingDay(date)) return date;
        throw new InvalidOperationException("Calendar has no working day within the search horizon.");
    }

    public DateOnly After(DateOnly date) => OnOrAfter(date.AddDays(1));

    public DateOnly Before(DateOnly date) => OnOrBefore(date.AddDays(-1));

    public DateOnly AddWorkingDays(DateOnly workingDay, int count)
    {
        var d = workingDay;
        for (; count > 0; count--) d = After(d);
        for (; count < 0; count++) d = Before(d);
        return d;
    }

    public int WorkingDaysBetween(DateOnly from, DateOnly to)
    {
        if (to == from) return 0;
        var sign = to > from ? 1 : -1;
        var (lo, hi) = sign > 0 ? (from, to) : (to, from);
        var n = 0;
        for (var d = lo.AddDays(1); d <= hi; d = d.AddDays(1))
            if (IsWorkingDay(d)) n++;
        return sign * n;
    }
}
