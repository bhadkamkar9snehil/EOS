using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class PlanServiceTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly PlanService _plans;
    private readonly Project _project = new() { ProjectNo = "PRO-T1", Name = "Test project", PoStart = new DateOnly(2026, 10, 5) };
    private static DateOnly D(int m, int d) => new(2026, m, d);

    public PlanServiceTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        _db.Projects.Add(_project);
        _db.SaveChanges();
        _plans = new PlanService(_db);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<Person> Person(string code, string name)
    {
        var p = new Person { Code = code, FullName = name };
        _db.People.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task Wbs_Codes_Follow_The_Hierarchy_And_Order()
    {
        var a = await _plans.AddTaskAsync(_project.Id, "Design", "t");
        var a1 = await _plans.AddTaskAsync(_project.Id, "Draft", "t", parentId: a.Id, workHours: 8);
        var a2 = await _plans.AddTaskAsync(_project.Id, "Review", "t", afterTaskId: a1.Id, workHours: 8);
        var b = await _plans.AddTaskAsync(_project.Id, "Build", "t", workHours: 16);

        var plan = await _plans.GetPlanAsync(_project.Id);
        Assert.Equal(["1", "1.1", "1.2", "2"], plan.Tasks.Select(t => t.Wbs));
        Assert.Equal(["Design", "Draft", "Review", "Build"], plan.Tasks.Select(t => t.Name));
        Assert.True(plan.Tasks[0].IsSummary);
        Assert.False(plan.Tasks[3].IsSummary);
        Assert.Equal(2, plan.Tasks[1].Level);
        _ = (a2, b);
    }

    [Fact]
    public async Task Indent_Outdent_And_Move_Reshape_The_Tree()
    {
        var a = await _plans.AddTaskAsync(_project.Id, "A", "t");
        var b = await _plans.AddTaskAsync(_project.Id, "B", "t");
        var c = await _plans.AddTaskAsync(_project.Id, "C", "t");

        await _plans.MoveTaskAsync(b.Id, MoveDirection.Indent, "t");
        Assert.Equal(["1", "1.1", "2"], (await _plans.GetPlanAsync(_project.Id)).Tasks.Select(t => t.Wbs));

        await _plans.MoveTaskAsync(c.Id, MoveDirection.Up, "t");
        Assert.Equal(["C", "A", "B"], (await _plans.GetPlanAsync(_project.Id)).Tasks.Select(t => t.Name));

        await _plans.MoveTaskAsync(b.Id, MoveDirection.Outdent, "t");
        var plan = await _plans.GetPlanAsync(_project.Id);
        Assert.Equal(["1", "2", "3"], plan.Tasks.Select(t => t.Wbs));
        Assert.Equal(["C", "A", "B"], plan.Tasks.Select(t => t.Name));
        _ = a;
    }

    [Fact]
    public async Task Moving_A_Task_Up_Carries_Its_Subtree()
    {
        var a = await _plans.AddTaskAsync(_project.Id, "A", "t");
        var b = await _plans.AddTaskAsync(_project.Id, "B", "t");
        await _plans.AddTaskAsync(_project.Id, "B1", "t", parentId: b.Id);
        await _plans.MoveTaskAsync(b.Id, MoveDirection.Up, "t");
        Assert.Equal(["B", "B1", "A"], (await _plans.GetPlanAsync(_project.Id)).Tasks.Select(t => t.Name));
        _ = a;
    }

    [Fact]
    public async Task Deleting_A_Summary_Deletes_Its_Subtree_Links_And_Assignments()
    {
        var anna = await Person("E1", "Anna");
        var s = await _plans.AddTaskAsync(_project.Id, "Summary", "t");
        var k = await _plans.AddTaskAsync(_project.Id, "Kid", "t", parentId: s.Id, workHours: 8);
        var other = await _plans.AddTaskAsync(_project.Id, "Other", "t", workHours: 8);
        await _plans.AddLinkAsync(k.Id, other.Id, 0, 0, "t");
        await _plans.AssignAsync(k.Id, anna.Id, 1m, "t");

        await _plans.DeleteTaskAsync(s.Id, "t");

        Assert.Equal(["Other"], (await _plans.GetPlanAsync(_project.Id)).Tasks.Select(t => t.Name));
        Assert.Empty(await _db.TaskLinks.ToListAsync());
        Assert.Empty(await _db.TaskAssignments.ToListAsync());
    }

    [Fact]
    public async Task Links_Reject_Cycles_Self_Summaries_And_Duplicates()
    {
        var a = await _plans.AddTaskAsync(_project.Id, "A", "t", workHours: 8);
        var b = await _plans.AddTaskAsync(_project.Id, "B", "t", workHours: 8);
        var c = await _plans.AddTaskAsync(_project.Id, "C", "t", workHours: 8);
        await _plans.AddLinkAsync(a.Id, b.Id, 0, 0, "t");
        await _plans.AddLinkAsync(b.Id, c.Id, 0, 0, "t");

        await Assert.ThrowsAsync<PlanException>(() => _plans.AddLinkAsync(c.Id, a.Id, 0, 0, "t"));
        await Assert.ThrowsAsync<PlanException>(() => _plans.AddLinkAsync(a.Id, a.Id, 0, 0, "t"));
        await Assert.ThrowsAsync<PlanException>(() => _plans.AddLinkAsync(a.Id, b.Id, 0, 0, "t"));
        await Assert.ThrowsAsync<PlanException>(() => _plans.AddLinkAsync(a.Id, b.Id, 9, 0, "t"));

        await _plans.AddTaskAsync(_project.Id, "A1", "t", parentId: a.Id, workHours: 8);
        await Assert.ThrowsAsync<PlanException>(() => _plans.AddLinkAsync(a.Id, c.Id, 0, 0, "t"));
    }

    [Fact]
    public async Task Links_Cannot_Cross_Projects()
    {
        var other = new Project { ProjectNo = "PRO-T2", Name = "Other" };
        _db.Projects.Add(other);
        await _db.SaveChangesAsync();
        var a = await _plans.AddTaskAsync(_project.Id, "A", "t", workHours: 8);
        var x = await _plans.AddTaskAsync(other.Id, "X", "t", workHours: 8);
        await Assert.ThrowsAsync<PlanException>(() => _plans.AddLinkAsync(a.Id, x.Id, 0, 0, "t"));
    }

    [Fact]
    public async Task A_Plan_Authored_In_EOS_Reproduces_The_Golden_Project1_Dates()
    {
        var mahesh = await Person("E9", "Mahesh");
        var lalji = await Person("E6", "Lalji");
        var twinkle = await Person("E17", "Twinkle");

        var t11 = await _plans.AddTaskAsync(_project.Id, "Design & detail engineering", "t", workHours: 96);
        var t12 = await _plans.AddTaskAsync(_project.Id, "Historian configuration", "t", workHours: 192);
        var t13 = await _plans.AddTaskAsync(_project.Id, "Dashboard development", "t", workHours: 144);
        var t14 = await _plans.AddTaskAsync(_project.Id, "FAT", "t", workHours: 48);
        var t15 = await _plans.AddTaskAsync(_project.Id, "Site installation", "t", workHours: 96);
        await _plans.AddLinkAsync(t11.Id, t12.Id, 0, 0, "t");
        await _plans.AddLinkAsync(t11.Id, t13.Id, 0, 0, "t");
        await _plans.AddLinkAsync(t12.Id, t14.Id, 0, 0, "t");
        await _plans.AddLinkAsync(t13.Id, t14.Id, 0, 0, "t");
        await _plans.AddLinkAsync(t14.Id, t15.Id, 0, 0, "t");
        foreach (var t in new[] { t11, t12, t14, t15 }) await _plans.AssignAsync(t.Id, mahesh.Id, 1m, "t");
        foreach (var t in new[] { t12, t14, t15 }) await _plans.AssignAsync(t.Id, lalji.Id, 1m, "t");
        await _plans.AssignAsync(t13.Id, twinkle.Id, 1m, "t");

        var plan = await _plans.RecomputeAsync(_project.Id, D(10, 5));

        PlanTaskView Task(string name) => plan.Tasks.Single(t => t.Name == name);
        Assert.Equal((D(10, 5), D(10, 17)), (Task("Design & detail engineering").Start!.Value, Task("Design & detail engineering").Finish!.Value));
        Assert.Equal((D(10, 19), D(10, 31)), (Task("Historian configuration").Start!.Value, Task("Historian configuration").Finish!.Value));
        Assert.Equal(D(11, 7), Task("Dashboard development").Finish);
        Assert.Equal(D(11, 11), Task("FAT").Finish);
        Assert.Equal(D(11, 18), plan.Finish);
        Assert.Equal(6, Task("Historian configuration").TotalFloatDays);
        Assert.True(Task("FAT").IsCritical);
        Assert.False(Task("Historian configuration").IsCritical);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task Summaries_Roll_Up_Dates_Work_And_Progress()
    {
        var anna = await Person("E1", "Anna");
        var s = await _plans.AddTaskAsync(_project.Id, "Phase", "t");
        var k1 = await _plans.AddTaskAsync(_project.Id, "K1", "t", parentId: s.Id, workHours: 80);
        var k2 = await _plans.AddTaskAsync(_project.Id, "K2", "t", parentId: s.Id, workHours: 40);
        await _plans.AddLinkAsync(k1.Id, k2.Id, 0, 0, "t");
        await _plans.AssignAsync(k1.Id, anna.Id, 1m, "t");
        await _plans.AssignAsync(k2.Id, anna.Id, 1m, "t");
        await _plans.UpdateTaskAsync(k1.Id, new TaskPatch { PercentComplete = 1m }, "t");

        var plan = await _plans.RecomputeAsync(_project.Id, D(10, 5));
        var summary = plan.Tasks.Single(t => t.Name == "Phase");
        Assert.True(summary.IsSummary);
        Assert.Equal(120m, summary.WorkHours);
        Assert.Equal(Math.Round(80m / 120m, 4), Math.Round(summary.PercentComplete, 4));
        Assert.Equal(plan.Tasks.Single(t => t.Name == "K2").Finish, summary.Finish);
        Assert.Equal("In Progress", summary.Status);
    }

    [Fact]
    public async Task Unassigned_Tasks_Get_A_Default_Crew_And_A_Warning()
    {
        await _plans.AddTaskAsync(_project.Id, "Lonely", "t", workHours: 16);
        var plan = await _plans.RecomputeAsync(_project.Id, D(10, 5));
        Assert.Equal(D(10, 6), plan.Tasks[0].Finish);
        Assert.Contains(plan.Warnings, w => w.Contains("no assigned engineer"));
    }

    [Fact]
    public async Task A_Task_With_No_Work_Or_Duration_Uses_The_Configured_Default()
    {
        await _plans.AddTaskAsync(_project.Id, "Empty", "t");
        var plan = await _plans.RecomputeAsync(_project.Id, D(10, 5));
        Assert.Equal(1, plan.Tasks[0].Start == plan.Tasks[0].Finish ? 1 : 0);
        Assert.Contains(plan.Warnings, w => w.Contains("assumed 1 day"));
    }

    [Fact]
    public async Task Hours_Per_Day_Setting_Via_Calendar_Changes_The_Schedule()
    {
        var anna = await Person("E1", "Anna");
        var t = await _plans.AddTaskAsync(_project.Id, "Work", "t", workHours: 96);
        await _plans.AssignAsync(t.Id, anna.Id, 1m, "t");
        Assert.Equal(12, WorkingDays(await _plans.RecomputeAsync(_project.Id, D(10, 5))));

        var cal = await _db.Calendars.SingleAsync(c => c.IsDefault);
        cal.HoursPerDay = 12m;
        await _db.SaveChangesAsync();
        Assert.Equal(8, WorkingDays(await _plans.RecomputeAsync(_project.Id, D(10, 5))));
    }

    private static int WorkingDays(PlanView plan)
    {
        var t = plan.Tasks[0];
        var cal = new EOS.Scheduling.WorkCalendar(8, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]);
        return cal.WorkingDaysBetween(t.Start!.Value, t.Finish!.Value) + 1;
    }

    [Fact]
    public async Task Fixed_Duration_And_Start_Constraint_Reproduce_Imported_Dates()
    {
        var t = await _plans.AddTaskAsync(_project.Id, "Booked FAT", "t");
        await _plans.UpdateTaskAsync(t.Id, new TaskPatch { FixedDurationDays = 3, StartNoEarlierThan = D(10, 14) }, "t");
        var plan = await _plans.RecomputeAsync(_project.Id, D(10, 5));
        Assert.Equal((D(10, 14), D(10, 16)), (plan.Tasks[0].Start!.Value, plan.Tasks[0].Finish!.Value));
    }

    [Fact]
    public async Task Progress_Sets_Status_And_Validates_Range()
    {
        var t = await _plans.AddTaskAsync(_project.Id, "T", "t", workHours: 8);
        await _plans.UpdateTaskAsync(t.Id, new TaskPatch { PercentComplete = 0.5m }, "t");
        Assert.Equal("In Progress", (await _db.Tasks.SingleAsync()).Status);
        await _plans.UpdateTaskAsync(t.Id, new TaskPatch { PercentComplete = 1m }, "t");
        Assert.Equal("Completed", (await _db.Tasks.SingleAsync()).Status);
        await Assert.ThrowsAsync<PlanException>(() => _plans.UpdateTaskAsync(t.Id, new TaskPatch { PercentComplete = 1.5m }, "t"));
        await Assert.ThrowsAsync<PlanException>(() => _plans.UpdateTaskAsync(t.Id, new TaskPatch { Status = "Bogus" }, "t"));
    }

    [Fact]
    public async Task Baseline_Variance_Shows_Slip_After_A_Change()
    {
        var anna = await Person("E1", "Anna");
        var t = await _plans.AddTaskAsync(_project.Id, "Work", "t", workHours: 40);
        await _plans.AssignAsync(t.Id, anna.Id, 1m, "t");
        await _plans.RecomputeAsync(_project.Id, D(10, 5));
        var baseline = await _plans.CreateBaselineAsync(_project.Id, "Plan v1", "t");
        Assert.Equal(0, baseline.Number);

        await _plans.UpdateTaskAsync(t.Id, new TaskPatch { WorkHours = 80 }, "t");
        await _plans.RecomputeAsync(_project.Id, D(10, 5));

        var variance = await _plans.GetBaselineVarianceAsync(_project.Id);
        Assert.Equal(5, variance.FinishVarianceDays);
        Assert.Equal(40m, variance.Rows.Single().WorkVarianceHours);
    }

    [Fact]
    public async Task Editing_An_Imported_Task_Locks_The_Edited_Fields()
    {
        var imported = new PlanTask { ProjectId = _project.Id, Name = "From tracker", Origin = TaskOrigins.Tracker, ExternalKey = "7", SortOrder = 1 };
        _db.Tasks.Add(imported);
        await _db.SaveChangesAsync();

        await _plans.UpdateTaskAsync(imported.Id, new TaskPatch { Name = "Renamed by a lead", WorkHours = 40 }, "snehil");

        var stored = await _db.Tasks.SingleAsync();
        Assert.Contains("name", stored.LockedFields!.Split(','));
        Assert.Contains("work", stored.LockedFields!.Split(','));
        Assert.False(stored.WorkIsEstimated);
    }

    [Fact]
    public async Task Every_Plan_Edit_Is_Audited()
    {
        var t = await _plans.AddTaskAsync(_project.Id, "T", "snehil", workHours: 8);
        await _plans.UpdateTaskAsync(t.Id, new TaskPatch { Notes = "n" }, "snehil");
        var actions = await _db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("plan.task.add", actions);
        Assert.Contains("plan.task.update", actions);
    }
}
