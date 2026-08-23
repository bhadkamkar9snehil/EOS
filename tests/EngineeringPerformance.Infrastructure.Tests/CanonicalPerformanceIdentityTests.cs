using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class CanonicalPerformanceIdentityTests
{
    [Fact]
    public async Task Corrected_reimport_replaces_stale_name_variants_with_current_source_values()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-canonical-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "canonical.db");
        var workbookPath = Path.Combine(folder, $"RPwiseTimesheetUtilazationReport-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteMonthlySummary(workbookPath, "Asha Nair", compliance: 100m, entered: 80m, approved: 70m);

            var options = new DbContextOptionsBuilder<PerformanceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var factory = new TestContextFactory(options);
            var workbookService = new WorkbookService();
            var inner = new LocalApplicationDatabase(factory, workbookService);
            IApplicationDatabase database = new ConfigurableApplicationDatabase(
                inner,
                factory,
                workbookService,
                folder);

            await database.InitializeAsync();

            await using (var seed = factory.CreateDbContext())
            {
                var staleCanonical = new EmployeeMonthlyPerformance
                {
                    Year = 2026,
                    Month = 7,
                    EmployeeName = "Asha Nair",
                    ComplianceHours = 220m,
                    EnteredHours = 210m,
                    ApprovedHours = 205m,
                    BillableHours = 180m,
                    DetailedHours = 12m,
                    DetailedEntries = 3,
                    UniqueProjects = 2
                };
                staleCanonical.Recalculate();

                var staleVariant = new EmployeeMonthlyPerformance
                {
                    Year = 2026,
                    Month = 7,
                    EmployeeName = "Asha   Nair",
                    ComplianceHours = 240m,
                    EnteredHours = 230m,
                    ApprovedHours = 220m,
                    BillableHours = 190m,
                    AttendanceDays = 20m,
                    PunchHours = 160m,
                    AttendanceTimesheetHours = 158m,
                    TimesheetFilledDays = 20m,
                    ExpectedTimesheetDays = 22m,
                    LateDays = 1
                };
                staleVariant.Recalculate();

                seed.EmployeeMonthlyPerformances.AddRange(staleCanonical, staleVariant);
                await seed.SaveChangesAsync();
            }

            var preview = await database.PreviewImportSourceAsync(
                ReportType.MonthlyTimesheetSummary,
                2026,
                7,
                workbookPath);
            Assert.Equal(0, preview.RowsAdded);
            Assert.Equal(1, preview.RowsUpdated);

            await database.ImportSourceAsync(
                ReportType.MonthlyTimesheetSummary,
                2026,
                7,
                workbookPath);

            await using var verify = factory.CreateDbContext();
            var candidates = await verify.EmployeeMonthlyPerformances
                .Where(x => x.Year == 2026 && x.Month == 7)
                .ToListAsync();
            var row = Assert.Single(candidates.Where(x => PersonName.Matches(x.EmployeeName, "Asha Nair")));

            Assert.Equal("Asha Nair", row.EmployeeName);
            Assert.Equal(100m, row.ComplianceHours);
            Assert.Equal(80m, row.EnteredHours);
            Assert.Equal(70m, row.ApprovedHours);
            Assert.Equal(12m, row.DetailedHours);
            Assert.Equal(3, row.DetailedEntries);
            Assert.Equal(20m, row.AttendanceDays);
            Assert.Equal(160m, row.PunchHours);
            Assert.Equal(22m, row.ExpectedTimesheetDays);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static void WriteMonthlySummary(
        string path,
        string employeeName,
        decimal compliance,
        decimal entered,
        decimal approved)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell(3, 1).Value = "Employee Name";
        sheet.Cell(3, 2).Value = "Total Month Hours";
        sheet.Cell(3, 3).Value = "Utilization";
        sheet.Cell(3, 4).Value = "Timsheet Compliance hours";
        sheet.Cell(3, 5).Value = "Total\nEntered Timesheet Hours";
        sheet.Cell(3, 6).Value = "Approved Timesheet Hours";
        sheet.Cell(3, 9).Value = "Billable Hours";
        sheet.Cell(3, 10).Value = "Non Billable Hours";
        sheet.Cell(3, 11).Value = "Sum of Training";
        sheet.Cell(3, 13).Value = "Sum of Office Working Hours";
        sheet.Cell(4, 1).Value = employeeName;
        sheet.Cell(4, 2).Value = compliance;
        sheet.Cell(4, 3).Value = 80m;
        sheet.Cell(4, 4).Value = compliance;
        sheet.Cell(4, 5).Value = entered;
        sheet.Cell(4, 6).Value = approved;
        sheet.Cell(4, 9).Value = 60m;
        sheet.Cell(4, 10).Value = 10m;
        sheet.Cell(4, 11).Value = 5m;
        sheet.Cell(4, 13).Value = 75m;
        workbook.SaveAs(path);
    }

    private sealed class TestContextFactory(DbContextOptions<PerformanceDbContext> options)
        : IDbContextFactory<PerformanceDbContext>
    {
        public PerformanceDbContext CreateDbContext() => new(options);
        public Task<PerformanceDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PerformanceDbContext(options));
    }
}
