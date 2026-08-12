using Bunit;
using EngineeringPerformance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.UI.Tests;

public sealed class ScoringPageTests : BunitContext
{
    private FakeApplicationDatabase _database = new();

    private IRenderedComponent<ScoringPage> RenderPage()
    {
        _database = new FakeApplicationDatabase();
        Services.AddSingleton<IApplicationDatabase>(_database);
        Services.AddSingleton(new AppState(_database));
        return Render<ScoringPage>();
    }

    [Fact]
    public void RendersWithoutDataAndLoadsSavedWeights()
    {
        _database = new FakeApplicationDatabase { ScoringSettings = new OperationalScoringSettings(60m, 10m, 30m) };
        Services.AddSingleton<IApplicationDatabase>(_database);
        Services.AddSingleton(new AppState(_database));

        var page = Render<ScoringPage>();

        Assert.Contains("60", page.Markup);
        Assert.Contains("Ready to apply", page.Markup);
    }

    [Fact]
    public void ShowsUnderTotalValidationWhenWeightsDoNotSumTo100()
    {
        var page = RenderPage();

        // Default weights (55/15/30) sum to 100; drop the timesheet weight so the total is under.
        var timesheetInput = page.FindAll("input[type=number]")[0];
        timesheetInput.Change("0");

        Assert.Contains("Add", page.Markup);
        var saveButton = page.Find("button.action:not(.secondary)");
        Assert.True(saveButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task SaveAndRecalculateAsyncPersistsValidWeightsAndShowsConfirmation()
    {
        var page = RenderPage();

        await page.InvokeAsync(async () =>
        {
            var instance = page.Instance;
            var method = typeof(ScoringPage).GetMethod("SaveAndRecalculateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)method.Invoke(instance, null)!;
        });

        Assert.Single(_database.SavedScoringCalls);
        Assert.Equal(100m, _database.SavedScoringCalls[0].Total);
    }

    [Fact]
    public async Task SaveAndRecalculateAsyncShowsErrorStateWhenDatabaseThrows()
    {
        var page = RenderPage();
        _database.SaveScoringSettingsThrows = new InvalidOperationException("disk is full");

        var state = Services.GetRequiredService<AppState>();
        await page.InvokeAsync(async () =>
        {
            var method = typeof(ScoringPage).GetMethod("SaveAndRecalculateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)method.Invoke(page.Instance, null)!;
        });

        Assert.True(state.IsError);
        Assert.Contains("disk is full", state.Message);
    }

    [Fact]
    public void ResetOperationalRestoresDefaultWeights()
    {
        var page = RenderPage();
        page.FindAll("input[type=number]")[0].Change("0");
        Assert.Contains("Add", page.Markup);

        page.Find("button.action.secondary").Click();

        Assert.Contains("Ready to apply", page.Markup);
    }
}
