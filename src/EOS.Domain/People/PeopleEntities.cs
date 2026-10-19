namespace EOS.Domain.People;

public sealed class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public int? LeaderPersonId { get; set; }
}

public sealed class Person
{
    public int Id { get; set; }

    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? RoleTitle { get; set; }
    public string? BaseLocation { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public decimal AvailabilityFraction { get; set; } = 1m;
    public bool IsActive { get; set; } = true;

    public bool IsUnverified { get; set; }

    public int? Seniority { get; set; }
    public string? Band { get; set; }
    public bool IsConsultant { get; set; }
    public bool IsOnProbation { get; set; }
    public bool IsNonBillable { get; set; }
}

public sealed class TimesheetEntry
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public int PersonId { get; set; }
    public DateOnly Date { get; set; }
    public string? ProjectNo { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? Activity { get; set; }
    public string? Milestone { get; set; }
    public string? Task { get; set; }
    public string? TaskType { get; set; }
    public string? Band { get; set; }
    public decimal Hours { get; set; }
}

public sealed class MonthlyPerformance
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal ComplianceHours { get; set; }
    public decimal EnteredHours { get; set; }
    public decimal ApprovedHours { get; set; }
    public decimal BillableHours { get; set; }
    public decimal NonBillableHours { get; set; }
    public decimal TrainingHours { get; set; }
    public decimal OfficeHours { get; set; }
    public decimal Utilization { get; set; }
    public int UniqueProjects { get; set; }
    public decimal AttendanceDays { get; set; }
    public decimal LeaveDays { get; set; }
    public decimal PunchHours { get; set; }
    public decimal TimesheetFilledDays { get; set; }
    public decimal ExpectedTimesheetDays { get; set; }
    public int MissingPunchDays { get; set; }
    public int LateDays { get; set; }
    public int EarlyDays { get; set; }
    public int LessDurationDays { get; set; }
    public decimal TimesheetCompletionScore { get; set; }
    public decimal ApprovalScore { get; set; }
    public decimal AttendanceDisciplineScore { get; set; }
    public decimal OperationalScore { get; set; }
}

public sealed class PeerReview
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int? ReviewerPersonId { get; set; }
    public string ReviewerName { get; set; } = "";
    public int SubjectPersonId { get; set; }
    public decimal Collaboration { get; set; }
    public decimal Communication { get; set; }
    public decimal Reliability { get; set; }
    public decimal TechnicalHelp { get; set; }
    public string? Comment { get; set; }
}

public enum AliasKind
{
    Initials = 0,
    ShortName = 1,
    AdUser = 2,
    Spelling = 3,
}

public sealed class PersonAlias
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
    public AliasKind Kind { get; set; }
    public string Source { get; set; } = "";
    public int? PersonId { get; set; }
}
