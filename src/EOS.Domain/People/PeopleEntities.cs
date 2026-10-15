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
