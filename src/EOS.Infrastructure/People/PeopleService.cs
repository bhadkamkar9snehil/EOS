using EOS.Domain.Audit;
using EOS.Domain.Capacity;
using EOS.Domain.People;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.People;

public sealed class PeopleException(string message) : Exception(message);

public sealed record PersonRow(int Id, string Code, string FullName, string? Team, int? TeamId, string? RoleTitle, string? BaseLocation,
    decimal AvailabilityFraction, bool IsActive, bool IsUnverified, string? Email);

public sealed record PersonSkillView(int SkillId, string Skill, int Rating, DateOnly? AssessedOn, string? AssessedBy, decimal? ModuleKnowFraction);

public sealed record PersonProfile(
    PersonRow Person, IReadOnlyList<PersonSkillView> Skills, int ModulesKnown, int ModulesTotal, decimal KnowFraction,
    IReadOnlyList<CapacityRow> Capacity, IReadOnlyList<(int ProjectId, string ProjectNo, string Name)> Projects,
    IReadOnlyList<Leave> Leave, int OpenDoubts);

public sealed record PersonPatch
{
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? RoleTitle { get; init; }
    public string? BaseLocation { get; init; }
    public int? TeamId { get; init; }
    public bool ClearTeam { get; init; }
    public decimal? AvailabilityFraction { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsUnverified { get; init; }
}

public sealed class PeopleService(EosDbContext db)
{
    public async Task<IReadOnlyList<PersonRow>> ListAsync(bool includeInactive = false, int? teamId = null, CancellationToken ct = default) =>
        await (from p in db.People.AsNoTracking()
               where (includeInactive || p.IsActive) && (teamId == null || p.TeamId == teamId)
               join t in db.Teams.AsNoTracking() on p.TeamId equals t.Id into teams
               from t in teams.DefaultIfEmpty()
               orderby p.FullName
               select new PersonRow(p.Id, p.Code, p.FullName, t == null ? null : t.Name, p.TeamId, p.RoleTitle, p.BaseLocation,
                   p.AvailabilityFraction, p.IsActive, p.IsUnverified, p.Email)).ToListAsync(ct);

    public async Task<Person> CreateAsync(Person person, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(person.Code) || string.IsNullOrWhiteSpace(person.FullName)) throw new PeopleException("A person needs a code and a name.");
        if (person.AvailabilityFraction is < 0 or > 1) throw new PeopleException("Availability must be between 0 and 1.");
        if (await db.People.AnyAsync(p => p.Code == person.Code, ct)) throw new PeopleException($"A person with code {person.Code} already exists.");
        db.People.Add(person);
        Audit("person.create", actor, person.Code, person.FullName);
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task UpdateAsync(int id, PersonPatch patch, string actor, CancellationToken ct = default)
    {
        var p = await db.People.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PeopleException($"Person {id} not found.");
        if (patch.FullName is not null) p.FullName = patch.FullName.Trim();
        if (patch.Email is not null) p.Email = patch.Email.Length == 0 ? null : patch.Email;
        if (patch.RoleTitle is not null) p.RoleTitle = patch.RoleTitle.Length == 0 ? null : patch.RoleTitle;
        if (patch.BaseLocation is not null) p.BaseLocation = patch.BaseLocation.Length == 0 ? null : patch.BaseLocation;
        if (patch.ClearTeam) p.TeamId = null;
        else if (patch.TeamId is { } t)
        {
            if (!await db.Teams.AnyAsync(x => x.Id == t, ct)) throw new PeopleException($"Team {t} not found.");
            p.TeamId = t;
        }
        if (patch.AvailabilityFraction is { } a)
        {
            if (a is < 0 or > 1) throw new PeopleException("Availability must be between 0 and 1.");
            p.AvailabilityFraction = a;
        }
        if (patch.IsActive is { } active) p.IsActive = active;
        if (patch.IsUnverified is { } u) p.IsUnverified = u;
        Audit("person.update", actor, p.Code, System.Text.Json.JsonSerializer.Serialize(patch));
        await db.SaveChangesAsync(ct);
    }

