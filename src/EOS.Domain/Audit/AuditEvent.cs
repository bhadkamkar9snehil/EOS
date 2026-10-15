namespace EOS.Domain.Audit;

public sealed class AuditEvent
{
    public int Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string SubjectKind { get; set; } = "";
    public string? SubjectId { get; set; }
    public string? Detail { get; set; }
}
