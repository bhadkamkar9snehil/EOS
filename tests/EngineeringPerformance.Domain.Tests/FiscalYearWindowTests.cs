using EngineeringPerformance.UI;

namespace EngineeringPerformance.Domain.Tests;

public sealed class FiscalYearWindowTests
{
    [Fact]
    public void FiscalYearRunsFromApril2026ThroughMarch2027()
    {
        Assert.Equal(new DateTime(2026, 4, 1), AppState.FiscalYearStart);
        Assert.Equal(new DateTime(2027, 3, 1), AppState.FiscalYearEnd);
        Assert.Equal(12, AppState.FiscalMonths.Count);
        Assert.False(AppState.IsFiscalMonth(new DateTime(2026, 3, 1)));
        Assert.True(AppState.IsFiscalMonth(new DateTime(2026, 4, 30)));
        Assert.True(AppState.IsFiscalMonth(new DateTime(2027, 3, 31)));
        Assert.False(AppState.IsFiscalMonth(new DateTime(2027, 4, 1)));
    }

    [Theory]
    [InlineData(2026, 4, 1)]
    [InlineData(2026, 7, 4)]
    [InlineData(2027, 3, 12)]
    public void HistoryWindowAlwaysStartsAtApril2026(int year, int month, int expectedMonths) =>
        Assert.Equal(expectedMonths, AppState.FiscalMonthsThrough(new DateTime(year, month, 1)));
}
