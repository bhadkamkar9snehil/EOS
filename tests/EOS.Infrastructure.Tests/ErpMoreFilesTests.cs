using ClosedXML.Excel;
using EOS.Domain.Capacity;
using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class ErpMoreFilesTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly List<string> _files = [];

    public ErpMoreFilesTests() => (_db, _connection) = EosDatabase.InMemory();

    public void Dispose() { _db.Dispose(); _connection.Dispose(); foreach (var f in _files) File.Delete(f); }

    private string Save(XLWorkbook wb)
    {
        var path = TestWorkbooks.TempPath();
        wb.SaveAs(path); wb.Dispose(); _files.Add(path);
        return path;
    }

    private static void Row(IXLWorksheet ws, int r, int firstCol, params object[] values)
    {
        for (var c = 0; c < values.Length; c++) ws.Cell(r, firstCol + c).Value = XLCellValue.FromObject(values[c]);
    }

    private async Task Seed()
    {
        _db.People.AddRange(new Person { Code = "E1", FullName = "Anna Lead" }, new Person { Code = "E2", FullName = "Bela Dev" });
        _db.Projects.Add(new Project { ProjectNo = "PRO2603013", Name = "Haldia" });
        await _db.SaveChangesAsync();
    }

    private string TaskList(double budget = 20, double filled = 17.33, string status = "Running")
    {
        var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet1");
        Row(ws, 1, 3, "Task No", "Milestone", "\n  XInfoNxt\n", "Activity", "Heads Activity", "Task Name", "Task Type", "Start Date", "End Date", "Total Hour", "Weightage",
            "Timesheet Filled Hours", "Project Task Status", "Created On", "Department", "Employee", "Project No");
        ws.Cell(2, 1).Value = "Department: LPP_Engineering";
        ws.Cell(3, 2).Value = "Employee: Anna Lead";
        Row(ws, 4, 3, "544178", "1. Design & Detail Engineering", "Projects", "X-Force DOW Engineering", "", "Check list Design", "", "2026-10-05", "2026-10-09", budget, 10, filled, status, "2026-08-17", "LPP", "Anna Lead", "PRO2603013");
        Row(ws, 5, 3, "544044", "3. Bulk Engineering", "Projects", "X-Force DOW Engineering", "", "Logsheet", "", "2026-10-12", "2026-10-16", "", 10, 4, "Completed", "2026-08-17", "LPP", "Anna Lead", "PRO2603013");
        Row(ws, 6, 3, "515476", "", "Common support", "", "Training", "", "", "2025-01-10", "2026-01-31", "", "", 35.85, "Running", "2025-09-01", "LPP", "Anna Lead", "");
        Row(ws, 7, 3, "999001", "1. Design", "Projects", "", "", "Orphan", "", "2026-10-05", "2026-10-06", 8, 10, 0, "Not Started", "2026-08-17", "LPP", "Bela Dev", "PRO0000000");
        return Save(wb);
    }

    [Fact]
    public async Task Task_list_becomes_plan_tasks_with_assignment_and_actual_hours()
    {
        await Seed();
        var path = TaskList();
        Assert.Equal(ErpFileKind.TaskList, PerformanceFilesImporter.Detect(path));

        var report = await new PerformanceFilesImporter(_db).ImportAsync(path, true, "t");

        Assert.Equal(3, report.Of("Task").Added);
        Assert.Equal(3, report.Of("Module group").Added);
        Assert.Equal(1, report.Of("Non-project task (not planned)").Unchanged);
        Assert.Equal(1, report.Of("Project (stub from task list)").Added);   // PRO0000000 is not in the project list
        Assert.Contains(await _db.Projects.ToListAsync(), p => p.ProjectNo == "PRO0000000");
        var t = await _db.Tasks.SingleAsync(x => x.ExternalKey == "erp:544178");
        Assert.Equal((20m, 17.33m, "In Progress", TaskOrigins.Erp), (t.WorkHours, t.ActualHours!.Value, t.Status, t.Origin));
        Assert.False(t.WorkIsEstimated);
        var done = await _db.Tasks.SingleAsync(x => x.ExternalKey == "erp:544044");
        Assert.Equal((1m, "Completed", true), (done.PercentComplete, done.Status, done.WorkIsEstimated));   // no budget in the ERP: estimated from the dates
        Assert.Equal(3, await _db.TaskAssignments.CountAsync());
    }

    [Fact]
    public async Task Task_list_re_import_updates_without_duplicating_and_respects_locked_fields()
    {
        await Seed();
        await new PerformanceFilesImporter(_db).ImportAsync(TaskList(), true, "t");
        var t = await _db.Tasks.SingleAsync(x => x.ExternalKey == "erp:544178");
        t.LockedFields = "work"; t.WorkHours = 99; await _db.SaveChangesAsync();

        var report = await new PerformanceFilesImporter(_db).ImportAsync(TaskList(budget: 30, filled: 25, status: "Completed"), true, "t");

        Assert.Equal(0, report.Of("Task").Added);
        Assert.Equal(1, report.Of("Task").Updated);
        Assert.Equal(3, await _db.TaskAssignments.CountAsync());
        t = await _db.Tasks.SingleAsync(x => x.ExternalKey == "erp:544178");
        Assert.Equal((99m, 25m, "Completed"), (t.WorkHours, t.ActualHours!.Value, t.Status));
    }

    [Fact]
    public async Task Attendance_file_also_fills_leave_and_a_re_import_replaces_only_erp_leave()
    {
        await Seed();
        var att = new XLWorkbook(); var a = att.AddWorksheet("Sheet1");
        Row(a, 1, 1, "Employee", "Emp No", "Date", "Attend Day", "Position", "Duty Type", "Leave status", "Leave Type", "Leave Day Type", "Leave Remarks", "Punch Duration",
            "Timesheet Hrs", "Timesheet", "Flg Punch not Found", "Flg Late Coming", "Flg Early Going", "Flg Less Duration", "UAA Status");
        Row(a, 2, 1, "Anna Lead", "E1", "2026-10-07", 1, "Leave", "", "Approved", "PL", "", "Personal", 0, 0, "", false, false, false, false, "");
        Row(a, 3, 1, "Anna Lead", "E1", "2026-10-08", 1, "Leave", "", "Approved", "PL", "", "Personal", 0, 0, "", false, false, false, false, "");
        Row(a, 4, 1, "Anna Lead", "E1", "2026-10-09", 0.5, "Leave", "", "Approved", "CL", "FirstHalf", "Half", 4, 4, "Filled", false, false, false, false, "");
        Row(a, 5, 1, "Anna Lead", "E1", "2026-10-10", 1, "Present", "Regular", "", "", "", "", 4.5, 4.5, "Filled", false, false, false, false, "");
        var path = Save(att);
        _db.Leaves.Add(new Leave { PersonId = (await _db.People.FirstAsync(p => p.Code == "E1")).Id, FromDate = new DateOnly(2026, 11, 2), ToDate = new DateOnly(2026, 11, 3), Kind = "Manual" });
        await _db.SaveChangesAsync();

        var report = await new PerformanceFilesImporter(_db).ImportAsync(path, true, "t");

        Assert.Equal(2, report.Of("Leave").Added);
        var erp = await _db.Leaves.Where(l => l.Origin == "erp").OrderBy(l => l.FromDate).ToListAsync();
        Assert.Equal((new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 8), 1m), (erp[0].FromDate, erp[0].ToDate, erp[0].Fraction));
        Assert.Equal((new DateOnly(2026, 10, 9), 0.5m, "CL"), (erp[1].FromDate, erp[1].Fraction, erp[1].Kind));

        var again = await new PerformanceFilesImporter(_db).ImportAsync(path, true, "t");
        Assert.Equal(2, again.Of("Leave").Unchanged);
        Assert.Equal(3, await _db.Leaves.CountAsync());           // the manually entered leave is untouched
    }

    [Fact]
    public async Task Half_day_leave_removes_only_half_of_a_saturday_in_capacity()
    {
        await Seed();
        var anna = await _db.People.FirstAsync(p => p.Code == "E1");
        _db.Leaves.Add(new Leave { PersonId = anna.Id, FromDate = new DateOnly(2026, 10, 10), ToDate = new DateOnly(2026, 10, 10), Fraction = 0.5m });
        await _db.SaveChangesAsync();

        var week = await new EOS.Infrastructure.Capacity.CapacityService(_db).GetWeeklyLoadAsync(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), personId: anna.Id);

        // Mon-Fri 8 h + Saturday 4.5 h, minus half of Saturday
        Assert.Equal(44.5m - 2.25m, week.Single().CapacityHours);
    }

    [Fact]
    public async Task Timesheet_approval_report_imports_as_timesheets()
    {
        await Seed();
        var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet1");
        Row(ws, 1, 1, "ADD Remarks", "Employee ", "Filled Date", "Start Date", "Start Time", "End Time", "Hrs", "Project No", "Task Type", "Task ", "Description", "Total Task Hours", "Status");
        ws.Cell(2, 1).Value = "Project No: PRO2603013";
        ws.Cell(3, 1).Value = "Employee : Anna Lead";
        Row(ws, 4, 2, "Anna Lead", "2026-10-06 18:00", "2026-10-06", "09:00", "13:00", 4, "PRO2603013", "Off Site", "Logsheet", "Did work", 50, "Approved");
        var path = Save(wb);
        Assert.Equal(ErpFileKind.TimesheetApproval, PerformanceFilesImporter.Detect(path));

        var report = await new PerformanceFilesImporter(_db).ImportAsync(path, true, "t");

        Assert.Equal(1, report.Of("Timesheet entry").Added);
        var entry = await _db.Timesheets.SingleAsync();
        Assert.Equal((4m, "PRO2603013"), (entry.Hours, entry.ProjectNo));
        Assert.NotNull(entry.ProjectId);
    }

    [Fact]
    public async Task Plain_erp_project_list_on_its_first_sheet_imports()
    {
        var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet1");
        Row(ws, 1, 1, "Project No", "Project Name", "Order Amount", "OIGM", "PO Start Date", "PO End Date", "Main Status", "Project Manager", "Technical Manager", "AccountName");
        Row(ws, 2, 1, "PRO2606031", "Onsite support", 47020, "<p>24%</p>", "2026-06-03", "2026-06-30", "RLS", "Brijesh", "Rajnath", "Honeywell");
        var path = Save(wb);
        Assert.Equal(ErpFileKind.ProjectMaster, PerformanceFilesImporter.Detect(path));

        var report = await new PerformanceFilesImporter(_db).ImportAsync(path, true, "t");

        Assert.Equal(1, report.Of("Project").Added);
        Assert.Equal(47020m, (await _db.Projects.SingleAsync()).OrderValue);
    }
}
