using EOS.Domain.Capacity;
using EOS.Domain.People;
using EOS.Domain.Projects;
using EOS.Domain.Settings;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class CapacityServiceTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly CapacityService _capacity;
    private readonly PlanService _plans;
    private static DateOnly D(int m, int d) => new(2026, m, d);

    public CapacityServiceTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        _capacity = new CapacityService(_db);
        _plans = new PlanService(_db);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<Person> Person(string name, decimal availability = 1m)
    {
        var p = new Person { Code = "E" + (_db.People.Count() + 1), FullName = name, AvailabilityFraction = availability };
        _db.People.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<Project> Project(string no)
    {
        var p = new Project { ProjectNo = no, Name = no, PoStart = D(10, 5) };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task PlanTask(Project p, Person who, decimal work)
    {
        var t = await _plans.AddTaskAsync(p.Id, "Work", "t", workHours: work);
        await _plans.AssignAsync(t.Id, who.Id, 1m, "t");
        await _plans.RecomputeAsync(p.Id, D(10, 5));
    }

    [Fact]
    public async Task Monthly_Capacity_Uses_The_Calendar_Availability_And_Target_Utilisation()
    {
        var half = await Person("Half", 0.5m);
        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: half.Id)).Single();

        Assert.Equal(27 * 8 * 0.5m, row.AvailableHours);
        Assert.Equal(108 * 0.85m, row.PlannedHours);
        Assert.Equal(0m, row.AllocatedHours);
        Assert.Equal(LoadBand.UnderUsed, row.Band);
    }

    [Fact]
    public async Task Fixed_Monthly_Capacity_Mode_Is_A_Setting()
    {
        var p = await Person("Full");
        await new SettingsStore(_db).SetAsync(SettingKeys.CapacityMode, SettingScope.Global, "\"fixedMonthly\"", D(1, 1), "t", "Use 160 h/month");
        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: p.Id)).Single();
        Assert.Equal(160m, row.AvailableHours);
    }

    [Fact]
    public async Task Target_Utilisation_Can_Be_Overridden_Per_Person()
    {
        var a = await Person("A");
        var b = await Person("B");
        await new SettingsStore(_db).SetAsync(SettingKeys.TargetUtilisation, SettingScope.Person(b.Id.ToString()), "0.5", D(1, 1), "t", "B is part-time on projects");
        var rows = await _capacity.GetCapacityAsync(D(10, 1), D(10, 31));
        Assert.Equal(0.85m, rows.Single(r => r.PersonId == a.Id).PlannedHours / rows.Single(r => r.PersonId == a.Id).AvailableHours);
        Assert.Equal(0.5m, rows.Single(r => r.PersonId == b.Id).PlannedHours / rows.Single(r => r.PersonId == b.Id).AvailableHours);
    }

    [Fact]
    public async Task Leave_Reduces_Available_Hours()
    {
        var p = await Person("Away");
        _db.Leaves.Add(new Leave { PersonId = p.Id, FromDate = D(10, 12), ToDate = D(10, 17), Kind = "Leave" });
        await _db.SaveChangesAsync();
        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: p.Id)).Single();
        Assert.Equal(6 * 8m, row.LeaveHours);
        Assert.Equal((27 - 6) * 8m, row.AvailableHours);
    }

    [Fact]
    public async Task Plan_Assignments_Become_Allocated_Hours_By_Project()
    {
        var anna = await Person("Anna");
        var p = await Project("PRJ");
        await PlanTask(p, anna, 80);
        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single();

        Assert.Equal(80m, row.AllocatedHours);
        Assert.Equal("plan", row.ByProject.Single().Source);
        Assert.Equal("PRJ", row.ByProject.Single().ProjectNo);
        Assert.Equal(Math.Round(80m / row.PlannedHours, 4), row.LoadVsPlanned);
    }

    [Fact]
    public async Task Overlapping_Parallel_Tasks_Show_As_Over_Allocated()
    {
        var anna = await Person("Anna", 0.5m);
        var p1 = await Project("P1"); var p2 = await Project("P2");
        await PlanTask(p1, anna, 96);
        await PlanTask(p2, anna, 96);
        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single();

        Assert.Equal(192m, row.AllocatedHours);
        Assert.Equal(LoadBand.OverAllocated, row.Band);
        Assert.True(row.LoadVsAvailable > 1m);
        Assert.Equal(2, row.ByProject.Count);
    }

    [Fact]
    public async Task Load_Bands_Follow_The_Settings()
    {
        var anna = await Person("Anna");
        var p = await Project("PRJ");
        await PlanTask(p, anna, 80);
        Assert.Equal(LoadBand.UnderUsed, (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single().Band);

        await new SettingsStore(_db).SetAsync(SettingKeys.LoadUnderUsedBelow, SettingScope.Global, "0.3", D(1, 1), "t", "Lower the under-used line");
        Assert.Equal(LoadBand.Healthy, (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single().Band);
    }

    [Fact]
    public async Task Manual_Monthly_Allocation_Is_Used_When_There_Is_No_Plan()
    {
        var anna = await Person("Anna");
        var p = await Project("PRJ");
        _db.Allocations.Add(new Allocation { PersonId = anna.Id, ProjectId = p.Id, Month = D(10, 1), Hours = 100, Status = "Confirmed" });
        _db.Allocations.Add(new Allocation { PersonId = anna.Id, ProjectId = p.Id, Month = D(10, 1), Hours = 50, Status = "Proposed" });
        await _db.SaveChangesAsync();

        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single();
        Assert.Equal(100m, row.AllocatedHours);
        Assert.Equal(50m, row.ProposedHours);
        Assert.Equal("manual", row.ByProject.Single().Source);

        await new SettingsStore(_db).SetAsync(SettingKeys.ProposedCountsTowardLoad, SettingScope.Global, "true", D(1, 1), "t", "Include proposals");
        var withProposed = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single();
        Assert.Equal(Math.Round(150m / withProposed.PlannedHours, 4), withProposed.LoadVsPlanned);
    }

    [Fact]
    public async Task Plan_Assignments_Win_Over_Manual_Rows_For_The_Same_Person_And_Project()
    {
        var anna = await Person("Anna");
        var p = await Project("PRJ");
        await PlanTask(p, anna, 80);
        _db.Allocations.Add(new Allocation { PersonId = anna.Id, ProjectId = p.Id, Month = D(10, 1), Hours = 100, Status = "Confirmed" });
        await _db.SaveChangesAsync();

        var row = (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), personId: anna.Id)).Single();
        Assert.Equal(80m, row.AllocatedHours);
    }

    [Fact]
    public async Task Weekly_Load_Splits_Hours_By_Monday_Weeks_And_Flags_Overload()
    {
        var anna = await Person("Anna");
        var p1 = await Project("P1"); var p2 = await Project("P2");
        await PlanTask(p1, anna, 48);
        await PlanTask(p2, anna, 48);
        var weeks = await _capacity.GetWeeklyLoadAsync(D(10, 5), D(10, 18), personId: anna.Id);

        Assert.Equal(2, weeks.Count);
        Assert.Equal(D(10, 5), weeks[0].WeekStart);
        Assert.Equal(48m, weeks[0].CapacityHours);
        Assert.Equal(96m, weeks[0].AllocatedHours);
        Assert.Equal(LoadBand.OverAllocated, weeks[0].Band);
        Assert.Equal(0m, weeks[1].AllocatedHours);
    }

    [Fact]
    public async Task Inactive_People_And_Team_Filters_Are_Respected()
    {
        var team = new Team { Name = "LPP" };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        var inTeam = await Person("In team");
        inTeam.TeamId = team.Id;
        var other = await Person("Other");
        var gone = await Person("Gone");
        gone.IsActive = false;
        await _db.SaveChangesAsync();

        Assert.Equal(["In team", "Other"], (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31))).Select(r => r.PersonName).OrderBy(x => x));
        Assert.Equal(["In team"], (await _capacity.GetCapacityAsync(D(10, 1), D(10, 31), teamId: team.Id)).Select(r => r.PersonName));
        _ = other;
    }

    [Fact]
    public async Task Multiple_Months_Return_One_Row_Per_Person_Per_Month()
    {
        await Person("Anna");
        var rows = await _capacity.GetCapacityAsync(D(10, 1), D(12, 31));
        Assert.Equal([D(10, 1), D(11, 1), D(12, 1)], rows.Select(r => r.Month));
    }
}
