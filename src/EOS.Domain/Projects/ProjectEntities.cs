namespace EOS.Domain.Projects;

/// <summary>A unit of customer delivery. The ERP project master supplies identity and commercials; plans add delivery detail (spec §11).</summary>
public sealed class Project
{
    public int Id { get; set; }

    /// <summary>ERP project number, e.g. "PRO2503040". Natural key for imports.</summary>
    public string ProjectNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Customer { get; set; }
    public string? ProductLine { get; set; }
    public string? ProjectType { get; set; }
    public string? ErpStatus { get; set; }
    public string? TrackerStatus { get; set; }

    /// <summary>Project manager (commercial/delivery). Text, because most PMs are not engineers in the roster.</summary>
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

    /// <summary>Optional manual health, with reason; otherwise health is computed (spec §11.6).</summary>
    public string? HealthOverride { get; set; }
    public string? HealthOverrideReason { get; set; }
    public decimal? ProgressOverride { get; set; }
    public DateOnly? LastStatusUpdate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>True for placeholder rows from sample sheets (spec §24.1 sample space).</summary>
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

    /// <summary>Required skill codes, comma separated.</summary>
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
