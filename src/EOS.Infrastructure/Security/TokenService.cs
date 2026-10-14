using System.Security.Cryptography;
using System.Text;
using EOS.Domain.Audit;
using EOS.Domain.Security;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Security;

/// <summary>Creates and validates personal access tokens. Tokens are random 256-bit values; only their SHA-256 hash is stored.</summary>
public sealed class TokenService(EosDbContext db)
{
    public const string Prefix = "eos_";

    /// <summary>Creates a token and returns it ONCE. The caller must show it to the user immediately.</summary>
    public async Task<string> CreateAsync(string name, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A token needs a name (it is recorded as the actor on every change).", nameof(name));
        name = name.Trim();
        if (await db.ApiTokens.AnyAsync(t => t.Name == name && !t.Revoked, ct)) throw new ArgumentException($"An active token named '{name}' already exists.", nameof(name));

        var token = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.ApiTokens.Add(new ApiToken { Name = name, Hash = Hash(token), CreatedUtc = DateTime.UtcNow });
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "token.create", SubjectKind = "token", SubjectId = name });
        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>Returns the token's name when valid and not revoked; otherwise null.</summary>
    public async Task<string?> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var hash = Hash(token);
        var row = await db.ApiTokens.SingleOrDefaultAsync(t => t.Hash == hash && !t.Revoked, ct);
        if (row is null) return null;
        if (row.LastUsedUtc is null || (DateTime.UtcNow - row.LastUsedUtc.Value).TotalMinutes > 5)
        {
            row.LastUsedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return row.Name;
    }

    public async Task RevokeAsync(int id, string actor, CancellationToken ct = default)
    {
        var row = await db.ApiTokens.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new ArgumentException($"Token {id} not found.");
        row.Revoked = true;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "token.revoke", SubjectKind = "token", SubjectId = row.Name });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(int Id, string Name, DateTime CreatedUtc, DateTime? LastUsedUtc, bool Revoked)>> ListAsync(CancellationToken ct = default) =>
        (await db.ApiTokens.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct)).Select(t => (t.Id, t.Name, t.CreatedUtc, t.LastUsedUtc, t.Revoked)).ToList();

    public async Task<bool> AnyActiveAsync(CancellationToken ct = default) => await db.ApiTokens.AnyAsync(t => !t.Revoked, ct);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
