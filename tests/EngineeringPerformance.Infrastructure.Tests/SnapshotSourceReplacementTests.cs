using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class SnapshotSourceReplacementTests
{
    [Fact]
    public async Task Detailed_snapshot_reimport_removes_omitted_month_even_when_filename_is_reused()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-snapshot-replace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "snapshot.db");
        var workbookPath = Path.Combine(folder, $"details-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteDetails(workbookPath,
            [
                ("Asha Nair", new DateTime(2026, 6, 15), "JUNE-PROJECT", 4m),
                ("Asha Nair", new DateTime(2026, 7, 15), "JULY-PROJECT", 5m)
            ]);

            var options = new DbContextOptionsBuilder<PerformanceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var factory = new TestContextFactory(options);
            var workbookService = new WorkbookService();
            var inner = new LocalApplicationDatabase(factory, workbookService);
            IApplicationDatabase database = new ConfigurableApplicationDatabase(inner, factory, workbookService, folder);
            await database.InitializeAsync();
            await database.ImportSourceAsync(ReportType.DetailedTimesheetTransactions, 2026, 7, workbookPath);

            await using (var initial = factory.CreateDbContext())
            {
                Assert.Single(await initial.EmployeeMonthlyPerformances.Where(x => x.Year == 2026 && x.Month == 6).ToListAsync());
                Assert.Single(await initial.EmployeeMonthlyPerformances.Where(x => x.Year == 2026 && x.Month == 7).ToListAsync());
                Assert.Equal(2, await initial.ImportedSourceFiles.CountAsync(x => x.ReportType == ReportType.DetailedTimesheetTransactions));
            }

            WriteDetails(workbookPath,
            [
                ("Asha Nair", new DateTime(2026, 7, 15), "JULY-PROJECT", 6m)
            ]);

            var preview = await database.PreviewImportSourceAsync(
                ReportType.DetailedTimesheetTransactions,
                2026,
                7,
                workbookPath);
            Assert.Equal(1, preview.RowsRemoved);
            Assert.Contains("Asha Nair", preview.SampleRemoved ?? []);

            await database.ImportSourceAsync(ReportType.DetailedTimesheetTransactions, 2026, 7, workbookPath);

            await using var verify = factory.CreateDbContext();
            Assert.Empty(await verify.EmployeeMonthlyPerformances.Where(x => x.Year == 2026 && x.Month == 6).ToListAsync());
            var july = Assert.Single(await verify.EmployeeMonthlyPerformances.Where(x => x.Year == 2026 && x.Month == 7).ToListAsync());
            Assert.Equal(6m, july.DetailedHours);
            Assert.Equal(1, july.UniqueProjects);

            var slots = await verify.ImportedSourceFiles
                .Where(x => x.ReportType == ReportType.DetailedTimesheetTransactions)
                .ToListAsync();
            var slot = Assert.Single(slots);
            Assert.Equal(2026, slot.Year);
            Assert.Equal(7, slot.Month);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static void WriteDetails(
        string path,
        IReadOnlyList<(string Name, DateTime Date, string Project, decimal Hours)> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell(1, 1).Value = "Employee";
        sheet.Cell(1, 2).Value = "Date";
        sheet.Cell(1, 3).Value = "Project No";
        sheet.Cell(1, 4).Value = "Total work Hours";

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.Name;
            sheet.Cell(excelRow, 2).Value = row.Date;
            sheet.Cell(excelRow, 3).Value = row.Project;
            sheet.Cell(excelRow, 4).Value = row.Hours;
        }

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
