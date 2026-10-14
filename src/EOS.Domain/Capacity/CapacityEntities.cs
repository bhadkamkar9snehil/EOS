namespace EOS.Domain.Capacity;

public sealed class Leave
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Kind { get; set; } = "Leave";
    public string? Note { get; set; }
}

/// <summary>One skill need of a project or opportunity: skill, minimum level, hours per week, window (spec §12.1).</summary>
public sealed class Demand
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public int? OpportunityId { get; set; }
    public int SkillId { get; set; }
    public int MinLevel { get; set; } = 3;
    public decimal HoursPerWeek { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Location { get; set; }

    /// <summary>Set to "Gap - Escalated" by a person; otherwise the fill status is computed.</summary>
    public string? StatusOverride { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Planned hours of a person on a project in a month. Proposed rows feed what-if without counting as committed (spec §13).</summary>
public sealed class Allocation
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int ProjectId { get; set; }
    public int? DemandId { get; set; }
    public string Role { get; set; } = "Engineer";
    public string Status { get; set; } = "Confirmed";

    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; set; }
    public decimal Hours { get; set; }
}

/// <summary>A stored what-if scenario: its moves, and its computed result for audit and comparison (spec §15).</summary>
public sealed class Scenario
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string Status { get; set; } = "Draft";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }

    /// <summary>JSON array of moves (person, from, to, date, percent).</summary>
    public string MovesJson { get; set; } = "[]";

    /// <summary>JSON of the computed impact per project.</summary>
    public string? ResultJson { get; set; }
    public string? DecisionBy { get; set; }
    public DateTime? DecisionUtc { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>The append-only ledger of where people's hours go (spec §17).</summary>
public sealed class ResourceMovement
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int FromProjectId { get; set; }
    public int ToProjectId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal Percent { get; set; } = 1m;
    public int? ReplacementPersonId { get; set; }
    public int? ScenarioId { get; set; }
    public string? Reason { get; set; }
    public string RequestedBy { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public DateOnly? ApprovedOn { get; set; }
    public string Status { get; set; } = "Requested";
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Change requests, capacity gaps and decisions with impact and approver (spec §17.4).</summary>
public sealed class ChangeGapEntry
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Capacity Gap";
    public int? ProjectId { get; set; }
    public int? DemandId { get; set; }
    public string RaisedBy { get; set; } = "";
    public DateOnly RaisedOn { get; set; }
    public string Description { get; set; } = "";
    public string? GapType { get; set; }
    public decimal ImpactHours { get; set; }
    public decimal ImpactDays { get; set; }
    public string? Decision { get; set; }
    public string? Approver { get; set; }
    public DateOnly? DecisionOn { get; set; }
    public string Status { get; set; } = "Open";
}
