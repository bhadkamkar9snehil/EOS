using Bunit;
using EngineeringPerformance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.UI.Tests;

public sealed class OverviewResponsiveTests : BunitContext
{
    [Fact]
    public async Task PulseStripProvidesExpandedCompactAndSummaryRepresentations()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var database = new FakeApplicationDatabase
        {
            MonthlyPerformance =
            [
                Performance("Critical", 42m),
                Performance("Serious", 58m),
                Performance("Warning one", 68m),
                Performance("Warning two", 74m),
                Performance("Good one", 84m),
                Performance("Good two", 89m),
                Performance("Good three", 93m),
                Performance("Good four", 97m)
            ]
        };
        database.History = database.MonthlyPerformance;
        var state = new AppState(database);
        await state.RefreshAsync();
        Services.AddSingleton<IApplicationDatabase>(database);
        Services.AddSingleton(state);

        var page = Render<Overview>();

        Assert.Single(page.FindAll(".adaptive-strip-shell"));
        Assert.Equal(5, page.FindAll(".adaptive-module").Count);
        Assert.Single(page.FindAll(".distribution-detail"));
        Assert.Single(page.FindAll(".distribution-summary"));
        Assert.Equal(4, page.FindAll(".distribution-segment").Count);
        Assert.Equal(3, page.FindAll(".trend-card").Count);
        Assert.Single(page.FindAll(".utilization-meter"));
        Assert.Single(page.FindAll(".readiness-detail"));
        Assert.Single(page.FindAll(".readiness-summary"));
        Assert.Single(page.FindAll(".alert-meter"));
    }

    [Fact]
    public async Task ScoreRangesAndPrimaryValuesRemainAtomicAndAccessible()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var database = new FakeApplicationDatabase
        {
            MonthlyPerformance = [Performance("Engineer", 88m)]
        };
        var state = new AppState(database);
        await state.RefreshAsync();
        Services.AddSingleton<IApplicationDatabase>(database);
        Services.AddSingleton(state);

        var page = Render<Overview>();

        Assert.All(page.FindAll(".distribution-range"), range => Assert.DoesNotContain("\n", range.TextContent));
        Assert.Contains("0–49", page.Markup);
        Assert.Contains("80–100", page.Markup);
        Assert.Contains("open alerts", page.Find(".alert-meter").GetAttribute("aria-label"));
        Assert.Contains("evidence sources ready", page.Find(".readiness-summary").GetAttribute("aria-label"));
    }

    private static MonthlyPerformanceItem Performance(string name, decimal score) => new(
        name, $"E-{score:0}", score,
        score, score, score,
        150m, 176m, 120m, 148m,
        40, 3, 20m, 1m,
        score < 50 ? 4 : 0, score < 65 ? 3 : 0, 0, 0,
        2026, 7, 160m, 158m,
        20m, 22m, 5m, 2m,
        145m, 8m, score);
}
