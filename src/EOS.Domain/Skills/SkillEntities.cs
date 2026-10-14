namespace EOS.Domain.Skills;

public sealed class Skill
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// The CURRENT rating of a person on a skill. Ratings change only by explicit human action
/// (spec §9.5); every change is recorded as a <see cref="SkillTransaction"/>.
/// </summary>
public sealed class PersonSkill
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int SkillId { get; set; }
    public int Rating { get; set; }
    public DateOnly? AssessedOn { get; set; }
    public string? AssessedBy { get; set; }
}

public enum SkillTransactionKind
{
    /// <summary>A person set an initial or periodic rating (includes importing a human-made assessment sheet).</summary>
    Assessment = 0,
    /// <summary>A person changed a rating; a reason is mandatory.</summary>
    ManualAdjustment = 1,
    /// <summary>Evidence attached without changing the rating.</summary>
    EvidenceNote = 2,
}

public sealed class SkillTransaction
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int SkillId { get; set; }
    public SkillTransactionKind Kind { get; set; }
    public int? OldRating { get; set; }
    public int? NewRating { get; set; }
    public string? Reason { get; set; }
    public string? Source { get; set; }
    public string ChangedBy { get; set; } = "";
    public DateTime AtUtc { get; set; }
}
