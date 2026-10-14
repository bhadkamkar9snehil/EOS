using EOS.Domain.Audit;
using EOS.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Persistence;

/// <summary>
/// Persists effective-dated settings. A change never edits history: it closes the previous open entry for the same
/// key and scope the day before and adds a new one (spec §8.7). Every change is audited with its reason.
/// </summary>
public sealed class SettingsStore(EosDbContext db)
{
    public async Task<SettingsResolver> LoadResolverAsync(CancellationToken ct = default)
    {
        var rows = await db.Settings.AsNoTracking().ToListAsync(ct);
        return new SettingsResolver(rows.Select(ToEntry));
    }

    public async Task SetAsync(
        string key, SettingScope scope, string jsonValue, DateOnly effectiveFrom, string actor, string reason,
        CancellationToken ct = default)
    {
        if (!SettingDefaults.All.ContainsKey(key))
            throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));
        try { System.Text.Json.JsonDocument.Parse(jsonValue).Dispose(); }
        catch (System.Text.Json.JsonException) { throw new ArgumentException("Value must be valid JSON.", nameof(jsonValue)); }
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required for a settings change.", nameof(reason));

        var scopeId = scope.Id ?? "";
        var open = await db.Settings
            .Where(r => r.Key == key && r.ScopeKind == (int)scope.Kind && r.ScopeId == scopeId && r.EffectiveTo == null)
            .ToListAsync(ct);
        foreach (var r in open)
        {
            if (r.EffectiveFrom >= effectiveFrom) db.Settings.Remove(r);          // superseded before it ever applied
            else r.EffectiveTo = effectiveFrom.AddDays(-1);
        }

        db.Settings.Add(new SettingRow
        {
            Key = key, ScopeKind = (int)scope.Kind, ScopeId = scopeId, JsonValue = jsonValue,
            EffectiveFrom = effectiveFrom, ChangedBy = actor, Reason = reason,
        });
        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow, Actor = actor, Action = "setting.set", SubjectKind = "setting",
            SubjectId = $"{key}@{scope.Kind}:{scopeId}", Detail = $"{jsonValue} from {effectiveFrom:yyyy-MM-dd}: {reason}",
        });
        await db.SaveChangesAsync(ct);
    }

    private static SettingEntry ToEntry(SettingRow r) => new(
        r.Key, new SettingScope((SettingScopeKind)r.ScopeKind, r.ScopeId.Length == 0 ? null : r.ScopeId),
        r.JsonValue, r.EffectiveFrom, r.EffectiveTo, r.ChangedBy, r.Reason);
}
