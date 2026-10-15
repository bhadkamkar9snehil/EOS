using EOS.Domain.Configuration;
using EOS.Domain.Hosting;
using EOS.Domain.People;
using EOS.Domain.Training;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.Operations;
using EOS.Infrastructure.People;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Security;
using EOS.Infrastructure.Training;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class AdminAndOpsTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private static DateOnly D(int m, int d) => new(2026, m, d);

    public AdminAndOpsTests() => (_db, _connection) = EosDatabase.InMemory();

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    [Fact]
    public async Task Tokens_Are_Stored_Hashed_Validated_And_Revocable()
    {
        var svc = new TokenService(_db);
        var token = await svc.CreateAsync("snehil", "bootstrap");

        Assert.StartsWith("eos_", token);
        Assert.DoesNotContain(token, await _db.ApiTokens.Select(t => t.Hash).SingleAsync());
        Assert.Equal("snehil", await svc.ValidateAsync(token));
        Assert.Null(await svc.ValidateAsync("eos_not-a-real-token"));
        Assert.Null(await svc.ValidateAsync(null));
        Assert.Null(await svc.ValidateAsync("plain"));

        await svc.RevokeAsync((await _db.ApiTokens.SingleAsync()).Id, "snehil");
        Assert.Null(await svc.ValidateAsync(token));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync("", "x"));
    }

    [Fact]
    public async Task Token_Names_Must_Be_Unique_While_Active()
    {
        var svc = new TokenService(_db);
        await svc.CreateAsync("lead1", "x");
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync("lead1", "x"));
        await svc.RevokeAsync((await _db.ApiTokens.SingleAsync()).Id, "x");
        await svc.CreateAsync("lead1", "x");
        Assert.True(await svc.AnyActiveAsync());
    }

    [Fact]
    public async Task People_Can_Be_Created_Updated_Assigned_To_Teams_And_Led()
    {
        var svc = new PeopleService(_db);
        var team = await svc.CreateTeamAsync("LPP", "t");
        var anna = await svc.CreateAsync(new Person { Code = "E1", FullName = "Anna" }, "t");
        await svc.UpdateAsync(anna.Id, new PersonPatch { TeamId = team.Id, RoleTitle = "Lead", AvailabilityFraction = 0.8m, IsUnverified = false }, "t");
        await svc.SetTeamLeaderAsync(team.Id, anna.Id, "t");

        var row = (await svc.ListAsync()).Single();
        Assert.Equal(("LPP", "Lead", 0.8m), (row.Team, row.RoleTitle, row.AvailabilityFraction));
        var teams = await svc.ListTeamsAsync();
        Assert.Equal(("Anna", 1), (teams.Single().LeaderName, teams.Single().Members));

        await svc.UpdateAsync(anna.Id, new PersonPatch { ClearTeam = true, IsActive = false }, "t");
        Assert.Empty(await svc.ListAsync());
        Assert.Single(await svc.ListAsync(includeInactive: true));
        await Assert.ThrowsAsync<PeopleException>(() => svc.CreateAsync(new Person { Code = "E1", FullName = "Dup" }, "t"));
        await Assert.ThrowsAsync<PeopleException>(() => svc.UpdateAsync(anna.Id, new PersonPatch { AvailabilityFraction = 1.5m }, "t"));
        await Assert.ThrowsAsync<PeopleException>(() => svc.CreateTeamAsync("LPP", "t"));
    }

    [Fact]
    public async Task Leave_And_Manual_Allocations_Are_Validated_And_Upserted()
    {
        var svc = new PeopleService(_db);
        var anna = await svc.CreateAsync(new Person { Code = "E1", FullName = "Anna" }, "t");
        var project = new EOS.Domain.Projects.Project { ProjectNo = "P", Name = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();

        await svc.AddLeaveAsync(new EOS.Domain.Capacity.Leave { PersonId = anna.Id, FromDate = D(10, 12), ToDate = D(10, 14) }, "t");
        await Assert.ThrowsAsync<PeopleException>(() => svc.AddLeaveAsync(new EOS.Domain.Capacity.Leave { PersonId = anna.Id, FromDate = D(10, 14), ToDate = D(10, 12) }, "t"));

        await svc.SetAllocationAsync(new EOS.Domain.Capacity.Allocation { PersonId = anna.Id, ProjectId = project.Id, Month = D(10, 17), Hours = 80 }, "t");
        await svc.SetAllocationAsync(new EOS.Domain.Capacity.Allocation { PersonId = anna.Id, ProjectId = project.Id, Month = D(10, 3), Hours = 100 }, "t");
        var a = await _db.Allocations.SingleAsync();
        Assert.Equal((D(10, 1), 100m), (a.Month, a.Hours));
        await Assert.ThrowsAsync<PeopleException>(() => svc.SetAllocationAsync(new EOS.Domain.Capacity.Allocation { PersonId = anna.Id, ProjectId = project.Id, Month = D(10, 1), Hours = 5, Status = "Bogus" }, "t"));
        await svc.RemoveAllocationAsync(a.Id, "t");
        Assert.Empty(await _db.Allocations.ToListAsync());
    }

    [Fact]
    public async Task Person_Profile_Brings_Skills_Capacity_Projects_And_Leave_Together()
    {
        var svc = new PeopleService(_db);
        var anna = await svc.CreateAsync(new Person { Code = "E1", FullName = "Anna" }, "t");
        var sql = new EOS.Domain.Skills.Skill { Code = "SQL", Name = "SQL" };
        _db.Skills.Add(sql); await _db.SaveChangesAsync();
        _db.PersonSkills.Add(new EOS.Domain.Skills.PersonSkill { PersonId = anna.Id, SkillId = sql.Id, Rating = 4 });
        _db.Modules.AddRange(new Module { Product = "F", Name = "LV", SubModule = "a" }, new Module { Product = "F", Name = "LV", SubModule = "b" });
        await _db.SaveChangesAsync();
        _db.PersonModules.Add(new PersonModule { PersonId = anna.Id, ModuleId = (await _db.Modules.FirstAsync()).Id, State = KnowledgeState.Know });
        _db.Leaves.Add(new EOS.Domain.Capacity.Leave { PersonId = anna.Id, FromDate = D(10, 20), ToDate = D(10, 22) });
        await _db.SaveChangesAsync();

        var profile = await svc.GetProfileAsync(anna.Id, D(10, 14));
        Assert.Equal(("SQL", 4), (profile.Skills.Single().Skill, profile.Skills.Single().Rating));
        Assert.Equal((1, 2, 0.5m), (profile.ModulesKnown, profile.ModulesTotal, profile.KnowFraction));
        Assert.Equal(4, profile.Capacity.Count);
        Assert.Single(profile.Leave);
        await Assert.ThrowsAsync<PeopleException>(() => svc.GetProfileAsync(9999, D(10, 14)));
    }

    [Fact]
    public async Task Training_Summary_Uses_Configurable_Bands_And_Coverage_Lists_Thin_Modules_First()
    {
        var anna = new Person { Code = "E1", FullName = "Anna" }; var bela = new Person { Code = "E2", FullName = "Bela" };
        _db.People.AddRange(anna, bela);
        var m1 = new Module { Product = "F", Name = "LV", SubModule = "a" }; var m2 = new Module { Product = "F", Name = "LV", SubModule = "b" };
        _db.Modules.AddRange(m1, m2); await _db.SaveChangesAsync();
        _db.PersonModules.AddRange(
            new PersonModule { PersonId = anna.Id, ModuleId = m1.Id, State = KnowledgeState.Know }, new PersonModule { PersonId = anna.Id, ModuleId = m2.Id, State = KnowledgeState.Know },
            new PersonModule { PersonId = bela.Id, ModuleId = m1.Id, State = KnowledgeState.Know }, new PersonModule { PersonId = bela.Id, ModuleId = m2.Id, State = KnowledgeState.DontKnow });
        await _db.SaveChangesAsync();

        var svc = new TrainingService(_db);
        var summary = await svc.SummaryAsync(D(10, 14));
        Assert.Equal(("Anna", "Good", 1m), (summary[0].Name, summary[0].Level, summary[0].KnowFraction));
        Assert.Equal(("Bela", "Weak"), (summary[1].Name, summary[1].Level));
        var coverage = await svc.CoverageAsync();
        Assert.Equal(("b", 1), (coverage[0].SubModule, coverage[0].KnownBy));

        await new SettingsStore(_db).SetAsync(EOS.Domain.Settings.SettingKeys.KnowledgeMediumAtLeast, EOS.Domain.Settings.SettingScope.Global, "0.5", D(1, 1), "t", "lower the medium band");
        Assert.Equal("Medium", (await svc.SummaryAsync(D(10, 14)))[1].Level);
    }

    [Fact]
    public async Task Doubts_Follow_Raise_Assign_Resolve()
    {
        var anna = new Person { Code = "E1", FullName = "Anna" }; var mentor = new Person { Code = "E2", FullName = "Mentor" };
        _db.People.AddRange(anna, mentor); await _db.SaveChangesAsync();
        var svc = new TrainingService(_db);

        var d = await svc.RaiseAsync("LV", "Pass Parameter", "How do parameters work?", anna.Id, D(10, 1), "t");
        Assert.Equal((DoubtResult.Open, "Anna"), (d.Result, d.RaisedByName));
        await svc.AssignAsync(d.Id, mentor.Id, D(10, 2), "t");
        Assert.Equal((DoubtResult.Assigned, "Mentor"), ((await svc.DoubtsAsync()).Single().Result, (await svc.DoubtsAsync()).Single().AssignedToName));
        await svc.ResolveAsync(d.Id, DoubtResult.Solved, D(10, 3), "Showed the LV parameter dialog", "t");
        Assert.Empty(await svc.DoubtsAsync(openOnly: true));
        Assert.Equal(D(10, 3), (await svc.DoubtsAsync()).Single().SolvedOn);

        await Assert.ThrowsAsync<TrainingException>(() => svc.RaiseAsync("LV", "x", " ", null, D(10, 1), "t"));
        await Assert.ThrowsAsync<TrainingException>(() => svc.ResolveAsync(d.Id, DoubtResult.Open, D(10, 3), null, "t"));
    }

    [Fact]
    public async Task Hosts_Are_Updated_Linked_To_Projects_And_The_Reclaim_List_Excludes_Linked_Vms()
    {
        var project = new EOS.Domain.Projects.Project { ProjectNo = "PRO1", Name = "One" };
        _db.Projects.Add(project);
        var physical = new Host { Kind = "physical", Ip = "10.0.0.1", Name = "Base" };
        _db.Hosts.Add(physical); await _db.SaveChangesAsync();
        var old = new Host { Kind = "vm", Name = "OLD", State = "Off", Purpose = "Unknown", ParentHostId = physical.Id };
        var used = new Host { Kind = "vm", Name = "USED", State = "Off", Purpose = "Unknown", ParentHostId = physical.Id };
        _db.Hosts.AddRange(old, used); await _db.SaveChangesAsync();
        _db.HostDisks.Add(new HostDisk { HostId = physical.Id, Drive = "C", FreeGb = 2, TotalGb = 100, CapturedOn = D(10, 1) });
        await _db.SaveChangesAsync();

        var svc = new HostService(_db);
        await svc.LinkAsync(project.Id, used.Id, "application", "t");
        await svc.LinkAsync(project.Id, used.Id, "application", "t");
        Assert.Equal(["OLD"], (await svc.ReclaimListAsync()).Select(h => h.Name));
        Assert.Equal(["USED"], (await svc.ListAsync(projectId: project.Id)).Select(h => h.Name));

        await svc.UpdateAsync(old.Id, new HostPatch { State = "Running", OwnerName = "Sagar", NeedBackup = true, LastBackupOn = D(10, 10) }, "t");
        Assert.Equal(("Running", "Sagar"), ((await svc.ListAsync()).Single(h => h.Name == "OLD").State, (await svc.ListAsync()).Single(h => h.Name == "OLD").OwnerName));
        await Assert.ThrowsAsync<HostException>(() => svc.UpdateAsync(old.Id, new HostPatch { State = "Exploded" }, "t"));
        Assert.Equal(0.02m, (await svc.ListAsync()).Single(h => h.Kind == "physical").Disks.Single().FreeFraction);
        Assert.Equal(2, (await svc.ListAsync()).Single(h => h.Kind == "physical").VmCount);
        await svc.UnlinkAsync(project.Id, used.Id, "application", "t");
        Assert.Equal(["USED"], (await svc.ReclaimListAsync()).Select(h => h.Name));
    }

    [Fact]
    public async Task Vocabularies_Can_Be_Extended_And_Disabled_And_Validation_Follows()
    {
        var svc = new AdminService(_db);
        Assert.False(await ConfigurationService.IsValidAsync(_db, Vocabularies.TrackerStatus, "Parked"));
        await svc.AddValueAsync(Vocabularies.TrackerStatus, "Parked", "#999", "t");
        Assert.True(await ConfigurationService.IsValidAsync(_db, Vocabularies.TrackerStatus, "Parked"));

        var item = await _db.Vocabulary.SingleAsync(v => v.Value == "Parked");
        await svc.SetActiveAsync(item.Id, false, "t");
        Assert.False(await ConfigurationService.IsValidAsync(_db, Vocabularies.TrackerStatus, "Parked"));
        Assert.True((await svc.VocabulariesAsync()).ContainsKey(Vocabularies.TrackerStatus));
    }

    [Fact]
    public async Task Calendars_Are_Validated_One_Is_Default_And_Holidays_Change_The_Engine_Calendar()
    {
        var svc = new AdminService(_db);
        await Assert.ThrowsAsync<AdminException>(() => svc.SaveCalendarAsync(new WorkCalendarDef { Name = "bad", WorkDays = "1,9" }, "t"));
        await Assert.ThrowsAsync<AdminException>(() => svc.SaveCalendarAsync(new WorkCalendarDef { Name = "bad", HoursPerDay = 30 }, "t"));

        var five = await svc.SaveCalendarAsync(new WorkCalendarDef { Name = "Site 5-day 7.5h", HoursPerDay = 7.5m, WorkDays = "5,1,2,3,4", IsDefault = true }, "t");
        Assert.Equal("1,2,3,4,5", five.WorkDays);
        Assert.Single(await _db.Calendars.Where(c => c.IsDefault).ToListAsync());

        await svc.SetHolidayAsync(five.Id, D(10, 20), false, "Festival", "t");
        var engine = await ConfigurationService.LoadCalendarAsync(_db, null);
        Assert.Equal(7.5m, engine.HoursPerDay);
        Assert.False(engine.IsWorkingDay(D(10, 20)));
        Assert.False(engine.IsWorkingDay(D(10, 17)));

        await svc.SetHolidayAsync(five.Id, D(10, 17), true, "Make-up day", "t");
        Assert.True((await ConfigurationService.LoadCalendarAsync(_db, null)).IsWorkingDay(D(10, 17)));
    }

    [Fact]
    public async Task Custom_Fields_Are_Typed_And_Validated()
    {
        var svc = new AdminService(_db);
        await svc.AddFieldAsync(new CustomFieldDefinition { EntityKind = "person", Key = "grade", Label = "Grade", Type = CustomFieldType.Choice, Options = "G1|G2|G3" }, "t");
        await svc.AddFieldAsync(new CustomFieldDefinition { EntityKind = "person", Key = "joined", Label = "Joined", Type = CustomFieldType.Date }, "t");
        await svc.AddFieldAsync(new CustomFieldDefinition { EntityKind = "person", Key = "ctc", Label = "Rate", Type = CustomFieldType.Number }, "t");

        await svc.SetFieldValueAsync("person", 7, "grade", "G2", "t");
        await svc.SetFieldValueAsync("person", 7, "joined", "2024-06-01", "t");
        Assert.Equal("G2", (await svc.FieldValuesAsync("person", 7))["grade"]);
        await Assert.ThrowsAsync<AdminException>(() => svc.SetFieldValueAsync("person", 7, "grade", "G9", "t"));
        await Assert.ThrowsAsync<AdminException>(() => svc.SetFieldValueAsync("person", 7, "ctc", "lots", "t"));
        await Assert.ThrowsAsync<AdminException>(() => svc.SetFieldValueAsync("person", 7, "joined", "yesterday", "t"));
        await Assert.ThrowsAsync<AdminException>(() => svc.SetFieldValueAsync("person", 7, "nope", "x", "t"));
        await Assert.ThrowsAsync<AdminException>(() => svc.AddFieldAsync(new CustomFieldDefinition { EntityKind = "person", Key = "grade", Label = "dup" }, "t"));
        await svc.SetFieldValueAsync("person", 7, "grade", "", "t");
        Assert.DoesNotContain("grade", (await svc.FieldValuesAsync("person", 7)).Keys);
    }

    [Fact]
    public async Task Backup_Creates_A_Restorable_Copy_And_Retention_Keeps_Recent_And_Monthly()
    {
        _db.People.Add(new Person { Code = "E1", FullName = "Backed up" });
        await _db.SaveChangesAsync();
        var dir = Path.Combine(Path.GetTempPath(), "eos-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var backup = await new BackupService(_db).CreateAsync(dir, "t");
            Assert.True(backup.SizeBytes > 0);

            var options = new DbContextOptionsBuilder<EosDbContext>().UseSqlite($"Data Source={backup.Path};Pooling=False").Options;
            using (var copy = new EosDbContext(options))
                Assert.Equal("Backed up", (await copy.People.SingleAsync()).FullName);

            foreach (var name in new[] { "eos-20260801-000000.db", "eos-20260802-000000.db", "eos-20260901-000000.db", "eos-20260902-000000.db", "eos-20260903-000000.db", "eos-20260904-000000.db" })
                File.WriteAllText(Path.Combine(dir, name), "x");
            var removed = BackupService.ApplyRetention(dir, keepDaily: 3, keepMonthly: 3);
            Assert.Equal(["eos-20260902-000000.db", "eos-20260901-000000.db", "eos-20260801-000000.db"], removed);
            Assert.DoesNotContain("eos-20260802-000000.db", removed);
            Assert.NotEmpty(BackupService.List(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
