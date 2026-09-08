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
