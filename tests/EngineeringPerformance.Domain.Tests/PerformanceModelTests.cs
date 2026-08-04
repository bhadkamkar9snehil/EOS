using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Domain.Tests;

public sealed class NameAndScoringTests
{
    [Theory]
    [InlineData("Dhruv  Varachhiya", "Dhruv Varachhiya")]
    [InlineData("  Snehil   Bhadkamkar ", "Snehil Bhadkamkar")]
    [InlineData(null, "")]
    public void NamesCollapseToASingleSpacedForm(string? raw, string expected) =>
        Assert.Equal(expected, PersonName.Normalize(raw));

    [Fact]
    public void NamesMatchAcrossInconsistentSpacing() =>
        Assert.True(PersonName.Matches("Dhruv  Varachhiya", "dhruv varachhiya"));

    [Fact]
    public void MissingUtilizationDataIsExcludedFromTheWeightingRatherThanScoredZero()
    {
        // Present on attendance only: no compliance hours, so no timesheet or approval source.
        var attendanceOnly = new EmployeeMonthlyPerformance
        {
            EmployeeName = "Attendance Only",
            ExpectedTimesheetDays = 20,
            TimesheetFilledDays = 20
        };
        attendanceOnly.Recalculate();

        Assert.False(attendanceOnly.HasSummaryData);
        Assert.Equal(100m, attendanceOnly.AttendanceDisciplineScore);
        // Weighted on attendance alone, not 100 * 0.30 = 30.
        Assert.Equal(100m, attendanceOnly.OperationalScore);
    }

    [Fact]
    public void CompleteDataWeightsEveryComponent()
    {
        var item = new EmployeeMonthlyPerformance
        {
            EmployeeName = "Complete",
            ComplianceHours = 100,
            EnteredHours = 100,
            ApprovedHours = 100,
            ExpectedTimesheetDays = 20,
            TimesheetFilledDays = 20
        };
        item.Recalculate();

        Assert.True(item.HasSummaryData);
        Assert.Equal(100m, item.OperationalScore);
    }
}


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
