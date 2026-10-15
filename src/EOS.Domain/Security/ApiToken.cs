namespace EOS.Domain.Security;

public sealed class ApiToken
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
    public bool Revoked { get; set; }
}
