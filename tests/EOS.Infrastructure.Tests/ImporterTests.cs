using EOS.Domain.Skills;
using EOS.Domain.Training;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Skills;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class ImporterTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly List<string> _files = [];

    public ImporterTests() => (_db, _connection) = EosDatabase.InMemory();

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        foreach (var f in _files) File.Delete(f);
    }

    private string Track(string path) { _files.Add(path); return path; }

    [Fact]
    public async Task Agility_Import_Creates_People_Skills_And_Ratings()
    {
        var report = await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility()), commit: true, "tester");

        Assert.Equal(3, report.Of("Person").Added);
        Assert.Equal(2, report.Of("Skill").Added);
        Assert.Equal(6, report.Of("SkillRating").Added);
        Assert.Equal(2, report.Of("Team").Added);
        Assert.Equal(3, await _db.People.CountAsync());
        Assert.All(await _db.People.ToListAsync(), p => Assert.True(p.IsUnverified));
        Assert.Equal(6, await _db.SkillTransactions.CountAsync(t => t.Kind == SkillTransactionKind.Assessment));
    }

    [Fact]
    public async Task Agility_Import_Is_Idempotent()
    {
        var file = Track(TestWorkbooks.Agility());
        await new AgilityWorkbookImporter(_db).ImportAsync(file, true, "tester");
        var second = await new AgilityWorkbookImporter(_db).ImportAsync(file, true, "tester");

        Assert.Equal(0, second.Of("Person").Added);
        Assert.Equal(3, second.Of("Person").Unchanged);
        Assert.Equal(6, second.Of("SkillRating").Unchanged);
        Assert.Equal(6, await _db.SkillTransactions.CountAsync());
    }

    [Fact]
    public async Task Agility_Preview_Saves_Nothing()
    {
        var report = await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility()), commit: false, "tester");

        Assert.False(report.Committed);
        Assert.Equal(3, report.Of("Person").Added);
        Assert.Equal(0, await _db.People.CountAsync());
        Assert.Equal(0, await _db.Skills.CountAsync());
    }

    [Fact]
    public async Task Matrix_Matches_By_Name_When_Ids_Disagree_And_Warns()
    {
        var report = await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility()), true, "tester");

        Assert.Contains(report.Warnings, w => w.Contains("Kartik Patil") && w.Contains("E099") && w.Contains("E003"));
        var kartik = await _db.People.SingleAsync(p => p.FullName == "Kartik Patil");
        Assert.Equal(2, await _db.PersonSkills.CountAsync(x => x.PersonId == kartik.Id));
    }

    [Fact]
    public async Task Reimport_Never_Overwrites_A_Rating_A_Person_Changed_Manually()
    {
        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility(sqlRatingForAnna: 3)), true, "tester");
        var anna = await _db.People.SingleAsync(p => p.Code == "E001");
        var sql = await _db.Skills.SingleAsync(s => s.Code == "SK01");

        await new SkillRatingService(_db).SetRatingAsync(anna.Id, sql.Id, 4, "snehil", "Led the Sohar historian work");

        var report = await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility(sqlRatingForAnna: 2)), true, "tester");

        Assert.Equal(4, (await _db.PersonSkills.SingleAsync(x => x.PersonId == anna.Id && x.SkillId == sql.Id)).Rating);
        Assert.Contains(report.Warnings, w => w.StartsWith("Kept manual rating", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reimport_Updates_An_Imported_Rating_And_Records_A_Transaction()
    {
        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility(sqlRatingForAnna: 3)), true, "tester");
        var report = await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility(sqlRatingForAnna: 4)), true, "tester");

        Assert.Equal(1, report.Of("SkillRating").Updated);
        var tx = await _db.SkillTransactions.Where(t => t.OldRating == 3 && t.NewRating == 4).SingleAsync();
        Assert.StartsWith("import:", tx.Source);
    }

    [Fact]
    public async Task Verified_Person_Values_Are_Kept_On_Reimport()
    {
        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility(annaRole: "Engineer")), true, "tester");
        var anna = await _db.People.SingleAsync(p => p.Code == "E001");
        anna.RoleTitle = "Principal Engineer";
        anna.IsUnverified = false;
        await _db.SaveChangesAsync();

        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility(annaRole: "Lead Engineer")), true, "tester");

        Assert.Equal("Principal Engineer", (await _db.People.SingleAsync(p => p.Code == "E001")).RoleTitle);
    }

    [Fact]
    public async Task Training_Import_Reads_Modules_States_And_Doubts()
    {
        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility()), true, "tester");
        var report = await new TrainingWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Training()), true, "tester");

        Assert.Equal(3, report.Of("Module").Added);
        Assert.Equal(9, report.Of("KnowledgeState").Added);
        Assert.Equal(2, report.Of("Doubt").Added);

        var states = await _db.PersonModules.GroupBy(x => x.State).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        Assert.Equal(6, states[KnowledgeState.Know]);
        Assert.Equal(2, states[KnowledgeState.DontKnow]);
        Assert.Equal(1, states[KnowledgeState.NeedToDiscuss]);
    }

    [Fact]
    public async Task Training_Matches_Short_Names_And_Creates_Unassigned_Persons_For_Unknowns()
    {
        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility()), true, "tester");
        var report = await new TrainingWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Training()), true, "tester");

        Assert.Equal(["Zed"], report.Unresolved);
        var zed = await _db.People.SingleAsync(p => p.FullName == "Zed");
        Assert.StartsWith("TR-", zed.Code);
        Assert.True(zed.IsUnverified);
        Assert.Null(zed.TeamId);

        Assert.Equal(1, await _db.People.CountAsync(p => p.FullName.StartsWith("Bela")));
        var doubt = await _db.Doubts.SingleAsync(d => d.SourceRow == 1);
        Assert.Equal(DoubtResult.Solved, doubt.Result);
        Assert.NotNull(doubt.RaisedByPersonId);
        Assert.NotNull(doubt.AssignedToPersonId);
    }

    [Fact]
    public async Task Training_Import_Is_Idempotent_And_Updates_Changed_States()
    {
        await new AgilityWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Agility()), true, "tester");
        await new TrainingWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Training("Know")), true, "tester");
        var same = await new TrainingWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Training("Know")), true, "tester");
        Assert.Equal(0, same.Of("KnowledgeState").Added + same.Of("KnowledgeState").Updated);
        Assert.Equal(2, same.Of("Doubt").Unchanged);

        var changed = await new TrainingWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Training("don't Know")), true, "tester");
        Assert.Equal(1, changed.Of("KnowledgeState").Updated);
    }

    [Fact]
    public async Task Module_Proficiency_Sheet_Is_Ignored()
    {
        await new TrainingWorkbookImporter(_db).ImportAsync(Track(TestWorkbooks.Training()), true, "tester");
        Assert.All(await _db.PersonModules.ToListAsync(), x => Assert.Null(x.Proficiency));
    }
}
