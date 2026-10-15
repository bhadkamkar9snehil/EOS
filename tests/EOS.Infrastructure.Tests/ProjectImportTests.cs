using ClosedXML.Excel;
using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class ProjectImportTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly List<string> _files = [];

    public ProjectImportTests() => (_db, _connection) = EosDatabase.InMemory();

    public void Dispose() { _db.Dispose(); _connection.Dispose(); foreach (var f in _files) File.Delete(f); }

    private string Save(XLWorkbook wb)
    {
        var path = TestWorkbooks.TempPath();
        wb.SaveAs(path);
        wb.Dispose();
        _files.Add(path);
        return path;
    }

    private async Task AddPeople()
    {
        _db.People.AddRange(
            new Person { Code = "E9", FullName = "Mahesh Udar" },
            new Person { Code = "E11", FullName = "Sagar Rana" },
            new Person { Code = "E20", FullName = "Dev Patel" },
            new Person { Code = "E18", FullName = "Dharati Patel" });
        await _db.SaveChangesAsync();
    }

    private string ErpWorkbook(string status = "RLS")
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("ProjectList");
        string[] h = ["Project No", "Project Type", "Project Name", "Order Amount", "OIGM", "PO Start Date", "PO End Date", "Main Status", "Project Manager", "Technical Manager", "AccountName", "Business Categories"];
        for (var c = 0; c < h.Length; c++) ws.Cell(1, c + 1).Value = h[c];
        object[][] rows =
        [
            ["PRO100", "Project and Project Services", "Alpha plant MES", 5000000, "<p><span data-teams=\"true\">12.50%</span></p>", new DateTime(2026, 1, 5), new DateTime(2026, 12, 31), status, "Brijesh", "Sagar Rana", "Alpha Ltd", "MES"],
            ["W_PRO100_1", "Runrate", "Alpha warranty", 0, "<p>0%</p>", new DateTime(2026, 2, 1), new DateTime(2027, 2, 1), "FUTURE ORDER", "Brijesh", "", "Alpha Ltd", "MES"],
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(2 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        var s2 = wb.AddWorksheet("Sheet2");
        s2.Cell(1, 1).Value = "Project No"; s2.Cell(1, 4).Value = "Project Assignments";
        s2.Cell(2, 4).Value = "Project Manager"; s2.Cell(2, 5).Value = "Technical Manager";
        s2.Cell(3, 1).Value = "PRO100"; s2.Cell(3, 3).Value = "MES"; s2.Cell(3, 4).Value = "Chirag K"; s2.Cell(3, 5).Value = "Mahesh Udar";
        return Save(wb);
    }

    [Fact]
    public async Task Erp_Import_Reads_Projects_Strips_Html_And_Links_Variants()
    {
        var report = await new ErpProjectImporter(_db).ImportAsync(ErpWorkbook(), true, "t");

        Assert.Equal(2, report.Of("Project").Added);
        var p = await _db.Projects.SingleAsync(x => x.ProjectNo == "PRO100");
        Assert.Equal(0.125m, p.MarginPercent);                       // "<p><span ...>12.50%</span></p>"
        Assert.Equal(5_000_000m, p.OrderValue);
        Assert.Equal(new DateOnly(2026, 12, 31), p.PoEnd);
        Assert.Equal("Alpha Ltd", p.Customer);
        Assert.Equal("Mahesh Udar", p.TechnicalManager);
        Assert.Equal("Chirag K", p.ProjectManager);
        var warranty = await _db.Projects.SingleAsync(x => x.ProjectNo == "W_PRO100_1");
        Assert.Equal(p.Id, warranty.ParentProjectId);
    }

    [Fact]
    public async Task Erp_Import_Is_Idempotent_And_Refreshes_Changed_Status()
    {
        await new ErpProjectImporter(_db).ImportAsync(ErpWorkbook("RLS"), true, "t");
        var same = await new ErpProjectImporter(_db).ImportAsync(ErpWorkbook("RLS"), true, "t");
        Assert.Equal(0, same.Of("Project").Added + same.Of("Project").Updated);
        Assert.Equal(2, same.Of("Project").Unchanged);

        var changed = await new ErpProjectImporter(_db).ImportAsync(ErpWorkbook("CLS"), true, "t");
        Assert.Equal(1, changed.Of("Project").Updated);
        Assert.Equal("CLS", (await _db.Projects.SingleAsync(x => x.ProjectNo == "PRO100")).ErpStatus);
    }

    [Fact]
    public async Task Erp_Import_Never_Overwrites_People_Maintained_Fields()
    {
        await new ErpProjectImporter(_db).ImportAsync(ErpWorkbook(), true, "t");
        var p = await _db.Projects.SingleAsync(x => x.ProjectNo == "PRO100");
        p.TrackerStatus = "Under Development"; p.Notes = "Customer visit in Nov"; p.HealthOverride = "At Risk";
        await _db.SaveChangesAsync();

        await new ErpProjectImporter(_db).ImportAsync(ErpWorkbook(), true, "t");
        var after = await _db.Projects.SingleAsync(x => x.ProjectNo == "PRO100");
        Assert.Equal(("Under Development", "Customer visit in Nov", "At Risk"), (after.TrackerStatus, after.Notes, after.HealthOverride));
    }

    private string TrackerWorkbook(string progress = "1", string sheetOwner = "MU,SR")
    {
        var wb = new XLWorkbook();
        wb.AddWorksheet("Master").Cell(1, 1).Value = "Entered";

        var lpp = wb.AddWorksheet("LPP");
        string[] lh = ["P. No", "P. No", "Name", "Product Name", "Status", "Project Name", "Technical Manager", "PM", "Assigned Engineer(s)", "Notes / Remarks"];
        for (var c = 0; c < lh.Length; c++) lpp.Cell(1, c + 1).Value = lh[c];
        lpp.Cell(2, 1).Value = 1; lpp.Cell(2, 2).Value = "PRO200"; lpp.Cell(2, 3).Value = "Beta Steel"; lpp.Cell(2, 4).Value = "MES";
        lpp.Cell(2, 5).Value = "Under Development"; lpp.Cell(2, 6).Value = "PRO200-Rolling mill MES"; lpp.Cell(2, 7).Value = "Sagar Rana"; lpp.Cell(2, 8).Value = "Vivek";

        var ws = wb.AddWorksheet("MES Beta");
        ws.Cell(1, 1).Value = "PROJECT TRACKER  |  Rolling mill MES";
        ws.Cell(2, 1).Value = "Project Name"; ws.Cell(2, 2).Value = "PRO200-Rolling mill MES";
        ws.Cell(3, 1).Value = "Project Number"; ws.Cell(3, 2).Value = "PRO200";
        ws.Cell(4, 1).Value = "Project Manager"; ws.Cell(4, 2).Value = "Vivek";
        ws.Cell(5, 1).Value = "Technical Lead"; ws.Cell(5, 2).Value = "Sagar Rana";
        ws.Cell(7, 1).Value = "Planned Completion"; ws.Cell(7, 2).Value = new DateTime(2026, 12, 15);
        ws.Cell(7, 9).Value = "Last Updated"; ws.Cell(7, 12).Value = new DateTime(2026, 10, 12);
        string[] th = ["Sr", "WBS", "Module", "Task Name", "Start Date", "End Date", "Duration\n(Days)", "Progress\n%", "Status", "Priority", "Owner"];
        for (var c = 0; c < th.Length; c++) ws.Cell(9, c + 1).Value = th[c];
        object[][] tasks =
        [
            [1, "1.0", "Reheating", "Furnace screen", new DateTime(2026, 7, 6), new DateTime(2026, 7, 10), 5, 1, "Completed", "", sheetOwner],
            [2, "2.0", "Reheating", "Billet tracking", new DateTime(2026, 7, 13), new DateTime(2026, 7, 17), 5, double.Parse(progress), "In Progress", "", "DP"],
            [3, "3.0", "Mill", "Tag printing", "17-July-26", "20-Sept-26", 0, 0, "Pending", "", "ALL"],
            [3, "4.0", "Mill", "Repeated serial number", new DateTime(2026, 7, 20), new DateTime(2026, 7, 21), 2, 0, "Pending", "", "MU"],
        ];
        for (var r = 0; r < tasks.Length; r++)
            for (var c = 0; c < tasks[r].Length; c++)
                ws.Cell(10 + r, c + 1).Value = XLCellValue.FromObject(tasks[r][c]);

        var template = wb.AddWorksheet("Copied template");
        template.Cell(3, 1).Value = "Project Number"; template.Cell(3, 2).Value = "PRO200";
        template.Cell(9, 1).Value = "Sr"; template.Cell(9, 4).Value = "Task Name";

        var log = wb.AddWorksheet("Observation log");
        log.Cell(1, 1).Value = "Sr. No."; log.Cell(1, 2).Value = "Raised Date";

        var amc = wb.AddWorksheet("AMC");
        string[] ah = ["P. No", "Project Name", "Product Name", "In AMC (Y/N)", "Raised Date", "Raised By", "Report Issue", "Assign Date", "Assign By", "Resolve Date", "Assigned Engineer(s)", "Root Cause"];
        for (var c = 0; c < ah.Length; c++) amc.Cell(1, c + 1).Value = ah[c];
        amc.Cell(2, 1).Value = "A_100_1"; amc.Cell(2, 2).Value = "Gamma"; amc.Cell(2, 3).Value = "Historian"; amc.Cell(2, 4).Value = "Y";
        amc.Cell(2, 5).Value = new DateTime(2026, 7, 6); amc.Cell(2, 7).Value = "Report data not showing"; amc.Cell(2, 8).Value = new DateTime(2026, 7, 6);
        amc.Cell(2, 10).Value = new DateTime(2026, 7, 7); amc.Cell(2, 11).Value = "Chirag D."; amc.Cell(2, 12).Value = "DB connectivity";
        return Save(wb);
    }

    [Fact]
    public async Task Tracker_Import_Builds_A_Wbs_With_Modules_As_Level_One()
    {
        await AddPeople();
        var report = await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), true, "t");

        var project = await _db.Projects.SingleAsync(p => p.ProjectNo == "PRO200");
        Assert.Equal("Rolling mill MES", project.Name);                       // "PRO200-" prefix stripped
        Assert.Equal("Under Development", project.TrackerStatus);
        Assert.Equal(new DateOnly(2026, 12, 15), project.PlannedEnd);
        Assert.Equal(new DateOnly(2026, 10, 12), project.LastStatusUpdate);

        var plan = await new PlanService(_db).GetPlanAsync(project.Id);
        Assert.Equal(2, plan.Tasks.Count(t => t.IsSummary));
        Assert.Equal(["Reheating", "Furnace screen", "Billet tracking", "Mill", "Tag printing", "Repeated serial number"], plan.Tasks.Select(t => t.Name));
        Assert.Equal(["1", "1.1", "1.2", "2", "2.1", "2.2"], plan.Tasks.Select(t => t.Wbs));
        Assert.Equal(2, report.Of("Module group").Added);
        Assert.Equal(4, report.Of("Task").Added);
    }

    [Fact]
    public async Task Tracker_Tasks_Keep_Tracker_Dates_As_Fixed_Duration_And_Flag_Work_As_Estimated()
    {
        await AddPeople();
        await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), true, "t");
        var t = await _db.Tasks.SingleAsync(x => x.Name == "Furnace screen");

        Assert.Equal(new DateOnly(2026, 7, 6), t.PlannedStart);
        Assert.Equal(5, t.FixedDurationDays);
        Assert.Equal(new DateOnly(2026, 7, 6), t.StartNoEarlierThan);
        Assert.True(t.WorkIsEstimated);
        Assert.Equal(5 * 8m * 2, t.WorkHours);
        Assert.Equal(TaskOrigins.Tracker, t.Origin);
    }

    [Fact]
    public async Task Imported_Schedule_Reproduces_The_Tracker_Dates()
    {
        await AddPeople();
        await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), true, "t");
        var project = await _db.Projects.SingleAsync(p => p.ProjectNo == "PRO200");

        var plan = await new PlanService(_db).RecomputeAsync(project.Id, new DateOnly(2026, 10, 12));
        var furnace = plan.Tasks.Single(x => x.Name == "Furnace screen");
        Assert.Equal((new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 10)), (furnace.Start!.Value, furnace.Finish!.Value));
        var tag = plan.Tasks.Single(x => x.Name == "Tag printing");            // "17-July-26" and "20-Sept-26" parsed
        Assert.Equal(new DateOnly(2026, 7, 17), tag.PlannedStart);
        Assert.Equal(new DateOnly(2026, 9, 20), tag.PlannedFinish);
    }

    [Fact]
    public async Task Initials_Resolve_Uniquely_Or_Are_Reported_Never_Guessed()
    {
        await AddPeople();
        var report = await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), true, "t");

        var assigned = await _db.TaskAssignments.Join(_db.People, a => a.PersonId, p => p.Id, (a, p) => p.FullName).Distinct().ToListAsync();
        Assert.Contains("Mahesh Udar", assigned);
        Assert.Contains("Sagar Rana", assigned);
        Assert.DoesNotContain("Dev Patel", assigned);                          // "DP" is ambiguous: Dev Patel / Dharati Patel
        Assert.DoesNotContain("Dharati Patel", assigned);
        Assert.Contains(report.Unresolved, u => u.StartsWith("DP (ambiguous", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Unresolved, u => u.StartsWith("ALL", StringComparison.OrdinalIgnoreCase));   // "ALL" is not a person
    }

    [Fact]
    public async Task A_Manual_Alias_Resolution_Is_Applied_On_The_Next_Import()
    {
        await AddPeople();
        await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), true, "t");
        var dharati = await _db.People.SingleAsync(p => p.Code == "E18");
        var alias = await _db.PersonAliases.SingleAsync(a => a.Alias == "DP");
        Assert.Null(alias.PersonId);
        alias.PersonId = dharati.Id;
        await _db.SaveChangesAsync();

        var report = await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), true, "t");
        Assert.DoesNotContain(report.Unresolved, u => u.StartsWith("DP", StringComparison.Ordinal));
        Assert.True(await _db.TaskAssignments.AnyAsync(a => a.PersonId == dharati.Id));
    }

    [Fact]
    public async Task Tracker_Import_Is_Idempotent_And_Skips_Non_Trackers_With_Warnings()
    {
        await AddPeople();
        var file = TrackerWorkbook();
        await new TrackerImporter(_db).ImportAsync(file, true, "t");
        var again = await new TrackerImporter(_db).ImportAsync(file, true, "t");

        Assert.Equal(0, again.Of("Task").Added + again.Of("Task").Updated);
        Assert.Equal(4, again.Of("Task").Unchanged);
        Assert.Equal(0, again.Of("Assignment").Added);
        Assert.Contains(again.Warnings, w => w.Contains("Copied template") && w.Contains("no tasks"));
        Assert.Contains(again.Warnings, w => w.Contains("Observation log") && w.Contains("no task-name column"));
        Assert.Contains(again.Warnings, w => w.Contains("Sr 3 is repeated"));
        Assert.Equal(1, await _db.AmcTickets.CountAsync());
    }

    [Fact]
    public async Task A_Reimport_Keeps_Fields_A_Person_Edited_And_Never_Deletes_Tasks()
    {
        await AddPeople();
        await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(progress: "0.2"), true, "t");
        var task = await _db.Tasks.SingleAsync(x => x.Name == "Billet tracking");
        var plans = new PlanService(_db);
        await plans.UpdateTaskAsync(task.Id, new TaskPatch { WorkHours = 200, Name = "Billet tracking (re-estimated)" }, "snehil");
        var extra = await plans.AddTaskAsync(task.ProjectId, "Added by a lead", "snehil", workHours: 8);

        await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(progress: "0.6"), true, "t");

        var after = await _db.Tasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal("Billet tracking (re-estimated)", after.Name);          // locked by the lead's edit
        Assert.Equal(200m, after.WorkHours);
        Assert.False(after.WorkIsEstimated);
        Assert.Equal(0.6m, after.PercentComplete);
        Assert.True(await _db.Tasks.AnyAsync(x => x.Id == extra.Id));
    }

    [Fact]
    public async Task Preview_Saves_Nothing()
    {
        await AddPeople();
        var report = await new TrackerImporter(_db).ImportAsync(TrackerWorkbook(), false, "t");
        Assert.False(report.Committed);
        Assert.Equal(0, await _db.Projects.CountAsync());
        Assert.Equal(0, await _db.Tasks.CountAsync());
    }

    [Theory]
    [InlineData("05-Sept-26", 2026, 9, 5)]
    [InlineData("17-July-26", 2026, 7, 17)]
    [InlineData("14-09-2026", 2026, 9, 14)]
    [InlineData("2026-09-14", 2026, 9, 14)]
    [InlineData("3 Oct 2026", 2026, 10, 3)]
    public void Tolerant_Date_Text_Is_Parsed(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), WorkbookReading.ParseDateText(text));

    [Theory]
    [InlineData("0")]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("not a date")]
    public void Junk_Date_Text_Is_Null(string text) => Assert.Null(WorkbookReading.ParseDateText(text));

    private string VmWorkbook(string secret)
    {
        var wb = new XLWorkbook();
        var phys = wb.AddWorksheet("New Physical Server Details");
        string[] h = ["Sr. No", "Status", "Server Type", "IP Address", "Used For", "Username", "Password", "C Drive\nRemaining/Total", "Total RAM", "CPU Cores ", "Logical Processors"];
        for (var c = 0; c < h.Length; c++) phys.Cell(1, c + 1).Value = h[c];
        object[][] rows =
        [
            [1, "", "Physical Server", "10.9.0.1", "Base Server", "adminuser", secret, "5.5/100 GB", "32 GB", 16, 24],
            [2, "", "VM on 10.9.0.1", "10.9.0.20", "PRO300_Publish", "Administrator", secret, "", "", "", ""],
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                phys.Cell(2 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);

        var vm = wb.AddWorksheet("Sheet1");
        string[] vh = ["Host IP", "VM IP", "Virtual Machine", "State", "Project / Purpose", "Assigned To", "Need Backup", "isdelete VM", "VM Removed", "VM Password"];
        for (var c = 0; c < vh.Length; c++) vm.Cell(1, c + 1).Value = vh[c];
        object[][] vms =
        [
            ["10.9.0.1", "10.9.0.20", "PRO300_Publish", "Running", "Gamma MES", "Gamma project", "", "", 0, secret],
            ["10.9.0.1", "10.9.0.21", "PRO300_Database", "Running", "Gamma MES", "Gamma project", "", "", 0, secret],
            ["10.9.0.1", "—", "OLD_POC", "Off", "Unknown", "Someone", "No", "Yes", 0, secret],
        ];
        for (var r = 0; r < vms.Length; r++)
            for (var c = 0; c < vms[r].Length; c++)
                vm.Cell(2 + r, c + 1).Value = XLCellValue.FromObject(vms[r][c]);
        return Save(wb);
    }

    [Fact]
    public async Task Vm_Import_Never_Reads_Credential_Columns()
    {
        const string secret = "S3CR3T-SENTINEL-9f2a";
        _db.Projects.Add(new EOS.Domain.Projects.Project { ProjectNo = "PRO300", Name = "Gamma MES" });
        await _db.SaveChangesAsync();

        var report = await new InfrastructureImporter(_db).ImportAsync(VmWorkbook(secret), true, "t");

        Assert.Equal(0, await CountOccurrencesInAllTablesAsync(secret));
        Assert.DoesNotContain(secret, report.ToText());
        Assert.DoesNotContain(secret, string.Join('|', await _db.AuditEvents.Select(a => a.Detail ?? "").ToListAsync()));
        Assert.Equal(1, await _db.Hosts.CountAsync(h => h.Kind == "physical"));
        Assert.Equal(3, await _db.Hosts.CountAsync(h => h.Kind == "vm"));
    }

    [Fact]
    public async Task Vm_Import_Captures_Disks_States_And_Links_Vms_To_Projects()
    {
        _db.Projects.Add(new EOS.Domain.Projects.Project { ProjectNo = "PRO300", Name = "Gamma MES" });
        await _db.SaveChangesAsync();
        await new InfrastructureImporter(_db).ImportAsync(VmWorkbook("x-secret-123"), true, "t");

        var disk = await _db.HostDisks.SingleAsync();
        Assert.Equal((5.5m, 100m, "C"), (disk.FreeGb, disk.TotalGb, disk.Drive));
        var host = await _db.Hosts.SingleAsync(h => h.Kind == "physical");
        Assert.Equal((32m, 16, 24), (host.RamGb, host.CpuCores, host.LogicalProcessors));

        var off = await _db.Hosts.SingleAsync(h => h.Name == "OLD_POC");
        Assert.Equal("Off", off.State);
        Assert.True(off.MarkedForDeletion);
        Assert.False(off.NeedBackup);

        var links = await _db.ProjectHosts.ToListAsync();
        Assert.Equal(["database", "publish"], links.Select(l => l.Role).OrderBy(x => x));
    }

    [Fact]
    public async Task Vm_Import_Is_Idempotent()
    {
        var file = VmWorkbook("x-secret-123");
        await new InfrastructureImporter(_db).ImportAsync(file, true, "t");
        var again = await new InfrastructureImporter(_db).ImportAsync(file, true, "t");
        Assert.Equal(0, again.Of("VM").Added + again.Of("Host").Added + again.Of("Disk").Added);
        Assert.Equal(1, await _db.HostDisks.CountAsync());
    }

    private async Task<int> CountOccurrencesInAllTablesAsync(string needle)
    {
        var tables = new List<string>();
        var conn = _db.Database.GetDbConnection();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var hits = 0;
        foreach (var table in tables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{table}\"";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                for (var i = 0; i < reader.FieldCount; i++)
                    if (!reader.IsDBNull(i) && reader.GetValue(i) is string s && s.Contains(needle, StringComparison.Ordinal)) hits++;
        }
        return hits;
    }
}
