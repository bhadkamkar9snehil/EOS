using Bunit;
using EngineeringPerformance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.UI.Tests;

public sealed class PerformanceStoryTests : BunitContext
{
    [Fact]
    public async Task Momentum_matches_canonical_names_and_no_hours_approval_is_inapplicable()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var database = new FakeApplicationDatabase
        {
            MonthlyPerformance =
            [
                Performance("Dhruv Varachhiya", 2026, 7, 80m, enteredHours: 0m, approvalScore: 0m)
            ],
            History =
            [
                Performance("Dhruv  Varachhiya", 2026, 6, 70m, enteredHours: 0m, approvalScore: 0m)
            ]
        };
        var state = new AppState(database);
        await state.SetMonthAsync(new DateTime(2026, 7, 1));
        Services.AddSingleton<IApplicationDatabase>(database);
        Services.AddSingleton(state);

        var page = Render<PerformanceStory>();

        Assert.Contains("Dhruv Varachhiya · score 80.0 · ↑ 10", page.Markup);
        Assert.Contains("BELOW TARGET / FALLING", page.Markup);

        var heatmapRow = page.FindAll("button")
            .Single(button => button.TextContent.Contains("Dhruv Varachhiya", StringComparison.Ordinal));
        Assert.Equal("—", heatmapRow.Children[2].TextContent.Trim());
    }

    [Fact]
    public async Task Approval_remains_applicable_when_entered_hours_exist_without_compliance_hours()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var database = new FakeApplicationDatabase
        {
            MonthlyPerformance =
            [
                Performance("Asha Nair", 2026, 7, 82m, enteredHours: 160m, approvalScore: 75m, complianceHours: 0m)
            ],
            History =
            [
                Performance("Asha Nair", 2026, 6, 80m, enteredHours: 160m, approvalScore: 70m, complianceHours: 0m)
            ]
        };
        var state = new AppState(database);
        await state.SetMonthAsync(new DateTime(2026, 7, 1));
        Services.AddSingleton<IApplicationDatabase>(database);
        Services.AddSingleton(state);

        var page = Render<PerformanceStory>();

        var heatmapRow = page.FindAll("button")
            .Single(button => button.TextContent.Contains("Asha Nair", StringComparison.Ordinal));
        Assert.Equal("75", heatmapRow.Children[2].TextContent.Trim());
        Assert.Contains("Approval completion", page.Markup);
    }

    private static MonthlyPerformanceItem Performance(
        string name,
        int year,
        int month,
        decimal score,
        decimal enteredHours,
        decimal approvalScore,
        decimal complianceHours = 176m) => new(
            name, "E-001", score,
            90m, approvalScore, 90m,
            enteredHours, complianceHours, 0m, enteredHours,
            enteredHours > 0 ? 20 : 0, enteredHours > 0 ? 2 : 0, 20m, 0m,
            0, 0, 0, 0,
            year, month, 160m, enteredHours,
            enteredHours > 0 ? 20m : 0m, 22m, 0m, 0m,
            0m, 8m, enteredHours > 0 ? 80m : 0m);
}
