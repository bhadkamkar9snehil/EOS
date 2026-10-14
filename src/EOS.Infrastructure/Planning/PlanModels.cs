namespace EOS.Infrastructure.Planning;

public sealed class PlanException(string message) : Exception(message);

public sealed record AssignmentView(
    int Id, int? PersonId, string? PersonName, decimal Units, DateOnly? From, DateOnly? To,
    string? PlaceholderSkill, int? PlaceholderMinLevel);

public sealed record PlanTaskView(
    int Id, int? ParentId, string Wbs, int Level, string Name, string? Module, string Kind, bool IsSummary,
    decimal WorkHours, bool WorkIsEstimated, int? FixedDurationDays, DateOnly? StartNoEarlierThan,
    decimal PercentComplete, string Status, string? Priority, string? Notes,
    DateOnly? Start, DateOnly? Finish, int? TotalFloatDays, bool IsCritical,
    DateOnly? PlannedStart, DateOnly? PlannedFinish, DateOnly? ActualStart, DateOnly? ActualFinish,
    string Origin, string? ExternalKey, IReadOnlyList<AssignmentView> Assignments);

public sealed record LinkView(int Id, int PredecessorId, int SuccessorId, int Type, int LagDays);

public sealed record PlanView(
    int ProjectId, string ProjectNo, string ProjectName, DateOnly? Start, DateOnly? Finish, decimal Progress,
    IReadOnlyList<PlanTaskView> Tasks, IReadOnlyList<LinkView> Links, IReadOnlyList<string> Warnings);

/// <summary>A partial update of a task. Null leaves a field unchanged; the Clear flags remove an optional value.</summary>
public sealed record TaskPatch
{
    public string? Name { get; init; }
    public string? Module { get; init; }
    public string? Kind { get; init; }
    public decimal? WorkHours { get; init; }
    public int? FixedDurationDays { get; init; }
    public bool ClearFixedDuration { get; init; }
    public DateOnly? StartNoEarlierThan { get; init; }
    public bool ClearStartNoEarlierThan { get; init; }
    public decimal? PercentComplete { get; init; }
    public string? Status { get; init; }
    public string? Priority { get; init; }
    public string? Notes { get; init; }
    public DateOnly? ActualStart { get; init; }
    public DateOnly? ActualFinish { get; init; }
}

public enum MoveDirection
{
    Up,
    Down,
    Indent,
    Outdent,
}

public sealed record BaselineVarianceRow(
    int TaskId, string Wbs, string Name, DateOnly? BaselineStart, DateOnly? BaselineFinish,
    DateOnly? CurrentStart, DateOnly? CurrentFinish, int? FinishVarianceDays, decimal WorkVarianceHours);

public sealed record BaselineVariance(
    int ProjectId, int BaselineNumber, string BaselineName, DateOnly? BaselineFinish, DateOnly? CurrentFinish,
    int? FinishVarianceDays, IReadOnlyList<BaselineVarianceRow> Rows);
