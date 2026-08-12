using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class ScoringPresetServiceTests
{
    private static (ScoringPresetService Service, string DataDirectory) CreateSut()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"epa-presets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        return (new ScoringPresetService(dataDirectory), dataDirectory);
    }

    [Fact]
    public async Task GetPresetsAsync_ReturnsBuiltInsOnFreshInstall()
    {
        var (service, dataDirectory) = CreateSut();
        try
        {
            var presets = await service.GetPresetsAsync();
            Assert.Contains(presets, x => x.Name == "Individual Contributor" && x.IsBuiltIn);
            Assert.Contains(presets, x => x.Name == "Team Lead" && x.IsBuiltIn);
        }
        finally { Directory.Delete(dataDirectory, true); }
    }

    [Fact]
    public async Task SavePresetAsync_PersistsAndIsReturnedByGetPresets()
    {
        var (service, dataDirectory) = CreateSut();
        try
        {
            await service.SavePresetAsync("Senior Engineer", new OperationalScoringSettings(60m, 20m, 20m));
            var presets = await service.GetPresetsAsync();
            var saved = Assert.Single(presets, x => x.Name == "Senior Engineer");
            Assert.False(saved.IsBuiltIn);
            Assert.Equal(60m, saved.Settings.TimesheetCompletionWeight);
        }
        finally { Directory.Delete(dataDirectory, true); }
    }

    [Fact]
    public async Task SavePresetAsync_WithSameNameTwice_UpdatesInPlaceRatherThanDuplicating()
    {
        var (service, dataDirectory) = CreateSut();
        try
        {
            await service.SavePresetAsync("Custom", new OperationalScoringSettings(50m, 20m, 30m));
            await service.SavePresetAsync("custom", new OperationalScoringSettings(40m, 30m, 30m));
            var presets = await service.GetPresetsAsync();
            var matches = presets.Where(x => string.Equals(x.Name, "custom", StringComparison.OrdinalIgnoreCase)).ToArray();
            var only = Assert.Single(matches);
            Assert.Equal(40m, only.Settings.TimesheetCompletionWeight);
        }
        finally { Directory.Delete(dataDirectory, true); }
    }

    [Fact]
    public async Task SavePresetAsync_RejectsWeightsThatDoNotTotal100()
    {
        var (service, dataDirectory) = CreateSut();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SavePresetAsync("Bad", new OperationalScoringSettings(50m, 20m, 20m)));
        }
        finally { Directory.Delete(dataDirectory, true); }
    }

    [Fact]
    public async Task DeletePresetAsync_RemovesASavedPreset_ButBuiltInsReappearAfterward()
    {
        var (service, dataDirectory) = CreateSut();
        try
        {
            await service.SavePresetAsync("Individual Contributor", new OperationalScoringSettings(70m, 10m, 20m));
            var overridden = await service.GetPresetsAsync();
            Assert.Contains(overridden, x => x.Name == "Individual Contributor" && !x.IsBuiltIn);

            await service.DeletePresetAsync("Individual Contributor");
            var afterDelete = await service.GetPresetsAsync();
            Assert.Contains(afterDelete, x => x.Name == "Individual Contributor" && x.IsBuiltIn);
        }
        finally { Directory.Delete(dataDirectory, true); }
    }
}
