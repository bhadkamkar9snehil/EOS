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
    Assessment = 0,
    ManualAdjustment = 1,
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
