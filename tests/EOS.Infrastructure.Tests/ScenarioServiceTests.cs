using EOS.Domain.People;
using EOS.Domain.Projects;
using EOS.Domain.Skills;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class ScenarioServiceTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly PlanService _plans;
    private readonly ScenarioService _scenarios;
    private readonly Dictionary<string, Person> _people = [];
    private Project _p1 = null!, _p2 = null!, _p3 = null!;
    private static DateOnly D(int m, int d) => new(2026, m, d);
    private static readonly DateOnly Start = new(2026, 10, 5);

    public ScenarioServiceTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        _plans = new PlanService(_db);
        _scenarios = new ScenarioService(_db);
        // The verified S3 examples assume a full 8 h Saturday; the shipped default calendar now has a 4.5 h Saturday (owner, 21 Oct).
        foreach (var c in _db.Calendars) c.DayHours = null;
        _db.SaveChanges();
        BuildGoldenAsync().GetAwaiter().GetResult();
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<Person> Person(string name, int sqlRating)
    {
        var p = new Person { Code = "E" + (_people.Count + 1), FullName = name };
        _db.People.Add(p);
        await _db.SaveChangesAsync();
        var sql = await _db.Skills.SingleOrDefaultAsync(s => s.Code == "SQL");
        if (sql is null) { sql = new Skill { Code = "SQL", Name = "SQL" }; _db.Skills.Add(sql); await _db.SaveChangesAsync(); }
        _db.PersonSkills.Add(new PersonSkill { PersonId = p.Id, SkillId = sql.Id, Rating = sqlRating });
        await _db.SaveChangesAsync();
        _people[name] = p;
        return p;
    }

    private async Task<Project> NewProject(string no)
    {
        var p = new Project { ProjectNo = no, Name = no, PoStart = Start };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<int> Task(Project p, string name, decimal work, params string[] crew)
    {
        var t = await _plans.AddTaskAsync(p.Id, name, "t", workHours: work);
        foreach (var who in crew) await _plans.AssignAsync(t.Id, _people[who].Id, 1m, "t");
        return t.Id;
    }

    private async Task BuildGoldenAsync()
    {
        foreach (var (n, r) in new[] { ("Mahesh", 5), ("Lalji", 3), ("Twinkle", 3), ("Chirag", 5), ("Deep", 3), ("Sagar", 5), ("Priyanka", 2), ("Dharati", 5) })
            await Person(n, r);
        _p1 = await NewProject("P1"); _p2 = await NewProject("P2"); _p3 = await NewProject("P3");

        var a11 = await Task(_p1, "1.1", 96, "Mahesh"); var a12 = await Task(_p1, "1.2", 192, "Mahesh", "Lalji");
        var a13 = await Task(_p1, "1.3", 144, "Twinkle"); var a14 = await Task(_p1, "1.4", 48, "Mahesh", "Lalji");
        var a15 = await Task(_p1, "1.5", 96, "Mahesh", "Lalji");
        await _plans.AddLinkAsync(a11, a12, 0, 0, "t"); await _plans.AddLinkAsync(a11, a13, 0, 0, "t");
        await _plans.AddLinkAsync(a12, a14, 0, 0, "t"); await _plans.AddLinkAsync(a13, a14, 0, 0, "t"); await _plans.AddLinkAsync(a14, a15, 0, 0, "t");

        var b21 = await Task(_p2, "2.1", 80, "Chirag"); var b22 = await Task(_p2, "2.2", 480, "Chirag", "Deep");
        var b23 = await Task(_p2, "2.3", 160, "Deep"); var b24 = await Task(_p2, "2.4", 48, "Chirag", "Deep");
        await _plans.AddLinkAsync(b21, b22, 0, 0, "t"); await _plans.AddLinkAsync(b22, b23, 0, 0, "t"); await _plans.AddLinkAsync(b23, b24, 0, 0, "t");

        var c31 = await Task(_p3, "3.1", 64, "Sagar"); var c32 = await Task(_p3, "3.2", 320, "Priyanka", "Dharati");
        var c33 = await Task(_p3, "3.3", 160, "Sagar"); var c34 = await Task(_p3, "3.4", 32, "Sagar", "Priyanka");
        await _plans.AddLinkAsync(c31, c32, 0, 0, "t"); await _plans.AddLinkAsync(c31, c33, 0, 0, "t");
        await _plans.AddLinkAsync(c32, c34, 0, 0, "t"); await _plans.AddLinkAsync(c33, c34, 0, 0, "t");

        var sql = await _db.Skills.SingleAsync(s => s.Code == "SQL");
        _db.Demands.Add(new EOS.Domain.Capacity.Demand { ProjectId = _p2.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 40, StartDate = Start, EndDate = D(12, 31) });
        await _db.SaveChangesAsync();

        foreach (var p in new[] { _p1, _p2, _p3 }) await _plans.RecomputeAsync(p.Id, Start);
    }

    private MoveInput Move(string who, Project from, Project to, DateOnly on, decimal pct = 1m) => new(_people[who].Id, from.Id, to.Id, on, pct);

    private static ProjectImpactView Impact(ScenarioView v, string no) => v.Projects.Single(p => p.ProjectNo == no);

    [Fact]
    public async Task Scenario1_Move_Mahesh_26_Oct_Without_Replacement()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(10, 26))], "Move Mahesh", "t", today: Start);

        Assert.Equal("Analysed", v.Status);
        Assert.Equal((D(11, 18), D(11, 28), 9), (Impact(v, "P1").BaselineFinish, Impact(v, "P1").ScenarioFinish, Impact(v, "P1").SlipDays));
        Assert.Equal((D(12, 16), D(11, 25), -18), (Impact(v, "P2").BaselineFinish, Impact(v, "P2").ScenarioFinish, Impact(v, "P2").SlipDays));
        Assert.DoesNotContain(v.Projects, p => p.ProjectNo == "P3");
        Assert.True(v.RequiresLeadershipApproval);
        Assert.Contains(v.Verdict, l => l.Contains("P1 finishes 9 working day(s) later"));
        Assert.Contains(v.Verdict, l => l.Contains("P2 finishes 18 working day(s) earlier"));
        Assert.Contains(Impact(v, "P1").Tasks, t => t.Name == "1.2" && t.SlipDays == 6);
    }

    [Fact]
    public async Task Scenario_Analysis_Does_Not_Change_Any_Plan()
    {
        var before = await _db.TaskAssignments.CountAsync();
        var finishBefore = (await _plans.GetPlanAsync(_p1.Id)).Finish;
        await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(10, 26))], "x", "t", today: Start);
        Assert.Equal(before, await _db.TaskAssignments.CountAsync());
        Assert.Equal(finishBefore, (await _plans.GetPlanAsync(_p1.Id)).Finish);
        Assert.All(await _db.TaskAssignments.ToListAsync(), a => Assert.Null(a.ToDate));
    }

    [Fact]
    public async Task Alternative_Move_On_19_Nov_Is_Free_For_P1_And_Gains_Eleven_On_P2()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(11, 19))], "Later move", "t", today: Start);
        Assert.Equal(0, Impact(v, "P1").SlipDays);
        Assert.Equal((D(12, 3), -11), (Impact(v, "P2").ScenarioFinish, Impact(v, "P2").SlipDays));
        Assert.False(v.RequiresLeadershipApproval is true && Impact(v, "P1").SlipDays != 0);
    }

    [Fact]
    public async Task Skill_Gate_Produces_A_Warning_For_An_Ineligible_Engineer()
    {
        var v = await _scenarios.AnalyseAsync([Move("Priyanka", _p3, _p2, D(10, 26))], "Priyanka to P2", "t", today: Start);
        Assert.Contains(v.Warnings, w => w.Contains("Priyanka") && w.Contains("not eligible (skill)") && w.Contains("SQL 2 < 3"));
    }

    [Fact]
    public async Task Scenario_Input_Is_Validated()
    {
        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.AnalyseAsync([], "empty", "t"));
        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.AnalyseAsync([new MoveInput(_people["Mahesh"].Id, 9999, _p2.Id, D(10, 26))], "bad", "t"));
        var empty = await NewProject("EMPTY");
        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.AnalyseAsync([Move("Mahesh", _p1, empty, D(10, 26))], "no plan", "t", today: Start));
    }

    [Fact]
    public async Task Release_Analysis_Matches_The_Source_Table()
    {
        var rows = await _scenarios.ReleaseAnalysisAsync(_p2.Id, D(10, 26), Start);
        ReleaseRowView Row(string n) => rows.Single(r => r.PersonName == n);

        Assert.Equal((D(11, 7), D(10, 26), 0), (Row("Twinkle").LastDayOnCurrentWork, Row("Twinkle").EarliestNoDelayMove!.Value, Row("Twinkle").CostIfMovedOnAnalysisDate));
        Assert.Equal((D(11, 5), D(11, 6), 10), (Row("Dharati").LastDayOnCurrentWork, Row("Dharati").EarliestNoDelayMove!.Value, Row("Dharati").CostIfMovedOnAnalysisDate));
        Assert.Equal((D(11, 7), D(11, 9), 7), (Row("Sagar").LastDayOnCurrentWork, Row("Sagar").EarliestNoDelayMove!.Value, Row("Sagar").CostIfMovedOnAnalysisDate));
        Assert.Equal((D(11, 18), D(11, 19), 9), (Row("Mahesh").LastDayOnCurrentWork, Row("Mahesh").EarliestNoDelayMove!.Value, Row("Mahesh").CostIfMovedOnAnalysisDate));
        Assert.Equal((D(11, 18), D(11, 19), 9), (Row("Lalji").LastDayOnCurrentWork, Row("Lalji").EarliestNoDelayMove!.Value, Row("Lalji").CostIfMovedOnAnalysisDate));

        Assert.False(Row("Priyanka").Eligible);                                           // SQL 2 < 3: "Not eligible (skill)"
        Assert.All(new[] { "Twinkle", "Dharati", "Sagar", "Mahesh", "Lalji" }, n =>
        {
            Assert.True(Row(n).Eligible);
            Assert.Equal(-18, Row(n).GainToTargetIfMovedOnAnalysisDate);
        });
        Assert.DoesNotContain(rows, r => r.PersonName is "Chirag" or "Deep");
    }

    [Fact]
    public async Task Push_Plan_Picks_Twinkle_Dharati_Sagar_And_Finishes_P2_On_18_Nov()
    {
        var plan = await _scenarios.PushPlanAsync(_p2.Id, D(10, 26), today: Start);

        Assert.Equal(D(12, 16), plan.BaselineFinish);
        Assert.Equal(["Twinkle", "Dharati", "Sagar"], plan.Steps.Select(s => s.PersonName));
        Assert.Equal([D(10, 26), D(11, 6), D(11, 9)], plan.Steps.Select(s => s.Date));
        Assert.Equal(D(11, 18), plan.Steps[^1].TargetFinish);
        Assert.Equal(-24, plan.Steps[^1].TargetSlipDays);
        Assert.DoesNotContain(plan.Steps, s => s.PersonName is "Mahesh" or "Lalji" or "Priyanka");

        var v = await _scenarios.AnalyseAsync(plan.Moves, "Push P2", "t", today: Start);
        Assert.Equal((D(11, 18), -24), (Impact(v, "P2").ScenarioFinish, Impact(v, "P2").SlipDays));
        Assert.Equal(0, Impact(v, "P1").SlipDays);
        Assert.Equal(0, Impact(v, "P3").SlipDays);
    }

    [Fact]
    public async Task Apply_Requires_An_Approved_Scenario()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(10, 26))], "Move Mahesh", "t", today: Start);
        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.ApplyAsync(v.Id, "snehil", D(10, 26)));
        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.DecideAsync(v.Id, true, "", null));

        var rejected = await _scenarios.DecideAsync(v.Id, false, "snehil", "Not now");
        Assert.Equal("Rejected", rejected.Status);
        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.ApplyAsync(v.Id, "snehil", D(10, 26)));
    }

    [Fact]
    public async Task Applying_A_Scenario_Updates_Plans_Ledger_Log_And_Baselines()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(10, 26))], "Move Mahesh to P2", "t", today: Start);
        await _scenarios.DecideAsync(v.Id, true, "snehil", "Project 2 is the priority");
        var applied = await _scenarios.ApplyAsync(v.Id, "snehil", D(10, 26));
        Assert.Equal("Applied", applied.Status);

        var mahesh = _people["Mahesh"].Id;
        var p1Tasks = await _db.Tasks.Where(t => t.ProjectId == _p1.Id).Select(t => t.Id).ToListAsync();
        Assert.All(await _db.TaskAssignments.Where(a => a.PersonId == mahesh && p1Tasks.Contains(a.TaskId)).ToListAsync(), a => Assert.Equal(D(10, 25), a.ToDate));
        var p2Tasks = await _db.Tasks.Where(t => t.ProjectId == _p2.Id && t.Name != "2.1").Select(t => t.Id).ToListAsync();
        Assert.Equal(3, await _db.TaskAssignments.CountAsync(a => a.PersonId == mahesh && p2Tasks.Contains(a.TaskId) && a.FromDate == D(10, 26)));

        Assert.Equal(D(11, 28), (await _plans.GetPlanAsync(_p1.Id)).Finish);
        Assert.Equal(D(11, 25), (await _plans.GetPlanAsync(_p2.Id)).Finish);

        var move = await _db.Movements.SingleAsync();
        Assert.Equal(("Executed", mahesh, v.Id), (move.Status, move.PersonId, move.ScenarioId));
        var log = await _db.ChangeGapLog.SingleAsync();
        Assert.Equal(("Decision", "snehil", 18m), (log.Kind, log.Approver, log.ImpactDays));
        Assert.Equal(2, await _db.Baselines.CountAsync(b => b.Name.StartsWith("After scenario")));
        Assert.Equal("Applied", (await _scenarios.GetAsync(v.Id)).Status);

        await Assert.ThrowsAsync<ScenarioException>(() => _scenarios.ApplyAsync(v.Id, "snehil", D(10, 26)));
    }

    [Fact]
    public async Task A_Future_Dated_Move_Is_Recorded_As_Approved_Not_Executed()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(11, 19))], "Later", "t", today: Start);
        await _scenarios.DecideAsync(v.Id, true, "snehil", null);
        await _scenarios.ApplyAsync(v.Id, "snehil", today: D(10, 14));
        Assert.Equal("Approved", (await _db.Movements.SingleAsync()).Status);
    }

    [Fact]
    public async Task Scenarios_Are_Listed_And_Can_Be_Reanalysed_Until_Applied()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(10, 26))], "A", "t", today: Start);
        await _scenarios.DecideAsync(v.Id, true, "snehil", null);
        var again = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(11, 19))], "A v2", "t", today: Start, scenarioId: v.Id);
        Assert.Equal("Analysed", again.Status);
        Assert.Null(again.DecisionBy);
        Assert.Single(await _scenarios.ListAsync());
    }

    [Fact]
    public async Task Every_Scenario_Step_Is_Audited()
    {
        var v = await _scenarios.AnalyseAsync([Move("Mahesh", _p1, _p2, D(10, 26))], "Audit me", "t", today: Start);
        await _scenarios.DecideAsync(v.Id, true, "snehil", null);
        await _scenarios.ApplyAsync(v.Id, "snehil", D(10, 26));
        var actions = await _db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("scenario.analyse", actions);
        Assert.Contains("scenario.approve", actions);
        Assert.Contains("scenario.apply", actions);
    }
}
