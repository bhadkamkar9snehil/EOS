using EngineeringPerformance.Application;

namespace EngineeringPerformance.UI.Tests;

public sealed class AppStateIdentityTests
{
    [Fact]
    public async Task RefreshCanonicalizesWhitespaceInCurrentAndHistoricalEmployeeNames()
    {
        var database = new FakeApplicationDatabase
        {
            MonthlyPerformance = [Performance("Dhruv  Varachhiya", 82m)],
            History = [Performance("  Dhruv   Varachhiya  ", 76m)]
        };
        var state = new AppState(database);

        await state.RefreshAsync();

        Assert.Equal("Dhruv Varachhiya", Assert.Single(state.Performance).EmployeeName);
        Assert.Equal("Dhruv Varachhiya", Assert.Single(state.History).EmployeeName);
    }

    [Fact]
    public async Task RefreshConsolidatesCanonicalDuplicatesAndPreservesSourceSpecificEvidence()
    {
        var database = new FakeApplicationDatabase
        {
            MonthlyPerformance =
            [
                new MonthlyPerformanceItem(
                    "Dhruv Varachhiya", "E-001", 0m,
                    80m, 90m, 0m,
                    150m, 176m, 120m, 0m,
                    0, 0, 0m, 0m,
                    0, 0, 0, 0,
                    2026, 7, 0m, 0m,
                    0m, 0m, 5m, 2m,
                    135m, 8m, 70m),
                new MonthlyPerformanceItem(
                    "  DHRUV   VARACHHIYA ", "E-001", 0m,
                    0m, 0m, 70m,
                    0m, 0m, 0m, 148m,
                    40, 3, 20m, 1m,
                    1, 2, 1, 1,
                    2026, 7, 160m, 158m,
                    20m, 22m, 0m, 0m)
            ]
        };
        var state = new AppState(database);

        await state.RefreshAsync();

        var merged = Assert.Single(state.Performance);
        Assert.Equal("Dhruv Varachhiya", merged.EmployeeName);
        Assert.Equal(176m, merged.ComplianceHours);
        Assert.Equal(22m, merged.ExpectedTimesheetDays);
        Assert.Equal(40, merged.DetailedEntries);
        Assert.Equal(78.5m, merged.OperationalScore);
    }

    private static MonthlyPerformanceItem Performance(string name, decimal score) => new(
        name, "E-001", score,
        score, score, score,
        150m, 176m, 120m, 148m,
        40, 3, 20m, 1m,
        0, 0, 0, 0,
        2026, 7, 160m, 158m,
        20m, 22m, 5m, 2m,
        145m, 8m, score);
}
