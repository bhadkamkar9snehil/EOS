namespace EOS.Domain.Planning;

public static class TaskKinds
{
    public const string Task = "task";
    public const string Milestone = "milestone";
}

public static class TaskOrigins
{
    public const string Eos = "eos";
    public const string Erp = "erp";
    public const string Tracker = "tracker";
    public const string MsProject = "msproject";
}

public sealed class PlanTask
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? ParentId { get; set; }

    public int SortOrder { get; set; }
    public string Name { get; set; } = "";

    public string? Module { get; set; }
    public string Kind { get; set; } = TaskKinds.Task;
    public decimal WorkHours { get; set; }

    /// <summary>Hours booked against this task in the ERP timesheets (from the ERP task list). Null when unknown.</summary>
    public decimal? ActualHours { get; set; }

    public bool WorkIsEstimated { get; set; }
    public int? FixedDurationDays { get; set; }
    public DateOnly? StartNoEarlierThan { get; set; }
    public decimal PercentComplete { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Priority { get; set; }
    public string? Notes { get; set; }

    public string Origin { get; set; } = TaskOrigins.Eos;
    public string? ExternalKey { get; set; }
    public string? LockedFields { get; set; }

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedFinish { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualFinish { get; set; }

    public int? RequiredSkillId { get; set; }
    public int? RequiredLevel { get; set; }

    public DateOnly? ScheduledStart { get; set; }
    public DateOnly? ScheduledFinish { get; set; }
    public int? TotalFloatDays { get; set; }
    public bool IsCritical { get; set; }
}

public sealed class TaskLink
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int PredecessorId { get; set; }
    public int SuccessorId { get; set; }

    public int Type { get; set; }
    public int LagDays { get; set; }
}

public sealed class TaskAssignment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int? PersonId { get; set; }

    public decimal Units { get; set; } = 1m;
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int? PlaceholderSkillId { get; set; }
    public int? PlaceholderMinLevel { get; set; }
}

public sealed class Baseline
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class BaselineTask
{
    public int Id { get; set; }
    public int BaselineId { get; set; }
    public int TaskId { get; set; }
    public string WbsCode { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly? Start { get; set; }
    public DateOnly? Finish { get; set; }
    public decimal WorkHours { get; set; }
    public decimal PercentComplete { get; set; }
}
