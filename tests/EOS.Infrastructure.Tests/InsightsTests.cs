using EOS.Domain.Capacity;
using EOS.Domain.Hosting;
using EOS.Domain.People;
using EOS.Domain.Planning;
using EOS.Domain.Projects;
using EOS.Domain.Settings;
using EOS.Domain.Skills;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Insights;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class InsightsTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly PlanService _plans;
    private static DateOnly D(int m, int d) => new(2026, m, d);
    private static readonly DateOnly Today = D(10, 14);

    public InsightsTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        _plans = new PlanService(_db);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<Person> Person(string name, decimal avail = 1m)
    {
        var p = new Person { Code = "E" + (_db.People.Count() + 1), FullName = name, AvailabilityFraction = avail };
        _db.People.Add(p); await _db.SaveChangesAsync();
        return p;
    }

    private async Task<Skill> Skill(string code)
    {
        var s = await _db.Skills.SingleOrDefaultAsync(x => x.Code == code);
        if (s is null) { s = new Skill { Code = code, Name = code }; _db.Skills.Add(s); await _db.SaveChangesAsync(); }
        return s;
    }

    private async Task Rate(Person p, Skill s, int rating)
    {
        _db.PersonSkills.Add(new PersonSkill { PersonId = p.Id, SkillId = s.Id, Rating = rating });
        await _db.SaveChangesAsync();
    }

    private async Task<Project> Project(string no, DateOnly? plannedEnd = null, string? tracker = "Under Development")
    {
        var p = new Project { ProjectNo = no, Name = "Project " + no, PoStart = D(9, 1), PlannedEnd = plannedEnd, TrackerStatus = tracker };
        _db.Projects.Add(p); await _db.SaveChangesAsync();
        return p;
    }

    // ---- demand ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Demand_Status_Is_Computed_From_Confirmed_Allocations_And_Can_Be_Overridden()
    {
        var sql = await Skill("SQL"); var anna = await Person("Anna"); await Rate(anna, sql, 4);
        var p = await Project("PRJ");
        var svc = new DemandService(_db);
        var d = await svc.AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 20, StartDate = D(10, 1), EndDate = D(10, 31) }, "t");

        Assert.Equal("Open", (await svc.ListAsync(p.Id)).Single().Status);

        _db.Allocations.Add(new Allocation { PersonId = anna.Id, ProjectId = p.Id, DemandId = d.Id, Month = D(10, 1), Hours = 45, Status = "Confirmed" });   // ~10 h/week
        await _db.SaveChangesAsync();
        var partial = (await svc.ListAsync(p.Id)).Single();
        Assert.Equal("Partially Filled", partial.Status);
        Assert.Equal(1, partial.EligibleCount);
        Assert.Equal("Anna", partial.BestPerson);

        _db.Allocations.Add(new Allocation { PersonId = anna.Id, ProjectId = p.Id, DemandId = d.Id, Month = D(10, 1), Hours = 90, Status = "Confirmed" });
        await _db.SaveChangesAsync();
        Assert.Equal("Filled", (await svc.ListAsync(p.Id)).Single().Status);

        d.StatusOverride = "Gap - Escalated";
        await svc.UpdateAsync(d, "t");
        Assert.Equal("Gap - Escalated", (await svc.ListAsync(p.Id)).Single().Status);
    }

    [Fact]
    public async Task Demand_Validation_Uses_The_Level_Count_Setting()
    {
        var sql = await Skill("SQL"); var p = await Project("PRJ");
        var svc = new DemandService(_db);
        await Assert.ThrowsAsync<DemandException>(() => svc.AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 6, HoursPerWeek = 10, StartDate = D(10, 1), EndDate = D(10, 31) }, "t"));
        await new SettingsStore(_db).SetAsync(SettingKeys.SkillLevelCount, SettingScope.Global, "7", D(1, 1), "t", "7 levels");
        await svc.AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 6, HoursPerWeek = 10, StartDate = D(10, 1), EndDate = D(10, 31) }, "t");

        await Assert.ThrowsAsync<DemandException>(() => svc.AddAsync(new Demand { SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 10, StartDate = D(10, 1), EndDate = D(10, 31) }, "t"));
        await Assert.ThrowsAsync<DemandException>(() => svc.AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 10, StartDate = D(10, 9), EndDate = D(10, 1) }, "t"));
        await Assert.ThrowsAsync<DemandException>(() => svc.AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 10, StartDate = D(10, 1), EndDate = D(10, 31), StatusOverride = "Bogus" }, "t"));
    }

    [Fact]
    public async Task Candidates_Prefer_Least_Sufficient_Skill_Then_Free_Capacity_And_Show_Reasons()
    {
        var sql = await Skill("SQL");
        var expert = await Person("Expert"); await Rate(expert, sql, 5);
        var fit = await Person("Fit"); await Rate(fit, sql, 3);
        var low = await Person("Low"); await Rate(low, sql, 2);                // below the gate: never a candidate
        var p = await Project("PRJ");
        var d = await new DemandService(_db).AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 20, StartDate = D(11, 2), EndDate = D(11, 30) }, "t");

        var ranked = await new DemandService(_db).RankCandidatesAsync(d.Id);
        Assert.Equal(["Fit", "Expert"], ranked.Select(c => c.Name));              // protect the level-5 expert for harder work
        Assert.Contains(ranked[1].Reasons, r => r.Contains("above the required 3"));
        Assert.DoesNotContain(ranked, c => c.Name == "Low");
    }

    [Fact]
    public async Task Candidate_Already_On_The_Project_Gets_A_Continuity_Bonus_And_Leave_Is_Penalised()
    {
        var sql = await Skill("SQL");
        var a = await Person("OnProject"); await Rate(a, sql, 3);
        var b = await Person("Away"); await Rate(b, sql, 3);
        var c = await Person("Free"); await Rate(c, sql, 3);
        var p = await Project("PRJ");
        var t = await _plans.AddTaskAsync(p.Id, "Existing", "t", workHours: 8);
        await _plans.AssignAsync(t.Id, a.Id, 0.1m, "t");
        await _plans.RecomputeAsync(p.Id, D(10, 5));
        _db.Leaves.Add(new Leave { PersonId = b.Id, FromDate = D(11, 2), ToDate = D(11, 28) });
        await _db.SaveChangesAsync();
        var d = await new DemandService(_db).AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 10, StartDate = D(11, 2), EndDate = D(11, 30) }, "t");

        var ranked = await new DemandService(_db).RankCandidatesAsync(d.Id);
        Assert.Equal("OnProject", ranked[0].Name);
        Assert.True(ranked[0].AlreadyOnProject);
        Assert.Equal("Away", ranked[^1].Name);
        Assert.True(ranked[^1].LeaveFraction > 0.5m);
    }

    // ---- pipeline and supply vs demand -----------------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_Weighted_Hours_And_Conversion_To_A_Project()
    {
        var sql = await Skill("SQL");
        var pipeline = new PipelineService(_db);
        var o = await pipeline.AddAsync(new Opportunity { Code = "UP1", Name = "Project A", ExpectedHours = 704, WinProbability = 0.7m, Stage = "Proposal Sent", RequiredSkills = "SQL", LikelyBookingMonth = D(11, 1), DurationMonths = 2 }, "t");
        Assert.Equal(492.8m, (await pipeline.ListAsync()).Single().WeightedHours);        // 704 x 0.7

        await new DemandService(_db).AddAsync(new Demand { OpportunityId = o.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 40, StartDate = D(11, 1), EndDate = D(12, 31) }, "t");
        var project = await pipeline.ConvertToProjectAsync(o.Id, "PRO-NEW", "t");

        Assert.Equal("Entered", project.TrackerStatus);
        Assert.Equal(project.Id, (await _db.Demands.SingleAsync()).ProjectId);            // demand lines move to the project
        Assert.Equal("Won", (await _db.Opportunities.SingleAsync()).Status);
        await Assert.ThrowsAsync<DemandException>(() => pipeline.ConvertToProjectAsync(o.Id, "PRO-NEW2", "t"));
        await Assert.ThrowsAsync<DemandException>(() => pipeline.AddAsync(new Opportunity { Code = "UP1", Name = "dup" }, "t"));
        await Assert.ThrowsAsync<DemandException>(() => pipeline.AddAsync(new Opportunity { Code = "UP9", Name = "x", WinProbability = 1.5m }, "t"));
    }

    [Fact]
    public async Task Supply_Demand_Shows_The_Gap_When_Demand_Exceeds_Pooled_Free_Hours()
    {
        var sql = await Skill("SQL");
        var anna = await Person("Anna", 0.5m); await Rate(anna, sql, 4);              // ~92 planned h free in November
        var p = await Project("PRJ");
        await new DemandService(_db).AddAsync(new Demand { ProjectId = p.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 40, StartDate = D(11, 1), EndDate = D(11, 30) }, "t");

        var row = (await new PipelineService(_db).SupplyDemandAsync(D(11, 1), 1)).Single(r => r.SkillId == sql.Id);
        Assert.Equal(Math.Round(40m * 30 / 7, 1), row.ProjectDemandHours);               // 40 h/week over 30 days
        Assert.Equal(1, row.DeployableCount);
        Assert.True(row.SupplyHours < row.ProjectDemandHours);
        Assert.Equal(Math.Round(row.ProjectDemandHours + row.PipelineHours - row.SupplyHours, 1), row.GapHours);
        Assert.True(row.GapHours > 0);
    }

    [Fact]
    public async Task Pipeline_Without_Demand_Lines_Spreads_Weighted_Hours_Over_Months_And_Skills()
    {
        var sql = await Skill("SQL"); await Skill("HIST");
        await new PipelineService(_db).AddAsync(new Opportunity { Code = "UP2", Name = "B", ExpectedHours = 1200, WinProbability = 0.5m, RequiredSkills = "SQL,HIST", LikelyBookingMonth = D(11, 1), DurationMonths = 3 }, "t");

        var rows = await new PipelineService(_db).SupplyDemandAsync(D(11, 1), 3);
        Assert.Equal(100m, rows.Single(r => r.SkillId == sql.Id && r.Month == D(11, 1)).PipelineHours);      // 1200 x 0.5 / 3 months / 2 skills
        Assert.Equal(100m, rows.Single(r => r.SkillId == sql.Id && r.Month == D(1 + 12 - 0 > 12 ? 1 : 1, 1) || r.SkillId == sql.Id && r.Month == D(12, 1)).PipelineHours);
    }

    // ---- health --------------------------------------------------------------------------------------------------

    private async Task<Project> ProjectWithDatedTasks(string no, DateOnly plannedEnd, int lateTasks, int doneTasks, int okOpenTasks = 0)
    {
        var p = await Project(no, plannedEnd);
        for (var i = 0; i < okOpenTasks; i++)
            _db.Tasks.Add(new PlanTask { ProjectId = p.Id, Name = $"ok {i}", SortOrder = 200 + i, PlannedStart = D(10, 1), PlannedFinish = D(12, 1), PercentComplete = 0m, Status = "Pending", ScheduledStart = D(10, 1), ScheduledFinish = D(11, 20) });
        for (var i = 0; i < doneTasks; i++)
            _db.Tasks.Add(new PlanTask { ProjectId = p.Id, Name = $"done {i}", SortOrder = i + 1, PlannedStart = D(9, 1), PlannedFinish = D(9, 10), PercentComplete = 1m, Status = "Completed", ScheduledStart = D(9, 1), ScheduledFinish = D(9, 10) });
        for (var i = 0; i < lateTasks; i++)
            _db.Tasks.Add(new PlanTask { ProjectId = p.Id, Name = $"late {i}", SortOrder = 100 + i, PlannedStart = D(9, 11), PlannedFinish = D(9, 30), PercentComplete = 0m, Status = "Delayed", ScheduledStart = D(10, 14), ScheduledFinish = D(11, 20) });
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task Health_Is_Computed_From_Variance_Delays_And_Progress_Gap()
    {
        var bad = await ProjectWithDatedTasks("BAD", D(10, 1), lateTasks: 3, doneTasks: 1);
        var good = await Project("GOOD", D(12, 31));
        _db.Tasks.Add(new PlanTask { ProjectId = good.Id, Name = "on time", SortOrder = 1, PlannedStart = D(10, 1), PlannedFinish = D(12, 20), PercentComplete = 0.2m, Status = "In Progress", ScheduledStart = D(10, 1), ScheduledFinish = D(12, 20) });
        await _db.SaveChangesAsync();

        var health = new HealthService(_db);
        var b = await health.GetAsync(bad.Id, Today);
        Assert.Equal("At Risk", b.Health);
        Assert.Equal(3, b.DelayedTasks);
        Assert.True(b.ScheduleVarianceDays > 20);
        Assert.Contains(b.Reasons, r => r.Contains("late"));
        Assert.Equal("On Track", (await health.GetAsync(good.Id, Today)).Health);
        Assert.Equal("BAD", (await health.GetAllAsync(Today)).First().ProjectNo);      // worst first
    }

    [Fact]
    public async Task Health_Thresholds_And_Overrides_Are_Configurable()
    {
        var p = await ProjectWithDatedTasks("MID", D(11, 20), lateTasks: 2, doneTasks: 20, okOpenTasks: 8);
        var health = new HealthService(_db);
        Assert.Equal("Moderate Risk", (await health.GetAsync(p.Id, Today)).Health);        // 2 of 10 open tasks late (20%)

        await new SettingsStore(_db).SetAsync(SettingKeys.HealthAtRiskDelayRatio, SettingScope.Global, "0.9", D(1, 1), "t", "tolerate more");
        await new SettingsStore(_db).SetAsync(SettingKeys.HealthModerateDelayRatio, SettingScope.Global, "0.95", D(1, 1), "t", "tolerate more");
        await new SettingsStore(_db).SetAsync(SettingKeys.HealthModerateVarianceDays, SettingScope.Global, "400", D(1, 1), "t", "tolerate more");
        await new SettingsStore(_db).SetAsync(SettingKeys.HealthModerateProgressGapPoints, SettingScope.Global, "100", D(1, 1), "t", "tolerate more");
        Assert.Equal("On Track", (await health.GetAsync(p.Id, Today)).Health);

        var entity = await _db.Projects.SingleAsync();
        entity.HealthOverride = "At Risk"; entity.HealthOverrideReason = "Customer escalation";
        await _db.SaveChangesAsync();
        var overridden = await health.GetAsync(p.Id, Today);
        Assert.Equal(("At Risk", true, "Customer escalation"), (overridden.Health, overridden.IsOverride, overridden.OverrideReason));
    }

    // ---- alerts -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Alerts_Cover_Capacity_Demand_Infrastructure_And_Data_Quality()
    {
        var sql = await Skill("SQL");
        var anna = await Person("Anna", 0.5m); await Rate(anna, sql, 2);               // nobody reaches level 3 -> skill gap
        var p1 = await Project("P1"); var p2 = await Project("P2");
        p1.PoStart = p2.PoStart = D(10, 5);                                           // both run in October
        await _db.SaveChangesAsync();
        foreach (var p in new[] { p1, p2 })
        {
            var t = await _plans.AddTaskAsync(p.Id, "Work", "t", workHours: 96);
            await _plans.AssignAsync(t.Id, anna.Id, 1m, "t");
            await _plans.RecomputeAsync(p.Id, D(10, 5));
        }
        await new DemandService(_db).AddAsync(new Demand { ProjectId = p1.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 20, StartDate = D(10, 20), EndDate = D(12, 31) }, "t");

        var host = new Host { Kind = "physical", Ip = "10.0.0.1", Name = "Base" };
        var vm = new Host { Kind = "vm", Name = "OLD_VM", State = "Off", Purpose = "Unknown", MarkedForDeletion = true };
        var vm2 = new Host { Kind = "vm", Name = "NEED_BACKUP", State = "Running", NeedBackup = true };
        _db.Hosts.AddRange(host, vm, vm2); await _db.SaveChangesAsync();
        _db.HostDisks.Add(new HostDisk { HostId = host.Id, Drive = "C", FreeGb = 1, TotalGb = 100, CapturedOn = Today });
        _db.PersonAliases.Add(new PersonAlias { Alias = "XX", Source = "tracker", Kind = AliasKind.Initials });
        await _db.SaveChangesAsync();

        var alerts = await new AlertService(_db).EvaluateAsync(Today);
        string[] rules = [.. alerts.Select(a => a.Rule).Distinct().OrderBy(x => x)];

        Assert.Contains("A01", rules);      // Anna: 192 h in October against 108 available
        Assert.Contains("A04", rules);      // no engineer is eligible for SQL >= 3
        Assert.Contains("A05", rules);      // bus factor
        Assert.DoesNotContain("A12", rules);   // two concurrent projects is within the default limit of 2
        Assert.Contains("A17", rules);      // 1% free disk
        Assert.Contains("A19", rules);      // reclaim
        Assert.Contains("A20", rules);      // needs backup, none recorded
        Assert.Contains("A21", rules);      // unresolved alias
        Assert.Equal(AlertSeverity.Critical, alerts.First().Severity);                   // sorted worst first
        Assert.Equal("A04", alerts.First().Rule);
    }

    [Fact]
    public async Task Approved_Unapplied_Scenarios_And_Stale_Decisions_Raise_Governance_Alerts()
    {
        _db.Scenarios.AddRange(
            new Scenario { Name = "waiting", Status = "Analysed", CreatedBy = "t", CreatedUtc = new DateTime(2026, 10, 1) },
            new Scenario { Name = "approved", Status = "Approved", CreatedBy = "t", CreatedUtc = new DateTime(2026, 10, 13) });
        await _db.SaveChangesAsync();

        var alerts = await new AlertService(_db).EvaluateAsync(Today);
        Assert.Contains(alerts, a => a.Rule == "A11" && a.Subject == "waiting");
        Assert.Contains(alerts, a => a.Rule == "A10" && a.Subject == "approved");
    }

    [Fact]
    public async Task Alert_Thresholds_Come_From_Settings()
    {
        var sql = await Skill("SQL");
        for (var i = 0; i < 4; i++) { var p = await Person("P" + i); await Rate(p, sql, 3); }
        var proj = await Project("PRJ");
        await new DemandService(_db).AddAsync(new Demand { ProjectId = proj.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 10, StartDate = D(10, 20), EndDate = D(12, 31) }, "t");

        Assert.DoesNotContain(await new AlertService(_db).EvaluateAsync(Today), a => a.Rule == "A05");     // 4 deployable > default limit 3
        await new SettingsStore(_db).SetAsync(SettingKeys.AlertBusFactorMax, SettingScope.Global, "5", D(1, 1), "t", "stricter");
        Assert.Contains(await new AlertService(_db).EvaluateAsync(Today), a => a.Rule == "A05");
    }

    // ---- dashboard ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Command_Center_Aggregates_Kpis_Health_Alerts_Load_And_Supply_Demand()
    {
        var sql = await Skill("SQL");
        var anna = await Person("Anna"); await Rate(anna, sql, 4);
        var p = await ProjectWithDatedTasks("DASH", D(10, 1), lateTasks: 2, doneTasks: 1);
        _db.Hosts.AddRange(new Host { Kind = "vm", Name = "A", State = "Running" }, new Host { Kind = "vm", Name = "B", State = "Off" });
        _db.ChangeGapLog.Add(new ChangeGapEntry { RaisedBy = "t", RaisedOn = Today, Description = "gap", Status = "Open" });
        await _db.SaveChangesAsync();

        var cc = await new DashboardService(_db).GetAsync(Today);

        Assert.Equal(1, cc.Kpis.ActiveProjects);
        Assert.Equal(1, cc.Kpis.AtRiskProjects);
        Assert.Equal(2, cc.Kpis.DelayedTasks);
        Assert.Equal((1, 1, 1), (cc.Kpis.VmsRunning, cc.Kpis.VmsOff, cc.Kpis.OpenGaps));
        Assert.Equal("DASH", cc.Projects.Single().ProjectNo);
        Assert.Equal(8, cc.LoadNext8Weeks.Count);
        Assert.Equal(3, cc.SupplyDemand.Count(r => r.SkillId == sql.Id));                 // 3 months
        Assert.Contains(cc.Alerts, a => a.Rule == "A06");
        _ = p;
    }
}
