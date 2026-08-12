namespace EngineeringPerformance.Domain;

public enum ObligationOutcome
{
    Pending = 0,
    OnTime = 1,
    Late = 2,
    Overdue = 3,
    Excused = 4,
    NotApplicable = 5,
    Waived = 6
}

public sealed record ObligationEvaluation(
    ObligationOutcome Outcome,
    DateTime DueAt,
    DateTime? CompletedAt,
    int? DelayMinutes)
{
    public int? MinutesRelativeToDeadline => CompletedAt is null
        ? null
        : (int)Math.Round((CompletedAt.Value - DueAt).TotalMinutes);
}

public static class ObligationEvaluator
{
    public static ObligationEvaluation Evaluate(
        DateTime dueAt,
        DateTime? completedAt,
        DateTime asOf,
        int graceMinutes = 0,
        ObligationOutcome? exceptionOutcome = null)
    {
        if (graceMinutes < 0) throw new ArgumentOutOfRangeException(nameof(graceMinutes));
        if (exceptionOutcome is not null && exceptionOutcome is not (ObligationOutcome.Excused or ObligationOutcome.NotApplicable or ObligationOutcome.Waived))
            throw new ArgumentOutOfRangeException(nameof(exceptionOutcome), "An exception can only produce Excused, Not Applicable, or Waived.");

        if (exceptionOutcome is { } exception)
            return new ObligationEvaluation(exception, dueAt, completedAt, null);

        if (completedAt is { } completed)
        {
            var delay = Math.Max(0, (int)Math.Ceiling((completed - dueAt).TotalMinutes));
            return new ObligationEvaluation(
                completed <= dueAt.AddMinutes(graceMinutes) ? ObligationOutcome.OnTime : ObligationOutcome.Late,
                dueAt,
                completed,
                delay);
        }

        return new ObligationEvaluation(
            asOf <= dueAt.AddMinutes(graceMinutes) ? ObligationOutcome.Pending : ObligationOutcome.Overdue,
            dueAt,
            null,
            null);
    }

    public static DateTime NextWorkingDayDeadline(
        DateTime workDate,
        int hour,
        int minute,
        bool saturdayIsWorkingDay = true)
    {
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(minute));

        var dueDate = workDate.Date.AddDays(1);
        while (dueDate.DayOfWeek == DayOfWeek.Sunday ||
               (!saturdayIsWorkingDay && dueDate.DayOfWeek == DayOfWeek.Saturday))
            dueDate = dueDate.AddDays(1);

        return dueDate.AddHours(hour).AddMinutes(minute);
    }
}
