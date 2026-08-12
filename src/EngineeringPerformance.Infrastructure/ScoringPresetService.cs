using System.Text.Json;
using EngineeringPerformance.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Persists named scoring-weight presets as a small JSON file alongside operational-scoring.json
/// — the same storage mechanism the live weights already use, rather than a new database table.
/// Two built-in presets ship as fallback defaults (not written to disk) so the picker is never
/// empty on a fresh install; saving a user preset under the same name as a built-in shadows it.
/// </summary>
public sealed class ScoringPresetService(string dataDirectory, ILogger<ScoringPresetService>? logger = null) : IScoringPresetService
{
    private readonly string _presetsPath = Path.Combine(dataDirectory, "scoring-presets.json");
    private readonly ILogger<ScoringPresetService> _logger = logger ?? NullLogger<ScoringPresetService>.Instance;

    public static readonly IReadOnlyList<ScoringPreset> BuiltInPresets =
    [
        new ScoringPreset("Individual Contributor", new OperationalScoringSettings(55m, 15m, 30m), IsBuiltIn: true),
        new ScoringPreset("Team Lead", new OperationalScoringSettings(35m, 35m, 30m), IsBuiltIn: true),
    ];

    public async Task<IReadOnlyList<ScoringPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
    {
        var saved = await ReadSavedAsync(cancellationToken);
        var savedNames = new HashSet<string>(saved.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        // Built-ins fill in only where the user hasn't saved a preset with the same name.
        var builtIns = BuiltInPresets.Where(x => !savedNames.Contains(x.Name));
        return builtIns.Concat(saved).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task SavePresetAsync(string name, OperationalScoringSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A preset needs a name.", nameof(name));
        if (!settings.IsValid) throw new InvalidOperationException("Preset weights must be non-negative and total exactly 100%.");

        var trimmed = name.Trim();
        var saved = (await ReadSavedAsync(cancellationToken)).ToList();
        var existingIndex = saved.FindIndex(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        var preset = new ScoringPreset(trimmed, settings);
        if (existingIndex >= 0) saved[existingIndex] = preset;
        else saved.Add(preset);
        await WriteSavedAsync(saved, cancellationToken);
    }

    public async Task DeletePresetAsync(string name, CancellationToken cancellationToken = default)
    {
        var saved = (await ReadSavedAsync(cancellationToken)).ToList();
        var removed = saved.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) await WriteSavedAsync(saved, cancellationToken);
    }

    private async Task<List<ScoringPreset>> ReadSavedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_presetsPath)) return [];
        try
        {
            await using var stream = File.OpenRead(_presetsPath);
            var presets = await JsonSerializer.DeserializeAsync<List<ScoringPreset>>(stream, cancellationToken: cancellationToken);
            return presets ?? [];
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Could not read {PresetsPath}; falling back to no saved presets (built-ins still available).", _presetsPath);
            return [];
        }
    }

    private async Task WriteSavedAsync(List<ScoringPreset> presets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_presetsPath)!);
        var temporary = _presetsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, presets, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        File.Move(temporary, _presetsPath, true);
    }
}
