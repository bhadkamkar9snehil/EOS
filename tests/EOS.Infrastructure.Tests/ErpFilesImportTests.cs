using System.IO.Compression;
using ClosedXML.Excel;
using EOS.Domain.People;
using EOS.Domain.Projects;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class ErpFilesImportTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly List<string> _files = [];

    public ErpFilesImportTests() => (_db, _connection) = EosDatabase.InMemory();

    public void Dispose() { _db.Dispose(); _connection.Dispose(); foreach (var f in _files) File.Delete(f); }

    private string Save(XLWorkbook wb)
    {
        var path = TestWorkbooks.TempPath();
        wb.SaveAs(path); wb.Dispose(); _files.Add(path);
        return path;
    }

    private static void Row(IXLWorksheet ws, int r, params object[] values)
    {
        for (var c = 0; c < values.Length; c++) ws.Cell(r, c + 1).Value = XLCellValue.FromObject(values[c]);
    }

    private async Task Seed()
    {
        _db.People.AddRange(new Person { Code = "E1", FullName = "Anna Lead" }, new Person { Code = "E2", FullName = "Bela Dev" });
        _db.Projects.Add(new Project { ProjectNo = "PRO2602059", Name = "E-Logbook" });
        await _db.SaveChangesAsync();
    }

    private string Timesheet(double hoursOnEdited = 2, string filled = "2026-10-20 10:00")
    {
        var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet1");
        Row(ws, 1, "Employee", "Project No", "Project", "Filled Date", "Date", "Start Time", "EndTime", "Total work Hours", "Bandlevel", "Milestone", "Activity", "Task", "Task Type");
        Row(ws, 2, "Anna Lead", "V_PRO2602059_2", "E-Logbook", filled, "2026-10-19", "09:00", "11:00", hoursOnEdited, "L1", "1. Design", "DOW", "Report", "Off Site");
        Row(ws, 3, "Bela Dev", "", "", "2026-10-20 10:00", "2026-10-19", "09:00", "12:00", 3, "L2", "", "", "Training", "Technical");
        Row(ws, 4, "Ghost Person", "PRO9999999", "Other", "2026-10-20 10:00", "2026-10-20", "09:00", "10:00", 1, "L2", "", "", "Work", "Technical");
        return Save(wb);
    }

    [Fact]
    public async Task Timesheet_is_detected_links_variant_project_and_creates_unknown_people_inactive()
    {
        await Seed();
        var path = Timesheet();
        Assert.Equal(ErpFileKind.DetailedTimesheet, PerformanceFilesImporter.Detect(path));

        var report = await new PerformanceFilesImporter(_db).ImportAsync(path, true, "t");

        Assert.Equal(3, report.Of("Timesheet entry").Added);
        var anna = await _db.Timesheets.SingleAsync(t => t.ProjectNo == "V_PRO2602059_2");
        Assert.Equal(await _db.Projects.Select(p => (int?)p.Id).FirstAsync(), anna.ProjectId);
        var ghost = await _db.People.SingleAsync(p => p.FullName == "Ghost Person");
        Assert.False(ghost.IsActive);
        Assert.Equal(1, (await _db.MonthlyPerformance.SingleAsync(m => m.PersonId == anna.PersonId)).UniqueProjects);
    }

    [Fact]
    public async Task Re_import_of_an_edited_entry_replaces_it_instead_of_doubling_it()
    {
        await Seed();
        await new PerformanceFilesImporter(_db).ImportAsync(Timesheet(), true, "t");
        var report = await new PerformanceFilesImporter(_db).ImportAsync(Timesheet(hoursOnEdited: 4, filled: "2026-10-21 09:00"), true, "t");

        Assert.Equal(1, report.Of("Timesheet entry").Added);
        Assert.Equal(1, report.Of("Timesheet entry replaced").Updated);
        Assert.Equal(3, await _db.Timesheets.CountAsync());
        Assert.Equal(4m, (await _db.Timesheets.SingleAsync(t => t.ProjectNo == "V_PRO2602059_2")).Hours);
    }

    [Fact]
    public async Task Preview_saves_nothing()
    {
        await Seed();
        await new PerformanceFilesImporter(_db).ImportAsync(Timesheet(), false, "t");
        Assert.Equal(0, await _db.Timesheets.CountAsync());
        Assert.Equal(2, await _db.People.CountAsync());
    }

    [Fact]
    public async Task Summary_attendance_and_scores_come_from_raw_files()
    {
        await Seed();
        var summary = new XLWorkbook(); var s = summary.AddWorksheet("Sheet1");
        s.Cell(1, 1).Value = "Month :"; s.Cell(1, 2).Value = "01-Oct-2026 to 31-Oct-2026";
        Row(s, 3, "Employee Name", "Timsheet Compliance hours", "Total\nEntered Timesheet Hours", "Approved Timesheet Hours", "Billable Hours",
            "Non Billable Hours", "Sum of Training", "Sum of Office Working Hours", "Total Month Hours", "Utilization");
        Row(s, 4, "Anna Lead", 160, 120, 90, 100, 20, 10, 5, 170, 0.53);
        var summaryPath = Save(summary);

        var att = new XLWorkbook(); var a = att.AddWorksheet("Sheet1");
        Row(a, 1, "Employee", "Emp No", "Date", "Attend Day", "Position", "Duty Type", "Leave status", "Punch Duration", "Timesheet Hrs", "Timesheet",
            "Flg Punch not Found", "Flg Late Coming", "Flg Early Going", "Flg Less Duration", "UAA Status");
        Row(a, 2, "Anna Lead", "E1", "2026-10-05", 1, "Present", "Regular", "", 9, 8, "Filled", false, true, false, false, "OK");
        Row(a, 3, "Anna Lead", "E1", "2026-10-06", 1, "Present", "Regular", "", 9, 8, "Not Filled", false, false, false, false, "OK");
        Row(a, 4, "Anna Lead", "E1", "2026-10-11", 1, "Present", "woff", "", 0, 0, "", false, false, false, false, "OK");
        var attPath = Save(att);

        var zip = TestWorkbooks.TempPath() + ".zip"; _files.Add(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            z.CreateEntryFromFile(summaryPath, "summary.xlsx");
            z.CreateEntryFromFile(attPath, "attendance.xlsx");
        }

        var report = await new PerformanceFilesImporter(_db).ImportAsync(zip, true, "t");

        var row = await _db.MonthlyPerformance.SingleAsync();
        Assert.Equal((2026, 10), (row.Year, row.Month));
        Assert.Equal(53m, row.Utilization);
        Assert.Equal(75m, row.TimesheetCompletionScore);
        Assert.Equal(2m, row.ExpectedTimesheetDays);          // the week-off Sunday is not accountable
        Assert.Equal(1m, row.TimesheetFilledDays);
        Assert.Equal(1, row.LateDays);
        // 0.55*75 + 0.15*75 + 0.30*attendance, attendance = 0.40*50 + 0.25*100 + 0.20*100 + 0.15*75
        Assert.Equal(76.25m, row.AttendanceDisciplineScore);
        Assert.Equal(decimal.Round((0.55m * 75 + 0.15m * 75 + 0.30m * 76.25m) / 1.0m, 2), row.OperationalScore);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task Multi_month_summary_is_refused()
    {
        await Seed();
        var wb = new XLWorkbook(); var s = wb.AddWorksheet("Sheet1");
        s.Cell(1, 1).Value = "Month :"; s.Cell(1, 2).Value = "01-Oct-2026 to 30-Nov-2026";
        Row(s, 3, "Employee Name", "Timsheet Compliance hours", "Total\nEntered Timesheet Hours", "Approved Timesheet Hours", "Billable Hours",
            "Non Billable Hours", "Sum of Training", "Sum of Office Working Hours", "Total Month Hours", "Utilization");
        Row(s, 4, "Anna Lead", 160, 120, 90, 100, 20, 10, 5, 170, 0.53);
        var report = await new PerformanceFilesImporter(_db).ImportAsync(Save(wb), true, "t");
        Assert.Contains(report.Warnings, w => w.Contains("more than one month"));
        Assert.Equal(0, await _db.MonthlyPerformance.CountAsync());
    }

    [Fact]
    public async Task Review_workbook_and_roster_import()
    {
        await Seed();
        var roster = new XLWorkbook(); var r = roster.AddWorksheet("Sheet1");
        Row(r, 1, "Employee No", "Full Name", "Band Level", "Official Email", "IsConsultant");
        Row(r, 2, "Admin Reporting: Boss", "", "", "", "");
        Row(r, 3, "E1", "Anna Lead", "L3", "Anna@Example.com", false);
        Row(r, 4, "E7", "New Joiner", "L1", "", true);
        var rosterPath = Save(roster);
        Assert.Equal(ErpFileKind.Roster, PerformanceFilesImporter.Detect(rosterPath));

        var rev = new XLWorkbook();
        var m = rev.AddWorksheet("Template Metadata");
        Row(m, 1, "EmployeeCode", "E1"); Row(m, 2, "EmployeeName", "Anna Lead"); Row(m, 3, "Year", 2026); Row(m, 4, "Month", 10);
        var p = rev.AddWorksheet("Peer Review");
        Row(p, 6, "Peer Code", "Peer Name", "Collaboration (1-5)", "Communication (1-5)", "Reliability (1-5)", "Technical Help (1-5)", "Comment");
        Row(p, 7, "E2", "Bela Dev", 5, 4, 4, 3, "Helpful");
        Row(p, 8, "E7", "New Joiner", "", "", "", "", "");
        var revPath = Save(rev);
        Assert.Equal(ErpFileKind.ReviewWorkbook, PerformanceFilesImporter.Detect(revPath));

        var zip = TestWorkbooks.TempPath() + ".zip"; _files.Add(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { z.CreateEntryFromFile(rosterPath, "roster.xlsx"); z.CreateEntryFromFile(revPath, "review.xlsx"); }
        await new PerformanceFilesImporter(_db).ImportAsync(zip, true, "t");

        var anna = await _db.People.SingleAsync(x => x.Code == "E1");
        Assert.Equal((3, "anna@example.com"), (anna.Seniority, anna.Email));
        Assert.True((await _db.People.SingleAsync(x => x.Code == "E7")).IsConsultant);
        var review = await _db.PeerReviews.SingleAsync();
        Assert.Equal((10, 5m, "Helpful"), (review.Month, review.Collaboration, review.Comment));

        // A newer workbook from the same reviewer replaces what they rated before.
        var again = await new PerformanceFilesImporter(_db).ImportAsync(revPath, true, "t");
        Assert.Equal(1, again.Of("Peer review").Updated);
        Assert.Equal(1, await _db.PeerReviews.CountAsync());
    }

    [Fact]
    public async Task Unrecognised_file_is_skipped_with_a_warning()
    {
        var wb = new XLWorkbook(); wb.AddWorksheet("Sheet1").Cell(1, 1).Value = "nothing useful";
        var report = await new PerformanceFilesImporter(_db).ImportAsync(Save(wb), true, "t");
        Assert.Contains(report.Warnings, w => w.Contains("not a recognised ERP report"));
    }
}
