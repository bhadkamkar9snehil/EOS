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

/// <summary>
/// One line of a project's WBS. A task with children is a summary: its dates and progress are rolled up from the children.
/// Plans can be authored in EOS or imported, then extended (spec §16.7). <see cref="LockedFields"/> lists fields a user
/// edited so that a re-import never overwrites them.
/// </summary>
public sealed class PlanTask
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? ParentId { get; set; }

    /// <summary>Position in the whole plan (depth-first order). WBS codes are derived from parent + order.</summary>
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The tracker "Module" column: the natural level-1 grouping for imported plans (spec §16.2).</summary>
    public string? Module { get; set; }
    public string Kind { get; set; } = TaskKinds.Task;
    public decimal WorkHours { get; set; }

    /// <summary>True when <see cref="WorkHours"/> was derived from a duration (trackers carry no work); results are lower-confidence.</summary>
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

    /// <summary>Dates as imported/promised (tracker or ERP); kept for comparison with the computed schedule.</summary>
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedFinish { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualFinish { get; set; }

    // Cached results of the last schedule run.
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

    /// <summary>0 = FS, 1 = SS, 2 = FF, 3 = SF (matches <c>EOS.Scheduling.LinkType</c>).</summary>
    public int Type { get; set; }
    public int LagDays { get; set; }
}

/// <summary>A person (or a placeholder skill need) on a task. Active between <see cref="FromDate"/> and <see cref="ToDate"/>.</summary>
public sealed class TaskAssignment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int? PersonId { get; set; }

    /// <summary>1 = full time on the task.</summary>
    public decimal Units { get; set; } = 1m;
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>A placeholder: "needs SQL level 3" before a named engineer is chosen (spec §16.7).</summary>
    public int? PlaceholderSkillId { get; set; }
    public int? PlaceholderMinLevel { get; set; }
}

/// <summary>An immutable snapshot of a project's plan used to measure change (spec §14.8).</summary>
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
