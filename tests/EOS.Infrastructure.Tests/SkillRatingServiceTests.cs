using EOS.Domain.People;
using EOS.Domain.Skills;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Skills;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class SkillRatingServiceTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SkillRatingService _svc;
    private readonly int _person, _skill;

    public SkillRatingServiceTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        _svc = new SkillRatingService(_db);
        var p = new Person { Code = "E1", FullName = "Test Person" };
        var s = new Skill { Code = "SQL", Name = "SQL" };
        _db.People.Add(p);
        _db.Skills.Add(s);
        _db.SaveChanges();
        _person = p.Id;
        _skill = s.Id;
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    [Fact]
    public async Task Set_then_clear_removes_the_rating_and_records_both_transactions()
    {
        await _svc.SetRatingAsync(_person, _skill, 4, "owner", "Edited in skills matrix");
        Assert.Equal(4, (await _db.PersonSkills.SingleAsync()).Rating);

        await _svc.ClearRatingAsync(_person, _skill, "owner", "Edited in skills matrix");

        Assert.Empty(await _db.PersonSkills.ToListAsync());
        var tx = await _db.SkillTransactions.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, tx.Count);
        Assert.Equal(4, tx[1].OldRating);
        Assert.Null(tx[1].NewRating);
    }

    [Fact]
    public async Task Clearing_an_unrated_cell_is_a_no_op()
    {
        await _svc.ClearRatingAsync(_person, _skill, "owner", "x");
        Assert.Empty(await _db.SkillTransactions.ToListAsync());
    }

    [Fact]
    public async Task Clear_needs_a_reason_and_an_actor()
    {
        await _svc.SetRatingAsync(_person, _skill, 2, "owner", "r");
        await Assert.ThrowsAsync<SkillRatingException>(() => _svc.ClearRatingAsync(_person, _skill, "owner", " "));
        await Assert.ThrowsAsync<SkillRatingException>(() => _svc.ClearRatingAsync(_person, _skill, "", "r"));
        Assert.Single(await _db.PersonSkills.ToListAsync());
    }

    [Fact]
    public async Task Rating_outside_the_configured_range_is_rejected()
    {
        await Assert.ThrowsAsync<SkillRatingException>(() => _svc.SetRatingAsync(_person, _skill, 0, "owner", "r"));
        await Assert.ThrowsAsync<SkillRatingException>(() => _svc.SetRatingAsync(_person, _skill, 99, "owner", "r"));
    }

    [Fact]
    public async Task Undo_to_a_previous_rating_is_just_another_manual_set()
    {
        await _svc.SetRatingAsync(_person, _skill, 3, "owner", "r");
        await _svc.SetRatingAsync(_person, _skill, 5, "owner", "r");
        await _svc.SetRatingAsync(_person, _skill, 3, "owner", "Undo");
        Assert.Equal(3, (await _db.PersonSkills.SingleAsync()).Rating);
        Assert.Equal(3, await _db.SkillTransactions.CountAsync());
    }
}
