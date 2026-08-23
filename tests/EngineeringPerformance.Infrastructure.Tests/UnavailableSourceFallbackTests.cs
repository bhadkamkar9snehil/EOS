using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class UnavailableSourceFallbackTests
{
    [Fact]
    public async Task Missing_active_source_preserves_zero_row_and_warns_replacement_preview()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-unavailable-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "fallback.db");
        var missingSource = Path.Combine(folder, "missing-summary.xlsx");
        var replacement = Path.Combine(folder, "replacement-summary.xlsx");

        try
        {
            WriteEmptySummary(replacement);
            var options = new DbContextOptionsBuilder<PerformanceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var factory = new TestContextFactory(options);
            var workbookService = new WorkbookService();
            var inner = new LocalApplicationDatabase(factory, workbookService);
            await inner.InitializeAsync();

            await using (var seed = factory.CreateDbContext())
            {
                var zeroRow = new EmployeeMonthlyPerformance
                {
                    Year = 2026,
                    Month = 7,
                    EmployeeName = "Asha Nair"
                };
                zeroRow.Recalculate();
                seed.EmployeeMonthlyPerformances.Add(zeroRow);
                seed.ImportedSourceFiles.Add(new ImportedSourceFile(
                    ReportType.MonthlyTimesheetSummary,
                    2026,
                    7,
                    Path.GetFileName(missingSource),
                    missingSource,
                    1));
                await seed.SaveChangesAsync();
            }

            IApplicationDatabase database = new ConfigurableApplicationDatabase(
                inner,
                factory,
                workbookService,
                folder);
            await database.InitializeAsync();

            await using (var verify = factory.CreateDbContext())
            {
                var row = await verify.EmployeeMonthlyPerformances.SingleAsync(x => x.Year == 2026 && x.Month == 7);
                Assert.Equal("Asha Nair", row.EmployeeName);
                Assert.Equal(0m, row.OperationalScore);
                Assert.Equal(0m, row.ComplianceHours);
            }

            var preview = await database.PreviewImportSourceAsync(
                ReportType.MonthlyTimesheetSummary,
                2026,
                7,
                replacement);
            Assert.NotNull(preview.Warning);
            Assert.Contains("could not be replayed", preview.Warning, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static void WriteEmptySummary(string path)
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
