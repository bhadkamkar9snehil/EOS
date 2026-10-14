namespace EOS.Domain.Security;

/// <summary>
/// A personal access token for the API, CLI, MCP and the web UI sign-in (spec §23.2). Only a hash is stored; the token itself
/// is shown once when created. The token's name is the actor recorded on every change.
/// </summary>
public sealed class ApiToken
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
    public bool Revoked { get; set; }
}
