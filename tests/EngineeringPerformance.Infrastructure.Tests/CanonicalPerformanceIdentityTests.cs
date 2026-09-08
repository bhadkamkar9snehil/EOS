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
        var folder = CreateFolder();
        var databasePath = Path.Combine(folder, "canonical.db");
        var workbookPath = Path.Combine(folder, $"RPwiseTimesheetUtilazationReport-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteMonthlySummary(workbookPath, [("Asha Nair", 100m, 80m, 70m)]);
            var (database, factory) = CreateDatabase(folder, databasePath);
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
            Assert.Equal(0, preview.RowsRemoved);

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
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task Reimport_removes_summary_evidence_for_employee_omitted_from_current_source()
    {
        var folder = CreateFolder();
        var databasePath = Path.Combine(folder, "replacement.db");
        var firstPath = Path.Combine(folder, $"RPwiseTimesheetUtilazationReport-{Guid.NewGuid():N}.xlsx");
        var correctedPath = Path.Combine(folder, $"RPwiseTimesheetUtilazationReport-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteMonthlySummary(firstPath,
            [
                ("Asha Nair", 176m, 160m, 150m),
                ("Bimal Shah", 176m, 150m, 140m)
            ]);
            WriteMonthlySummary(correctedPath, [("Asha Nair", 176m, 158m, 148m)]);

            var (database, factory) = CreateDatabase(folder, databasePath);
            await database.InitializeAsync();
            await database.ImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, firstPath);

            await using (var before = factory.CreateDbContext())
            {
                Assert.Equal(2, await before.EmployeeMonthlyPerformances.CountAsync(x => x.Year == 2026 && x.Month == 7));
            }

            var preview = await database.PreviewImportSourceAsync(
                ReportType.MonthlyTimesheetSummary,
                2026,
                7,
                correctedPath);
            Assert.Equal(1, preview.RowsUpdated);
            Assert.Equal(1, preview.RowsRemoved);
            Assert.Contains("Bimal Shah", preview.SampleRemoved ?? []);

            await database.ImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, correctedPath);

            await using var after = factory.CreateDbContext();
            var rows = await after.EmployeeMonthlyPerformances
                .Where(x => x.Year == 2026 && x.Month == 7)
                .ToListAsync();
            var remaining = Assert.Single(rows);
            Assert.Equal("Asha Nair", remaining.EmployeeName);
            Assert.Equal(158m, remaining.EnteredHours);
            Assert.DoesNotContain(rows, x => PersonName.Matches(x.EmployeeName, "Bimal Shah"));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task Empty_reimport_clears_superseded_monthly_summary_evidence()
    {
        var folder = CreateFolder();
        var databasePath = Path.Combine(folder, "empty-replacement.db");
        var firstPath = Path.Combine(folder, $"RPwiseTimesheetUtilazationReport-{Guid.NewGuid():N}.xlsx");
        var emptyPath = Path.Combine(folder, $"RPwiseTimesheetUtilazationReport-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteMonthlySummary(firstPath, [("Asha Nair", 176m, 160m, 150m)]);
            WriteMonthlySummary(emptyPath, []);

            var (database, factory) = CreateDatabase(folder, databasePath);
            await database.InitializeAsync();
            await database.ImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, firstPath);

            await using (var before = factory.CreateDbContext())
            {
                Assert.Single(await before.EmployeeMonthlyPerformances
                    .Where(x => x.Year == 2026 && x.Month == 7)
                    .ToListAsync());
            }

            var preview = await database.PreviewImportSourceAsync(
                ReportType.MonthlyTimesheetSummary,
                2026,
                7,
                emptyPath);
            Assert.Equal(0, preview.TotalRows);
            Assert.Equal(1, preview.RowsRemoved);
            Assert.Contains("Asha Nair", preview.SampleRemoved ?? []);

            await database.ImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, emptyPath);

            await using var after = factory.CreateDbContext();
            Assert.Empty(await after.EmployeeMonthlyPerformances
                .Where(x => x.Year == 2026 && x.Month == 7)
                .ToListAsync());
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static (IApplicationDatabase Database, TestContextFactory Factory) CreateDatabase(
        string folder,
        string databasePath)
    {
        var options = new DbContextOptionsBuilder<PerformanceDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var factory = new TestContextFactory(options);
        var workbookService = new WorkbookService();
        var inner = new LocalApplicationDatabase(factory, workbookService);
        return (new ConfigurableApplicationDatabase(inner, factory, workbookService, folder), factory);
    }

    private static string CreateFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eos-canonical-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private static void WriteMonthlySummary(
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
