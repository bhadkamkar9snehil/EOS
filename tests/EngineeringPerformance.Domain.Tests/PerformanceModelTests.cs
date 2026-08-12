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

    [Fact]
    public void CompletedLateObligationRemainsLateAfterEvaluationDateChanges()
    {
        var due = new DateTime(2026, 8, 11, 10, 0, 0);
        var completed = due.AddHours(4).AddMinutes(32);

        var first = ObligationEvaluator.Evaluate(due, completed, due.AddDays(1));
        var later = ObligationEvaluator.Evaluate(due, completed, due.AddMonths(1));

        Assert.Equal(ObligationOutcome.Late, first.Outcome);
        Assert.Equal(ObligationOutcome.Late, later.Outcome);
        Assert.Equal(272, first.DelayMinutes);
    }

    [Fact]
    public void SaturdayWorkMovesToMondayWhenSaturdayIsNotAWorkingDay()
    {
        var friday = new DateTime(2026, 8, 14);
        Assert.Equal(new DateTime(2026, 8, 15, 10, 0, 0),
            ObligationEvaluator.NextWorkingDayDeadline(friday, 10, 0, true));
        Assert.Equal(new DateTime(2026, 8, 17, 10, 0, 0),
            ObligationEvaluator.NextWorkingDayDeadline(friday, 10, 0, false));
    }

    [Fact]
    public void AuditableExceptionReplacesNormalOutcomeWithoutChangingEvidenceTimestamps()
    {
        var due = new DateTime(2026, 8, 11, 10, 0, 0);
        var completed = due.AddHours(5);

        var evaluation = ObligationEvaluator.Evaluate(
            due, completed, due.AddDays(1), exceptionOutcome: ObligationOutcome.Excused);

        Assert.Equal(ObligationOutcome.Excused, evaluation.Outcome);
        Assert.Equal(due, evaluation.DueAt);
        Assert.Equal(completed, evaluation.CompletedAt);
        Assert.Null(evaluation.DelayMinutes);
    }
}
