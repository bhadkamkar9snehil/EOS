using System.IO.Compression;
using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class PackageCanonicalPerformanceIdentityTests
{
    [Fact]
    public async Task Package_reimport_removes_employee_omitted_from_current_summary()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-package-canonical-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "package.db");
        var firstWorkbook = Path.Combine(folder, $"summary-{Guid.NewGuid():N}.xlsx");
        var correctedWorkbook = Path.Combine(folder, $"summary-{Guid.NewGuid():N}.xlsx");
        var firstZip = Path.Combine(folder, "first.zip");
        var correctedZip = Path.Combine(folder, "corrected.zip");

        try
        {
            WriteSummary(firstWorkbook,
            [
                ("Asha Nair", 176m, 160m, 150m),
                ("Bimal Shah", 176m, 150m, 140m)
            ]);
            WriteSummary(correctedWorkbook, [("Asha Nair", 176m, 158m, 148m)]);
            CreatePackage(firstZip, firstWorkbook);
            CreatePackage(correctedZip, correctedWorkbook);

            var options = new DbContextOptionsBuilder<PerformanceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var factory = new TestContextFactory(options);
            var workbookService = new WorkbookService();
            IApplicationDatabase database = new ConfigurableApplicationDatabase(
                new LocalApplicationDatabase(factory, workbookService),
                factory,
                workbookService,
                folder);

            await database.InitializeAsync();
            await database.ImportPackageAsync(2026, 7, firstZip);
            await database.ImportPackageAsync(2026, 7, correctedZip);

            await using var verify = factory.CreateDbContext();
            var rows = await verify.EmployeeMonthlyPerformances
                .Where(x => x.Year == 2026 && x.Month == 7)
                .ToListAsync();

            var remaining = Assert.Single(rows);
            Assert.Equal("Asha Nair", remaining.EmployeeName);
            Assert.Equal(158m, remaining.EnteredHours);
            Assert.DoesNotContain(rows, x => PersonName.Matches(x.EmployeeName, "Bimal Shah"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static void CreatePackage(string zipPath, string workbookPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(workbookPath, Path.GetFileName(workbookPath));
    }

    private static void WriteSummary(
        string path,
        IReadOnlyList<(string Name, decimal Compliance, decimal Entered, decimal Approved)> rows)
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

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = 4 + index;
            sheet.Cell(excelRow, 1).Value = row.Name;
            sheet.Cell(excelRow, 2).Value = row.Compliance;
            sheet.Cell(excelRow, 3).Value = 0.80m;
            sheet.Cell(excelRow, 4).Value = row.Compliance;
            sheet.Cell(excelRow, 5).Value = row.Entered;
            sheet.Cell(excelRow, 6).Value = row.Approved;
            sheet.Cell(excelRow, 9).Value = Math.Min(row.Entered, 120m);
            sheet.Cell(excelRow, 10).Value = 10m;
            sheet.Cell(excelRow, 11).Value = 5m;
            sheet.Cell(excelRow, 13).Value = 75m;
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
