using EngineeringPerformance.Application;

namespace EngineeringPerformance.UI.Tests;

/// <summary>In-memory stand-in for the file-backed ScoringPresetService, so bUnit tests don't touch disk.</summary>
public sealed class FakeScoringPresetService : IScoringPresetService
{
    private readonly List<ScoringPreset> _presets = [];

    public Task<IReadOnlyList<ScoringPreset>> GetPresetsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScoringPreset>>([.. _presets]);

    public Task SavePresetAsync(string name, OperationalScoringSettings settings, CancellationToken cancellationToken = default)
    {
        _presets.RemoveAll(p => p.Name == name);
        _presets.Add(new ScoringPreset(name, settings));
        return Task.CompletedTask;
    }

    public Task DeletePresetAsync(string name, CancellationToken cancellationToken = default)
    {
        _presets.RemoveAll(p => p.Name == name);
        return Task.CompletedTask;
    }
}
