namespace EOS.Domain.Projects;

public sealed class Project
{
    public int Id { get; set; }

    public string ProjectNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Customer { get; set; }
    public string? ProductLine { get; set; }
    public string? ProjectType { get; set; }
    public string? ErpStatus { get; set; }
    public string? TrackerStatus { get; set; }

    public string? ProjectManager { get; set; }
    public string? TechnicalManager { get; set; }
    public int? TechnicalManagerPersonId { get; set; }
    public decimal? OrderValue { get; set; }
    public decimal? MarginPercent { get; set; }
    public DateOnly? PoStart { get; set; }
    public DateOnly? PoEnd { get; set; }
    public DateOnly? ContractualEnd { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public string? SiteLocation { get; set; }
    public int? ParentProjectId { get; set; }
    public int? CalendarId { get; set; }

    public string? HealthOverride { get; set; }
    public string? HealthOverrideReason { get; set; }
    public decimal? ProgressOverride { get; set; }
    public DateOnly? LastStatusUpdate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsSample { get; set; }
}

public sealed class Opportunity
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? LeadName { get; set; }
    public decimal? ExpectedValueLakh { get; set; }
    public decimal? ExpectedHours { get; set; }
    public DateOnly? LikelyBookingMonth { get; set; }
    public int? DurationMonths { get; set; }
    public string? Stage { get; set; }
    public decimal WinProbability { get; set; }
    public string Status { get; set; } = "Expected";

    public string? RequiredSkills { get; set; }
    public int? ConvertedProjectId { get; set; }
    public bool IsSample { get; set; }
}

public sealed class RiskIssue
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Kind { get; set; } = "Risk";
    public string Text { get; set; } = "";
    public string? Severity { get; set; }
    public string? Owner { get; set; }
    public DateOnly? DueOn { get; set; }
    public string Status { get; set; } = "Open";
    public string? Mitigation { get; set; }
}

public sealed class AmcTicket
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectNo { get; set; }
    public string? Product { get; set; }
    public bool InAmc { get; set; }
    public DateOnly? RaisedOn { get; set; }
    public string? RaisedBy { get; set; }
    public string Issue { get; set; } = "";
    public DateOnly? AssignedOn { get; set; }
    public string? AssignedBy { get; set; }
    public DateOnly? ResolvedOn { get; set; }
    public string? AssignedEngineers { get; set; }
    public string? RootCause { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "Open";
}