    public async Task<PersonProfile> GetProfileAsync(int id, DateOnly asOf, CancellationToken ct = default)
    {
        var row = (await ListAsync(true, ct: ct)).SingleOrDefault(p => p.Id == id) ?? throw new PeopleException($"Person {id} not found.");
        var skills = await db.Skills.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var ratings = await db.PersonSkills.AsNoTracking().Where(r => r.PersonId == id).ToDictionaryAsync(r => r.SkillId, ct);
        var views = skills.Where(s => ratings.ContainsKey(s.Id)).Select(s => new PersonSkillView(s.Id, s.Name, ratings[s.Id].Rating, ratings[s.Id].AssessedOn, ratings[s.Id].AssessedBy, null)).ToList();

        var states = await db.PersonModules.AsNoTracking().Where(m => m.PersonId == id).ToListAsync(ct);
        var known = states.Count(s => s.State == EOS.Domain.Training.KnowledgeState.Know);
        var total = await db.Modules.CountAsync(ct);

        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        var capacity = await new CapacityService(db).GetCapacityAsync(month, month.AddMonths(3), personId: id, ct: ct);
        var projects = (await db.TaskAssignments.AsNoTracking().Where(a => a.PersonId == id)
            .Join(db.Tasks, a => a.TaskId, t => t.Id, (a, t) => t.ProjectId).Distinct()
            .Join(db.Projects, pid => pid, p => p.Id, (pid, p) => new { p.Id, p.ProjectNo, p.Name }).ToListAsync(ct))
            .Select(x => (x.Id, x.ProjectNo, x.Name)).OrderBy(x => x.ProjectNo).ToList();
        var leave = await db.Leaves.AsNoTracking().Where(l => l.PersonId == id && l.ToDate >= asOf).OrderBy(l => l.FromDate).ToListAsync(ct);
        var name = row.FullName.ToLowerInvariant();
        var doubts = await db.Doubts.CountAsync(d => d.Result != EOS.Domain.Training.DoubtResult.Solved && (d.RaisedByPersonId == id || d.AssignedToPersonId == id), ct);
        return new PersonProfile(row, views, known, total, total == 0 ? 0m : (decimal)known / total, capacity, projects, leave, doubts);
    }

    public async Task<IReadOnlyList<(int Id, string Name, int? LeaderPersonId, string? LeaderName, int Members)>> ListTeamsAsync(CancellationToken ct = default)
    {
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var people = await db.People.AsNoTracking().ToListAsync(ct);
        return teams.Select(t => (t.Id, t.Name, t.LeaderPersonId, people.FirstOrDefault(p => p.Id == t.LeaderPersonId)?.FullName, people.Count(p => p.TeamId == t.Id && p.IsActive))).ToList();
    }

    public async Task<Team> CreateTeamAsync(string name, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new PeopleException("A team needs a name.");
        if (await db.Teams.AnyAsync(t => t.Name == name.Trim(), ct)) throw new PeopleException($"Team '{name}' already exists.");
        var team = new Team { Name = name.Trim() };
        db.Teams.Add(team);
        Audit("team.create", actor, name, null);
        await db.SaveChangesAsync(ct);
        return team;
    }

    public async Task SetTeamLeaderAsync(int teamId, int? personId, string actor, CancellationToken ct = default)
    {
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == teamId, ct) ?? throw new PeopleException($"Team {teamId} not found.");
        if (personId is { } pid && !await db.People.AnyAsync(p => p.Id == pid, ct)) throw new PeopleException($"Person {pid} not found.");
        team.LeaderPersonId = personId;
        Audit("team.leader", actor, team.Name, personId?.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<Leave> AddLeaveAsync(Leave leave, string actor, CancellationToken ct = default)
    {
        if (leave.ToDate < leave.FromDate) throw new PeopleException("Leave ends before it starts.");
        if (!await db.People.AnyAsync(p => p.Id == leave.PersonId, ct)) throw new PeopleException($"Person {leave.PersonId} not found.");
        db.Leaves.Add(leave);
        Audit("leave.add", actor, leave.PersonId.ToString(), $"{leave.FromDate:yyyy-MM-dd}..{leave.ToDate:yyyy-MM-dd} {leave.Kind}");
        await db.SaveChangesAsync(ct);
        return leave;
    }

    public async Task RemoveLeaveAsync(int id, string actor, CancellationToken ct = default)
    {
        var l = await db.Leaves.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PeopleException($"Leave {id} not found.");
        db.Leaves.Remove(l);
        Audit("leave.remove", actor, l.PersonId.ToString(), null);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Allocation> SetAllocationAsync(Allocation a, string actor, CancellationToken ct = default)
    {
        if (a.Hours < 0) throw new PeopleException("Hours cannot be negative.");
        if (!await EOS.Infrastructure.Configuration.ConfigurationService.IsValidAsync(db, EOS.Domain.Configuration.Vocabularies.AllocationStatus, a.Status, ct))
            throw new PeopleException($"'{a.Status}' is not an allowed allocation status.");
        a.Month = new DateOnly(a.Month.Year, a.Month.Month, 1);
        var existing = await db.Allocations.SingleOrDefaultAsync(x => x.PersonId == a.PersonId && x.ProjectId == a.ProjectId && x.Month == a.Month && x.Status == a.Status && x.DemandId == a.DemandId, ct);
        if (existing is null) db.Allocations.Add(a); else { existing.Hours = a.Hours; existing.Role = a.Role; a = existing; }
        Audit("allocation.set", actor, $"{a.PersonId}/{a.ProjectId}", $"{a.Month:yyyy-MM} {a.Hours} h {a.Status}");
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task RemoveAllocationAsync(int id, string actor, CancellationToken ct = default)
    {
        var a = await db.Allocations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PeopleException($"Allocation {id} not found.");
        db.Allocations.Remove(a);
        Audit("allocation.remove", actor, id.ToString(), null);
        await db.SaveChangesAsync(ct);
    }

    private void Audit(string action, string actor, string subject, string? detail) =>
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = action, SubjectKind = "people", SubjectId = subject, Detail = detail });
}
