using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task DatabaseCanInitialize()
    {
        var options = new DbContextOptionsBuilder<PerformanceDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var context = new PerformanceDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public void TemplatesAreGeneratedForEveryEmployeeAtOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-tests-{Guid.NewGuid():N}");
        try
        {
            var employees = new[]
            {
                new Employee("E1001", "Priyanka Makwana", 3),
                new Employee("E1002", "Rohit Sharma", 5),
                new Employee("E1003", "Ananya Iyer", 2)
            };

            var generated = new WorkbookService().GenerateEngineerTemplates(folder, employees, 2026, 7);

            Assert.Equal(3, generated.Count);
            Assert.All(generated, path => Assert.True(File.Exists(path)));
            Assert.Contains(generated, path => Path.GetFileName(path) == "E1002_Rohit_Sharma_2026_07_Review.xlsx");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void SeniorityLevelAndNameCanBeEdited()
    {
        var employee = new Employee("E1001", "Priyanka Makwana", 3);

        employee.Rename("Priyanka M");
        employee.SetSeniorityLevel(6);

        Assert.Equal("Priyanka M", employee.Name);
        Assert.Equal(6, employee.SeniorityLevel);
        Assert.Throws<ArgumentOutOfRangeException>(() => employee.SetSeniorityLevel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => employee.SetSeniorityLevel(100));
        Assert.Throws<ArgumentException>(() => employee.Rename("  "));
    }

    [Fact]
    public void ReferenceReportsAreDetectedAndParsed()
    {
        // The real ERP exports hold employee records and are not committed, so this
        // test only runs on machines that have them beside the solution.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Reference Excel Files"))) root = root.Parent;
        if (root is null) return;
        var references = Path.Combine(root!.FullName, "Reference Excel Files");
        var service = new WorkbookService();

        var attendance = Path.Combine(references, "LV_LeaveSummaryforRP.xlsx");
        var details = Path.Combine(references, "LV_Timesheet_ManagerHead_Rpt.xlsx");
        var summary = Path.Combine(references, "RPwiseTimesheetUtilazationReport04-Aug-2026_08_43_30.xlsx");

        Assert.Equal(ReportType.AttendanceLeaveUaaTimesheet, service.DetectReportType(attendance));
        Assert.Equal(ReportType.DetailedTimesheetTransactions, service.DetectReportType(details));
        Assert.Equal(ReportType.MonthlyTimesheetSummary, service.DetectReportType(summary));

        var summaryRows = service.ReadPerformance(summary, ReportType.MonthlyTimesheetSummary, 2026, 7);
        var detailRows = service.ReadPerformance(details, ReportType.DetailedTimesheetTransactions, 2026, 7);
        var attendanceRows = service.ReadPerformance(attendance, ReportType.AttendanceLeaveUaaTimesheet, 2026, 7);
        Assert.True(summaryRows.Count >= 15);
        Assert.True(detailRows.Count >= 10);
        Assert.True(attendanceRows.Count >= 10);
        var priyanka = summaryRows.Single(x => x.EmployeeName == "Priyanka Makwana");
        Assert.Equal(193.5m, priyanka.ComplianceHours);
        Assert.Equal(111.17m, priyanka.EnteredHours);
    }
}
