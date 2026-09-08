using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class CanonicalStartupRecoveryTests
{
    [Fact]
    public async Task Initialize_rebuilds_persisted_month_from_active_source_slot_and_then_is_idempotent()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-startup-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "recovery.db");
        var workbookPath = Path.Combine(folder, $"summary-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteMonthlySummary(workbookPath, "Asha Nair", 100m, 80m, 70m);

            var options = new DbContextOptionsBuilder<PerformanceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var factory = new TestContextFactory(options);
            var workbookService = new WorkbookService();
            var inner = new LocalApplicationDatabase(factory, workbookService);
            await inner.InitializeAsync();

            await using (var seed = factory.CreateDbContext())
            {
                var stale = new EmployeeMonthlyPerformance
                {
                    Year = 2026,
                    Month = 7,
                    EmployeeName = "Asha Nair",
                    ComplianceHours = 220m,
                    EnteredHours = 210m,
                    ApprovedHours = 205m,
                    BillableHours = 180m
                };
                stale.Recalculate();
                seed.EmployeeMonthlyPerformances.Add(stale);
                seed.ImportedSourceFiles.Add(new ImportedSourceFile(
                    ReportType.MonthlyTimesheetSummary,
                    2026,
                    7,
                    Path.GetFileName(workbookPath),
                    workbookPath,
                    1));
                await seed.SaveChangesAsync();
            }

            IApplicationDatabase database = new ConfigurableApplicationDatabase(
                inner,
                factory,
                workbookService,
                folder);
            await database.InitializeAsync();

            int repairedId;
            await using (var verify = factory.CreateDbContext())
            {
                var row = await verify.EmployeeMonthlyPerformances.SingleAsync(x => x.Year == 2026 && x.Month == 7);
                repairedId = row.Id;
                Assert.Equal("Asha Nair", row.EmployeeName);
                Assert.Equal(100m, row.ComplianceHours);
                Assert.Equal(80m, row.EnteredHours);
                Assert.Equal(70m, row.ApprovedHours);
            }

            await database.InitializeAsync();

            await using var secondVerify = factory.CreateDbContext();
            var unchanged = await secondVerify.EmployeeMonthlyPerformances.SingleAsync(x => x.Year == 2026 && x.Month == 7);
            Assert.Equal(repairedId, unchanged.Id);
            Assert.Equal(100m, unchanged.ComplianceHours);
            Assert.Equal(80m, unchanged.EnteredHours);
            Assert.Equal(70m, unchanged.ApprovedHours);
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
        sheet.Cell(4, 3).Value = 0.80m;
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
