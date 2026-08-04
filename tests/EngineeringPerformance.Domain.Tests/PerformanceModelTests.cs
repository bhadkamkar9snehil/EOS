using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Domain.Tests;

public sealed class PerformanceModelTests
{
    [Fact]
    public void NumericSeniorityRejectsZero() => Assert.Throws<ArgumentOutOfRangeException>(() => new Employee("E001", "Engineer", 0));

    [Fact]
    public void WeightedScoreRenormalizesApplicableMetrics()
    {
        var result = WeightedScoreCalculator.Calculate([new("timesheet", 100m, 30m), new("punch", 50m, 20m), new("unavailable", 0m, 50m, false)]);
        Assert.Equal(80m, result);
    }
}
