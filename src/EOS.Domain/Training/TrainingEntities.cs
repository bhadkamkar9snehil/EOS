namespace EOS.Domain.Training;

public sealed class Module
{
    public int Id { get; set; }
    public string Product { get; set; } = "";
    public string Name { get; set; } = "";
    public string SubModule { get; set; } = "";
    public int SortOrder { get; set; }
}

public enum KnowledgeState
{
    DontKnow = 0,
    Know = 1,
    NeedToDiscuss = 2,
}

public sealed class PersonModule
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int ModuleId { get; set; }
    public KnowledgeState State { get; set; }

    public string? Proficiency { get; set; }
}

public enum DoubtResult
{
    Open = 0,
    Assigned = 1,
    Hold = 2,
    Solved = 3,
}

public sealed class Doubt
{
    public int Id { get; set; }
    public string Module { get; set; } = "";
    public string SubModule { get; set; } = "";
    public string Text { get; set; } = "";
    public DateOnly? RaisedOn { get; set; }
    public int? RaisedByPersonId { get; set; }
    public string? RaisedByName { get; set; }
    public DateOnly? AssignedOn { get; set; }
    public int? AssignedToPersonId { get; set; }
    public string? AssignedToName { get; set; }
    public DateOnly? SolvedOn { get; set; }
    public DoubtResult Result { get; set; }
    public string? Remarks { get; set; }
    public int SourceRow { get; set; }
}
