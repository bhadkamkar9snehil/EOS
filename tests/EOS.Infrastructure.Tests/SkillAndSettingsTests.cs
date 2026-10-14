using EOS.Domain.People;
using EOS.Domain.Settings;
using EOS.Domain.Skills;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Skills;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class SkillAndSettingsTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Person _person = new() { Code = "E001", FullName = "Anna Lead" };
    private readonly Skill _skill = new() { Code = "SK01", Name = "SQL" };

    public SkillAndSettingsTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        _db.People.Add(_person);
        _db.Skills.Add(_skill);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    // ---- Manual-only skill ratings (spec §9.5) ---------------------------------------------------------

    [Fact]
    public async Task Rating_Change_Records_Who_Old_New_And_Reason()
    {
        var svc = new SkillRatingService(_db);
        await svc.SetRatingAsync(_person.Id, _skill.Id, 3, "snehil", "Initial", SkillTransactionKind.Assessment);
        await svc.SetRatingAsync(_person.Id, _skill.Id, 4, "snehil", "Designed the Sohar historian");

        Assert.Equal(4, (await _db.PersonSkills.SingleAsync()).Rating);
        var last = await _db.SkillTransactions.OrderByDescending(t => t.Id).FirstAsync();
        Assert.Equal((3, 4, "snehil", "Designed the Sohar historian"), (last.OldRating!.Value, last.NewRating!.Value, last.ChangedBy, last.Reason));
        Assert.Equal(SkillTransactionKind.ManualAdjustment, last.Kind);
        Assert.Equal(2, await _db.AuditEvents.CountAsync(a => a.Action == "skill.rate"));
    }

    [Fact]
    public async Task Manual_Adjustment_Requires_A_Reason_And_An_Actor()
    {
        var svc = new SkillRatingService(_db);
        await Assert.ThrowsAsync<SkillRatingException>(() => svc.SetRatingAsync(_person.Id, _skill.Id, 3, "snehil", "  "));
        await Assert.ThrowsAsync<SkillRatingException>(() => svc.SetRatingAsync(_person.Id, _skill.Id, 3, "", "why"));
        Assert.Equal(0, await _db.PersonSkills.CountAsync());
    }

    [Fact]
    public async Task Rating_Range_Comes_From_The_Level_Count_Setting()
    {
        var svc = new SkillRatingService(_db);
        await Assert.ThrowsAsync<SkillRatingException>(() => svc.SetRatingAsync(_person.Id, _skill.Id, 6, "s", "r"));

        await new SettingsStore(_db).SetAsync(SettingKeys.SkillLevelCount, SettingScope.Global, "7", new DateOnly(2020, 1, 1), "snehil", "Use a 7-level scale");
        await svc.SetRatingAsync(_person.Id, _skill.Id, 6, "s", "r");
        Assert.Equal(6, (await _db.PersonSkills.SingleAsync()).Rating);
        await Assert.ThrowsAsync<SkillRatingException>(() => svc.SetRatingAsync(_person.Id, _skill.Id, 8, "s", "r"));
        await Assert.ThrowsAsync<SkillRatingException>(() => svc.SetRatingAsync(_person.Id, _skill.Id, 0, "s", "r"));
    }

    [Fact]
    public async Task Setting_The_Same_Rating_Again_Adds_No_Transaction()
    {
        var svc = new SkillRatingService(_db);
        await svc.SetRatingAsync(_person.Id, _skill.Id, 3, "s", "first");
        await svc.SetRatingAsync(_person.Id, _skill.Id, 3, "s", "again");
        Assert.Equal(1, await _db.SkillTransactions.CountAsync());
    }

    [Fact]
    public async Task Evidence_Note_Does_Not_Change_The_Rating()
    {
        var svc = new SkillRatingService(_db);
        await svc.SetRatingAsync(_person.Id, _skill.Id, 3, "s", "first");
        await svc.AddEvidenceNoteAsync(_person.Id, _skill.Id, "Completed 120 task-hours on SQL", "s");

        Assert.Equal(3, (await _db.PersonSkills.SingleAsync()).Rating);
        Assert.Equal(SkillTransactionKind.EvidenceNote, (await _db.SkillTransactions.OrderByDescending(t => t.Id).FirstAsync()).Kind);
    }

    // ---- Settings store (spec §8.7) --------------------------------------------------------------------

    [Fact]
    public async Task Changing_A_Setting_Closes_The_Previous_Entry_And_Keeps_History()
    {
        var store = new SettingsStore(_db);
        await store.SetAsync(SettingKeys.TargetUtilisation, SettingScope.Global, "0.80", new DateOnly(2026, 1, 1), "snehil", "Initial target");
        await store.SetAsync(SettingKeys.TargetUtilisation, SettingScope.Global, "0.85", new DateOnly(2026, 10, 1), "snehil", "Raise target");

        var rows = await _db.Settings.OrderBy(r => r.EffectiveFrom).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), rows[0].EffectiveTo);
        Assert.Null(rows[1].EffectiveTo);

        var resolver = await store.LoadResolverAsync();
        var chain = SettingsResolver.Chain();
        Assert.Equal(0.80m, resolver.Get<decimal>(SettingKeys.TargetUtilisation, chain, new DateOnly(2026, 8, 1)));
        Assert.Equal(0.85m, resolver.Get<decimal>(SettingKeys.TargetUtilisation, chain, new DateOnly(2026, 10, 14)));
    }

    [Fact]
    public async Task Team_Scope_Overrides_Global_And_Is_Persisted()
    {
        var store = new SettingsStore(_db);
        await store.SetAsync(SettingKeys.HoursPerDay, SettingScope.Team("LPP"), "7.5", new DateOnly(2026, 1, 1), "snehil", "LPP works 7.5 h");
        var resolver = await store.LoadResolverAsync();
        Assert.Equal(7.5m, resolver.Get<decimal>(SettingKeys.HoursPerDay, SettingsResolver.Chain(teamId: "LPP"), new DateOnly(2026, 10, 14)));
        Assert.Equal(8m, resolver.Get<decimal>(SettingKeys.HoursPerDay, SettingsResolver.Chain(), new DateOnly(2026, 10, 14)));
    }

    [Fact]
    public async Task Settings_Reject_Unknown_Keys_Invalid_Json_And_Missing_Reason()
    {
        var store = new SettingsStore(_db);
        var from = new DateOnly(2026, 1, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("no.such.key", SettingScope.Global, "1", from, "s", "r"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(SettingKeys.HoursPerDay, SettingScope.Global, "{oops", from, "s", "r"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(SettingKeys.HoursPerDay, SettingScope.Global, "8", from, "s", ""));
    }

    // ---- Person matching (spec §6.4) -------------------------------------------------------------------

    private static PersonMatcher Matcher(params string[] names) =>
        new(names.Select((n, i) => new Person { Id = i + 1, Code = $"E{i:000}", FullName = n }).ToList(), []);

    [Fact]
    public void Matcher_Resolves_Unique_First_Names_And_Single_Typos()
    {
        var m = Matcher("Aayush Patel", "Ziaul Haque", "Chirag Dhimmar", "Deep Thakor", "Deepak Upadhaya");
        Assert.Equal("Chirag Dhimmar", m.Resolve("chirag")?.FullName);
        Assert.Equal("Aayush Patel", m.Resolve("Ayush")?.FullName);          // one deletion
        Assert.Equal("Ziaul Haque", m.Resolve("Ziual")?.FullName);           // one transposition
        Assert.Equal("Deep Thakor", m.Resolve("Deep")?.FullName);            // exact first token beats near-match "Deepak"
    }

    [Fact]
    public void Matcher_Never_Guesses_When_Ambiguous_Or_Unknown()
    {
        var m = Matcher("Tejas Kale", "Tejas Khadke", "Dev Patel", "Dharati Patel");
        Assert.Null(m.Resolve("Tejas"));                                     // two Tejas
        Assert.Null(m.Resolve("Safwan"));                                    // unknown
        Assert.Null(m.Resolve("Tejas Shah"));                                // multi-word that matches nothing exactly
        Assert.Null(m.Resolve(""));
        Assert.Equal("Tejas Kale", m.Resolve("Tejas Kale")?.FullName);
    }

    [Fact]
    public void Matcher_Prefers_The_Alias_Table()
    {
        var people = new List<Person> { new() { Id = 1, Code = "E1", FullName = "Dharati Patel" }, new() { Id = 2, Code = "E2", FullName = "Dev Patel" } };
        var aliases = new List<PersonAlias> { new() { Alias = "DP", Kind = AliasKind.Initials, Source = "tracker", PersonId = 1 } };
        Assert.Equal("Dharati Patel", new PersonMatcher(people, aliases).Resolve("dp")?.FullName);
    }
}
