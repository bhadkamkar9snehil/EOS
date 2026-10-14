namespace EOS.Infrastructure.Capacity;

public sealed class ScenarioException(string message) : Exception(message);

public sealed record MoveInput(int PersonId, int FromProjectId, int ToProjectId, DateOnly Date, decimal Percent = 1m);

public sealed record MoveView(
    int PersonId, string PersonName, int FromProjectId, string FromProjectNo, int ToProjectId, string ToProjectNo,
    DateOnly Date, decimal Percent);

public sealed record TaskSlip(string Name, DateOnly? BaselineFinish, DateOnly? ScenarioFinish, int SlipDays);

/// <param name="SlipDays">Working days: positive = later than baseline, negative = earlier (spec §15.6).</param>
public sealed record ProjectImpactView(
    int ProjectId, string ProjectNo, string Name, DateOnly BaselineFinish, DateOnly ScenarioFinish, int SlipDays,
    IReadOnlyList<TaskSlip> Tasks);

public sealed record ScenarioView(
    int Id, string Name, string Status, string? Notes, IReadOnlyList<MoveView> Moves,
    IReadOnlyList<ProjectImpactView> Projects, IReadOnlyList<string> Warnings, IReadOnlyList<string> Verdict,
    bool RequiresLeadershipApproval, string CreatedBy, DateTime CreatedUtc, string? DecisionBy, string? DecisionNote);

public sealed record ReleaseRowView(
    int PersonId, string PersonName, int ProjectId, string ProjectNo, DateOnly LastDayOnCurrentWork,
    DateOnly? EarliestNoDelayMove, int CostIfMovedOnAnalysisDate, int GainToTargetIfMovedOnAnalysisDate,
    bool Eligible, string? EligibilityNote, bool TooLateToHelp);

public sealed record PushStep(int Step, string PersonName, string FromProjectNo, DateOnly Date, DateOnly TargetFinish, int TargetSlipDays);

public sealed record PushPlanView(
    int TargetProjectId, string TargetProjectNo, DateOnly BaselineFinish, IReadOnlyList<PushStep> Steps, IReadOnlyList<MoveInput> Moves);
