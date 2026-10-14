namespace EOS.Domain.People;

public sealed class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The team leader (a person). Team leaders are the only non-owner users later (spec §23).</summary>
    public int? LeaderPersonId { get; set; }
}

public sealed class Person
{
    public int Id { get; set; }

    /// <summary>Employee code, e.g. "E001". Natural key for imports.</summary>
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? RoleTitle { get; set; }
    public string? BaseLocation { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>Fraction of a full-time engineer (1 = 100%). Seeded from imports, then user-maintained.</summary>
    public decimal AvailabilityFraction { get; set; } = 1m;
    public bool IsActive { get; set; } = true;

    /// <summary>True while role/location/availability are placeholder values from a sample sheet (spec §6.1, §24.1).</summary>
    public bool IsUnverified { get; set; }
}

public enum AliasKind
{
    Initials = 0,
    ShortName = 1,
    AdUser = 2,
    Spelling = 3,
}

/// <summary>
/// Another way a person is written in a source (initials in trackers, first names in the training sheet, AD usernames).
/// <see cref="PersonId"/> is null while unresolved; unresolved aliases never block an import (spec §6.4).
/// </summary>
public sealed class PersonAlias
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
    public AliasKind Kind { get; set; }
    public string Source { get; set; } = "";
    public int? PersonId { get; set; }
}
