using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public void DetailedWorkLogBuildsOneEvidenceRecordPerEngineerWorkdayUsingLatestFilledDate()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-discipline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "worklog.xlsx");
        try
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                string[] headers = ["Employee", "Project Task Status", "Project No", "Project", "Description", "Filled Date", "Date", "Start Time", "EndTime", "Total work Hours"];
                for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
                sheet.Cell(2, 1).Value = "Utsav Patel";
                sheet.Cell(2, 6).Value = new DateTime(2026, 8, 11, 9, 15, 0);
                sheet.Cell(2, 7).Value = new DateTime(2026, 8, 10);
                sheet.Cell(2, 10).Value = 3.5m;
                sheet.Cell(3, 1).Value = "Utsav  Patel";
                sheet.Cell(3, 6).Value = new DateTime(2026, 8, 11, 9, 32, 0);
                sheet.Cell(3, 7).Value = new DateTime(2026, 8, 10);
                sheet.Cell(3, 10).Value = 4.14m;
                workbook.SaveAs(path);
            }

            var evidence = new WorkbookService().ReadTimesheetDayEvidence(path, 2026, 8);

            var day = Assert.Single(evidence);
            Assert.Equal("Utsav Patel", day.EmployeeName);
            Assert.Equal(new DateTime(2026, 8, 11, 9, 32, 0), day.LastFilledAt);
            Assert.Equal(7.64m, day.RecordedHours);
            Assert.Equal(2, day.EntryCount);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void TeamAndEmployeeReportsAreValidWorkbooks()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-reports-{Guid.NewGuid():N}");
        try
        {
            var items = new[]
            {
                new MonthlyPerformanceItem("Priyanka Makwana", "E1001", 92m, 90m, 95m, 88m, 180m, 200m, 120m, 60m, 40, 3, 20, 1, 0, 1, 0, 0, 2026, 7, 150m, 148m, 20, 20, 10m, 5m),
                new MonthlyPerformanceItem("Rohit Sharma", "E1002", 61m, 55m, 70m, 65m, 100m, 200m, 80m, 30m, 20, 2, 18, 2, 3, 2, 1, 1, 2026, 7, 140m, 120m, 15, 20, 5m, 2m)
            };
            var reviews = new[]
            {
                new PeerReviewItem("Rohit Sharma", "E1002", "Priyanka Makwana", "E1001", 5, 4, 5, 4, 4.5m, "Great to work with.")
            };
            var service = new WorkbookService();

            var teamPath = Path.Combine(folder, "TeamReport.xlsx");
            service.GenerateTeamReport(teamPath, new TeamReportData(2026, 7, items, reviews, ["[Warning] Rohit Sharma — Sample alert."], 1));
            Assert.True(File.Exists(teamPath));
            using (var wb = new ClosedXML.Excel.XLWorkbook(teamPath))
                Assert.Contains("Team summary", wb.Worksheets.Select(x => x.Name));

            var employeePath = Path.Combine(folder, "EmployeeReport.xlsx");
            service.GenerateEmployeeReport(employeePath, new EmployeeReportData(
                "Priyanka Makwana", "E1001", 3, 2026, 7, items[0], items, reviews, []));
            Assert.True(File.Exists(employeePath));
            using (var wb = new ClosedXML.Excel.XLWorkbook(employeePath))
                Assert.Contains("Summary", wb.Worksheets.Select(x => x.Name));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

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
    public void PeerReviewSurvivesTheTemplateRoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-peer-{Guid.NewGuid():N}");
        try
        {
            var service = new WorkbookService();
            var employees = new[]
            {
                new Employee("E1001", "Priyanka Makwana", 3),
                new Employee("E1002", "Rohit Sharma", 5),
                new Employee("E1003", "Ananya Iyer", 2)
            };

            var generated = service.GenerateEngineerTemplates(folder, employees, 2026, 7);
            var rohitWorkbook = generated.Single(x => Path.GetFileName(x).StartsWith("E1002", StringComparison.Ordinal));

            // The roster is pre-filled with everyone but the reviewer.
            using (var workbook = new ClosedXML.Excel.XLWorkbook(rohitWorkbook))
            {
                var sheet = workbook.Worksheet("Peer Review");
                // Roster is alphabetical by name: Ananya Iyer, then Priyanka Makwana.
                Assert.Equal("E1003", sheet.Cell(7, 1).GetString());
                Assert.Equal("E1001", sheet.Cell(8, 1).GetString());

                // Rohit rates Priyanka but leaves Ananya blank.
                sheet.Cell(8, 3).Value = 5;
                sheet.Cell(8, 4).Value = 4;
                sheet.Cell(8, 5).Value = 5;
                sheet.Cell(8, 6).Value = 3;
                sheet.Cell(8, 7).Value = "Unblocked the pipeline migration.";
                workbook.Save();
            }

            var reviews = service.ReadPeerReviews(rohitWorkbook, 2026, 7);

            var review = Assert.Single(reviews);
            Assert.Equal("E1002", review.ReviewerCode);
            Assert.Equal("Rohit Sharma", review.ReviewerName);
            Assert.Equal("E1001", review.SubjectCode);
            Assert.Equal("Priyanka Makwana", review.SubjectName);
            Assert.Equal(4.25m, review.Average);
            Assert.Equal("Unblocked the pipeline migration.", review.Comment);
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

        // The detailed timesheet export is a full historical dump (2019-2026 in the supplied
        // reference file), not one reporting month — every row must land in its own real month,
        // not be pooled onto whichever month the caller asked for.
        var distinctMonths = detailRows.Select(x => (x.Year, x.Month)).Distinct().Count();
        Assert.True(distinctMonths > 1, "Detailed timesheet rows should span more than one real month.");
        Assert.True(detailRows.All(x => x.Year > 0 && x.Month is >= 1 and <= 12));
        var julyOnly = detailRows.Where(x => x.Year == 2026 && x.Month == 7).ToArray();
        Assert.True(julyOnly.Length < detailRows.Count, "July rows should be a subset, not the whole export.");
    }

    [Fact]
    public void MultiMonthUtilizationSummaryIsRejectedRatherThanMisattributed()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-multimonth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "Utilization.xlsx");
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                sheet.Cell(1, 1).Value = "Month : ";
                sheet.Cell(1, 2).Value = "01-Apr-2026 to 04-Aug-2026";
                sheet.Cell(3, 1).Value = "Employee Name";
                sheet.Cell(3, 2).Value = "Total Month Hours";
                sheet.Cell(3, 4).Value = "Timsheet Compliance hours";
                sheet.Cell(3, 5).Value = "Total\nEntered Timesheet Hours";
                sheet.Cell(3, 6).Value = "Approved Timesheet Hours";
                sheet.Cell(3, 9).Value = "Billable Hours";
                sheet.Cell(3, 10).Value = "Non Billable Hours";
                sheet.Cell(3, 11).Value = "Sum of Training";
                sheet.Cell(3, 13).Value = "Sum of Office Working Hours";
                sheet.Cell(4, 1).Value = "Someone";
                workbook.SaveAs(path);
            }

            var service = new WorkbookService();
            var ex = Assert.Throws<InvalidDataException>(() => service.ReadPerformance(path, ReportType.MonthlyTimesheetSummary, 2026, 7));
            Assert.Contains("more than one month", ex.Message);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The import log must be append-only: re-uploading the same slot replaces the
    /// <see cref="ImportedSourceFile"/> row but has to leave a second audit line behind,
    /// otherwise a daily upload routine has no reviewable trail.
    /// </summary>
    [Fact]
    public async Task ReimportingASlotAppendsToTheAuditLogRatherThanReplacingIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "audit-test.db");
        try
        {
            var workbookPath = Path.Combine(folder, "RPwiseTimesheetUtilazationReport01-Aug-2026_00_00_00.xlsx");
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                // "Total Month Hours" + "Utilization" is what DetectReportType keys on for this
                // report, and the reader looks the rest up by exact header text.
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
                sheet.Cell(4, 1).Value = "Priyanka Makwana";
                sheet.Cell(4, 2).Value = 200;
                sheet.Cell(4, 3).Value = 95;
                sheet.Cell(4, 4).Value = 200;
                sheet.Cell(4, 5).Value = 190;
                sheet.Cell(4, 6).Value = 185;
                sheet.Cell(4, 9).Value = 150;
                sheet.Cell(4, 10).Value = 40;
                sheet.Cell(4, 11).Value = 0;
                sheet.Cell(4, 13).Value = 180;
                workbook.SaveAs(workbookPath);
            }

            var options = new DbContextOptionsBuilder<PerformanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            var factory = new TestContextFactory(options);
            var database = new LocalApplicationDatabase(factory, new WorkbookService());
            await database.InitializeAsync();

            await database.ImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, workbookPath);
            var afterFirst = await database.GetImportHistoryAsync(2026, 7);
            Assert.Single(afterFirst);
            Assert.False(afterFirst[0].ReplacedExisting);
            Assert.Equal(1, afterFirst[0].RowCount);

            await database.ImportSourceAsync(ReportType.MonthlyTimesheetSummary, 2026, 7, workbookPath);
            var afterSecond = await database.GetImportHistoryAsync(2026, 7);
            Assert.Equal(2, afterSecond.Count);
            // Newest first, and the newer line records that it overwrote the earlier file.
            Assert.True(afterSecond[0].ReplacedExisting);
            Assert.False(afterSecond[1].ReplacedExisting);

            // The slot itself is still single-valued — only the log grew.
            await using var context = factory.CreateDbContext();
            Assert.Single(await context.ImportedSourceFiles
                .Where(x => x.Year == 2026 && x.Month == 7 && x.ReportType == ReportType.MonthlyTimesheetSummary)
                .ToListAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task IndividualReviewerWorkbooksMergeWithoutErasingOtherReviewers()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-review-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "review-merge.db");
        try
        {
            var service = new WorkbookService();
            var employees = new[]
            {
                new Employee("E1001", "Priyanka Makwana", 3),
                new Employee("E1002", "Rohit Sharma", 5),
                new Employee("E1003", "Ananya Iyer", 2)
            };
            var priPath = Path.Combine(folder, "Priyanka.xlsx");
            var ananyaPath = Path.Combine(folder, "Ananya.xlsx");
            service.GenerateEngineerTemplate(priPath, employees[0], 2026, 7, employees);
            service.GenerateEngineerTemplate(ananyaPath, employees[2], 2026, 7, employees);
            FillPeerRating(priPath, "E1002", 5);
            FillPeerRating(ananyaPath, "E1002", 4);

            var options = new DbContextOptionsBuilder<PerformanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            var factory = new TestContextFactory(options);
            var database = new LocalApplicationDatabase(factory, service);
            await database.InitializeAsync();

            var first = await database.ImportEngineerReviewsAsync(2026, 7, [priPath]);
            Assert.Equal(1, first.ReviewerCount);
            Assert.Equal(1, first.TotalReviews);

            var second = await database.ImportEngineerReviewsAsync(2026, 7, [ananyaPath]);
            Assert.Equal(2, second.ReviewerCount);
            Assert.Equal(2, second.TotalReviews);
            Assert.Equal(1, second.AddedReviews);

            FillPeerRating(priPath, "E1002", 2);
            var update = await database.ImportEngineerReviewsAsync(2026, 7, [priPath]);
            Assert.Equal(2, update.ReviewerCount);
            Assert.Equal(2, update.TotalReviews);
            Assert.Equal(1, update.UpdatedReviews);

            var rows = await database.GetPeerReviewsAsync(2026, 7);
            Assert.Equal(2m, rows.Single(x => x.ReviewerCode == "E1001").Collaboration);
            Assert.Equal(4m, rows.Single(x => x.ReviewerCode == "E1003").Collaboration);

            var replace = await database.ImportEngineerReviewsAsync(2026, 7, [priPath], ReviewImportMode.ReplaceMonth);
            Assert.Equal(1, replace.ReviewerCount);
            Assert.Equal(1, replace.TotalReviews);
            Assert.Equal(1, replace.RemovedReviews);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ReviewWorkbookCannotBeImportedIntoTheWrongMonth()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-review-month-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "review-month.db");
        try
        {
            var service = new WorkbookService();
            var reviewer = new Employee("E1001", "Priyanka Makwana", 3);
            var subject = new Employee("E1002", "Rohit Sharma", 5);
            var path = Path.Combine(folder, "July-review.xlsx");
            service.GenerateEngineerTemplate(path, reviewer, 2026, 7, [reviewer, subject]);
            FillPeerRating(path, "E1002", 5);

            var options = new DbContextOptionsBuilder<PerformanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            var database = new LocalApplicationDatabase(new TestContextFactory(options), service);
            await database.InitializeAsync();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                database.ImportEngineerReviewsAsync(2026, 8, [path]));
            Assert.Contains("July 2026", exception.Message);
            Assert.Contains("August 2026", exception.Message);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static void FillPeerRating(string path, string subjectCode, int collaboration)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        var sheet = workbook.Worksheet("Peer Review");
        var row = Enumerable.Range(7, Math.Max(0, (sheet.LastRowUsed()?.RowNumber() ?? 6) - 6))
            .Single(x => sheet.Cell(x, 1).GetString() == subjectCode);
        sheet.Cell(row, 3).Value = collaboration;
        sheet.Cell(row, 4).Value = collaboration;
        sheet.Cell(row, 5).Value = collaboration;
        sheet.Cell(row, 6).Value = collaboration;
        workbook.Save();
    }

    private sealed class TestContextFactory(DbContextOptions<PerformanceDbContext> options) : IDbContextFactory<PerformanceDbContext>
    {
        public PerformanceDbContext CreateDbContext() => new(options);
    }
}
