namespace EOS.Domain.Configuration;

public sealed class VocabularyItem
{
    public int Id { get; set; }

    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
    public int SortOrder { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class Vocabularies
{
    public const string TrackerStatus = "project.trackerStatus";
    public const string ErpStatus = "project.erpStatus";
    public const string ProjectType = "project.type";
    public const string ProductLine = "project.productLine";
    public const string Health = "project.health";
    public const string TaskStatus = "task.status";
    public const string AllocationStatus = "allocation.status";
    public const string DemandStatus = "demand.status";
    public const string OpportunityStage = "opportunity.stage";
    public const string OpportunityStatus = "opportunity.status";
    public const string ScenarioStatus = "scenario.status";
    public const string MovementStatus = "movement.status";
    public const string HostState = "host.state";
    public const string HostPurpose = "host.purpose";
    public const string LogKind = "log.kind";
    public const string GapType = "log.gapType";
    public const string LeaveKind = "leave.kind";
    public const string RiskKind = "risk.kind";
    public const string TicketStatus = "ticket.status";

    public static IReadOnlyDictionary<string, string[]> Seed { get; } = new Dictionary<string, string[]>
    {
        [TrackerStatus] = ["Entered", "Under Development", "Hold", "Deployed", "Completed"],
        [ErpStatus] = ["CLS", "RLS", "TECO", "FUTURE ORDER", "HLD"],
        [ProjectType] = ["Project and Project Services", "Service+Spare", "Mandays", "Runrate"],
        [ProductLine] = ["DOW", "MES", "Historian", "LIMS", "CMS", "PIP", "EMS", "AIMS", "BATCH"],
        [Health] = ["On Track", "Moderate Risk", "At Risk"],
        [TaskStatus] = ["Pending", "In Progress", "Completed", "Delayed"],
        [AllocationStatus] = ["Confirmed", "Proposed", "Released"],
        [DemandStatus] = ["Open", "Partially Filled", "Filled", "Gap - Escalated"],
        [OpportunityStage] = ["Qualified", "Proposal Sent", "Negotiation"],
        [OpportunityStatus] = ["Expected", "Won", "Lost"],
        [ScenarioStatus] = ["Draft", "Analysed", "Approved", "Rejected", "Applied"],
        [MovementStatus] = ["Requested", "Approved", "Rejected", "Executed", "Reverted"],
        [HostState] = ["Running", "Off", "Unknown", "No access"],
        [HostPurpose] = ["Publish", "Database", "Demo", "POC", "Development", "Test", "Production", "Unknown"],
        [LogKind] = ["Capacity Gap", "Change Request", "Decision"],
        [GapType] = ["Proficiency mismatch", "Capacity", "Timing"],
        [LeaveKind] = ["Leave", "Training", "Public holiday", "Other"],
        [RiskKind] = ["Risk", "Issue", "Dependency", "Assumption"],
        [TicketStatus] = ["Open", "Assigned", "Resolved", "Closed"],
    };
}

public sealed class WorkCalendarDef
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal HoursPerDay { get; set; } = 8m;

    public string WorkDays { get; set; } = "1,2,3,4,5,6";
    public bool IsDefault { get; set; }

    /// <summary>Hours for weekdays that differ from <see cref="HoursPerDay"/>, as "weekday=hours" pairs, for example "6=4.5" for a Saturday that ends at 1:30 pm.</summary>
    public string? DayHours { get; set; }
}

public sealed class CalendarException
{
    public int Id { get; set; }
    public int CalendarId { get; set; }
    public DateOnly Date { get; set; }

    public bool IsWorking { get; set; }
    public string? Note { get; set; }
}

public enum CustomFieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Choice = 3,
}

public sealed class CustomFieldDefinition
{
    public int Id { get; set; }

    public string EntityKind { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public CustomFieldType Type { get; set; }

    public string? Options { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CustomFieldValue
{
    public int Id { get; set; }
    public string EntityKind { get; set; } = "";
    public int EntityId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
