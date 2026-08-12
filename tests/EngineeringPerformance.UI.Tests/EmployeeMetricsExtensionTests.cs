using Bunit;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.UI.Tests;

public sealed class EmployeeMetricsExtensionTests : BunitContext
{
    private FakeApplicationDatabase _database = null!;

    private IRenderedComponent<EmployeeMetricsExtension> RenderFor(string name)
    {
        _database = new FakeApplicationDatabase { History = HistoryFor(name) };
        Services.AddSingleton<IApplicationDatabase>(_database);
        var state = new AppState(_database);
        Services.AddSingleton(state);
        JSInterop.Mode = JSRuntimeMode.Loose; // component fires several chart JS calls we don't assert on here
        state.GetType(); // ensure AppState constructed before render
        // Populate AppState.History via a refresh so the component's `State.History` has rows.
        state.RefreshAsync().GetAwaiter().GetResult();
        return Render<EmployeeMetricsExtension>(p => p.Add(c => c.Name, name));
    }

    private static IReadOnlyList<MonthlyPerformanceItem> HistoryFor(string name) =>
    [
        new MonthlyPerformanceItem(name, "E1001", 88m, 90m, 85m, 80m, 150m, 160m, 120m, 40m,
            10, 2, 20, 1, 0, 1, 0, 0, 2026, 6, 140m, 138m, 20, 20, 20m, 10m, ApprovedHours: 155m),
        new MonthlyPerformanceItem(name, "E1001", 92m, 91m, 93m, 88m, 155m, 160m, 125m, 42m,
            11, 2, 21, 0, 0, 0, 0, 0, 2026, 7, 145m, 143m, 21, 21, 18m, 9m, ApprovedHours: 158m)
    ];

    [Fact]
    public void RendersNothingWhenNoHistoryIsLoadedForTheEmployee()
    {
        var database = new FakeApplicationDatabase { History = [] };
        Services.AddSingleton<IApplicationDatabase>(database);
        Services.AddSingleton(new AppState(database));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<EmployeeMetricsExtension>(p => p.Add(c => c.Name, "Someone With No Data"));

        Assert.Equal(string.Empty, component.Markup.Trim());
    }

    [Fact]
    public void RendersLatestOperatingProfileWhenHistoryIsPresent()
    {
        var component = RenderFor("Priyanka Makwana");

        Assert.Contains("Current operating profile", component.Markup);
        Assert.Contains("Operational score", component.Markup);
        // The latest (July) row's operational score, not the June one, should be displayed.
        Assert.Contains("92", component.Markup);
    }

    [Fact]
    public void ShowsExpandedHistorySectionCoveringAllLoadedMonths()
    {
        var component = RenderFor("Rohit Sharma");

        Assert.Contains("Expanded operational history", component.Markup);
        Assert.Contains("2 loaded month", component.Markup);
    }
}
