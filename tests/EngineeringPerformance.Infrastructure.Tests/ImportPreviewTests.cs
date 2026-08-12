using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.Infrastructure.Tests;

/// <summary>
/// A minimal IWorkbookService double that returns pre-built performance rows instead of parsing a
/// real xlsx — the import preview logic under test is the diffing, not workbook parsing (already
/// covered by WorkbookServiceTests-style workbook round-trips elsewhere).
/// </summary>
file sealed class FakeWorkbookService(ReportType reportType, IReadOnlyList<EmployeeMonthlyPerformance> rows) : IWorkbookService
{
    public WorkbookInspection Inspect(string filePath) => new(Path.GetFileName(filePath), ["Sheet1"], 1);
    public ReportType DetectReportType(string filePath) => reportType;
    public IReadOnlyList<EmployeeMonthlyPerformance> ReadPerformance(string filePath, ReportType type, int year, int month) => rows;
    public IReadOnlyList<WeeklyPerformanceItem> ReadWeeklyPerformance(string filePath, ReportType type, int year, int month) => [];
    public IReadOnlyList<PeerReview> ReadPeerReviews(string filePath, int year, int month) => [];
    public IReadOnlyList<RosterEntry> ReadEmployeeRoster(string filePath) => [];
    public IReadOnlyList<TimesheetDayEvidence> ReadTimesheetDayEvidence(string filePath, int year, int month) => [];
    public IReadOnlyList<AccountableWorkday> ReadAccountableWorkdays(string filePath, int year, int month) => [];
    public void GenerateEngineerTemplate(string destinationPath, Employee employee, int year, int month, IReadOnlyList<Employee>? peers = null) { }
    public IReadOnlyList<string> GenerateEngineerTemplates(string destinationFolder, IReadOnlyList<Employee> employees, int year, int month) => [];
    public void GenerateEmployeeReport(string destinationPath, EmployeeReportData data) { }
    public void GenerateTeamReport(string destinationPath, TeamReportData data) { }
}

public sealed class ImportPreviewTests
{
    private static IDbContextFactory<PerformanceDbContext> CreateFactory(out ServiceProvider provider)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"epa-preview-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<PerformanceDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<PerformanceDbContext>>();
        using (var context = factory.CreateDbContext())
            context.Database.EnsureCreated();
        return factory;
    }

    [Fact]
    public async Task PreviewImportSourceAsync_ReportsAllRowsAsAdded_WhenDatabaseIsEmpty()
    {
        var factory = CreateFactory(out var provider);
        try
        {
            var rows = new[]
            {
                new EmployeeMonthlyPerformance { Year = 2026, Month = 7, EmployeeName = "Priyanka Makwana", ComplianceHours = 180m, EnteredHours = 170m },
                new EmployeeMonthlyPerformance { Year = 2026, Month = 7, EmployeeName = "Rohit Sharma", ComplianceHours = 160m, EnteredHours = 150m }
            };
            var database = new LocalApplicationDatabase(factory, new FakeWorkbookService(ReportType.MonthlyTimesheetSummary, rows));

            var preview = await database.PreviewImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, "fake.xlsx");

            Assert.Equal(2, preview.TotalRows);
            Assert.Equal(2, preview.RowsAdded);
            Assert.Equal(0, preview.RowsUpdated);
            Assert.Equal(0, preview.RowsUnchanged);

            // Preview must not have written anything to the database.
            await using var verify = await factory.CreateDbContextAsync();
            Assert.Equal(0, await verify.EmployeeMonthlyPerformances.CountAsync());
        }
        finally { provider.Dispose(); }
    }

    [Fact]
    public async Task PreviewImportSourceAsync_DistinguishesUpdatedFromUnchangedRows()
    {
        var factory = CreateFactory(out var provider);
        try
        {
            await using (var seed = await factory.CreateDbContextAsync())
            {
                seed.EmployeeMonthlyPerformances.Add(new EmployeeMonthlyPerformance
                { Year = 2026, Month = 7, EmployeeName = "Priyanka Makwana", ComplianceHours = 180m, EnteredHours = 170m });
                seed.EmployeeMonthlyPerformances.Add(new EmployeeMonthlyPerformance
                { Year = 2026, Month = 7, EmployeeName = "Rohit Sharma", ComplianceHours = 160m, EnteredHours = 150m });
                await seed.SaveChangesAsync();
            }

            var rows = new[]
            {
                // Same values as seeded -> unchanged.
                new EmployeeMonthlyPerformance { Year = 2026, Month = 7, EmployeeName = "Priyanka Makwana", ComplianceHours = 180m, EnteredHours = 170m },
                // Different EnteredHours -> updated.
                new EmployeeMonthlyPerformance { Year = 2026, Month = 7, EmployeeName = "Rohit Sharma", ComplianceHours = 160m, EnteredHours = 155m },
                // New employee -> added.
                new EmployeeMonthlyPerformance { Year = 2026, Month = 7, EmployeeName = "Ananya Iyer", ComplianceHours = 100m, EnteredHours = 90m }
            };
            var database = new LocalApplicationDatabase(factory, new FakeWorkbookService(ReportType.MonthlyTimesheetSummary, rows));

            var preview = await database.PreviewImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, "fake.xlsx");

            Assert.Equal(3, preview.TotalRows);
            Assert.Equal(1, preview.RowsAdded);
            Assert.Equal(1, preview.RowsUpdated);
            Assert.Equal(1, preview.RowsUnchanged);
            Assert.Contains("Ananya Iyer", preview.SampleAdded);
            Assert.Contains("Rohit Sharma", preview.SampleUpdated);

            // Still nothing committed by the preview.
            await using var verify = await factory.CreateDbContextAsync();
            Assert.Equal(2, await verify.EmployeeMonthlyPerformances.CountAsync());
        }
        finally { provider.Dispose(); }
    }

    [Fact]
    public async Task PreviewImportSourceAsync_ThrowsWhenDetectedTypeDoesNotMatchRequested()
    {
        var factory = CreateFactory(out var provider);
        try
        {
            var database = new LocalApplicationDatabase(factory,
                new FakeWorkbookService(ReportType.AttendanceLeaveUaaTimesheet, []));

            await Assert.ThrowsAsync<InvalidDataException>(
                () => database.PreviewImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, "fake.xlsx"));
        }
        finally { provider.Dispose(); }
    }
}
