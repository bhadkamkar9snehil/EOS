using EOS.Domain.Audit;
using EOS.Domain.Capacity;
using EOS.Domain.Settings;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Capacity;

public sealed class DemandException(string message) : Exception(message);

public sealed record DemandView(
    int Id, int? ProjectId, string? ProjectNo, int? OpportunityId, int SkillId, string SkillName, int MinLevel, decimal HoursPerWeek,
    DateOnly Start, DateOnly End, string? Location, string Status, decimal SuppliedHoursPerWeek, int EligibleCount,
    string? BestPerson, int BestRating, string? Notes);

public sealed record Candidate(
    int PersonId, string Name, int Rating, decimal FreePlannedHoursPerWeek, bool AlreadyOnProject, decimal LeaveFraction,
    decimal Score, IReadOnlyList<string> Reasons);

/// <summary>Skill demand lines, their fill status and ranked candidates (spec §12, §9.7).</summary>
public sealed class DemandService(EosDbContext db)
{
    public async Task<Demand> AddAsync(Demand demand, string actor, CancellationToken ct = default)
    {
        await ValidateAsync(demand, ct);
        db.Demands.Add(demand);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "demand.add", SubjectKind = "demand", SubjectId = demand.SkillId.ToString() });
        await db.SaveChangesAsync(ct);
        return demand;
    }

    public async Task UpdateAsync(Demand changes, string actor, CancellationToken ct = default)
    {
        var current = await db.Demands.SingleOrDefaultAsync(d => d.Id == changes.Id, ct) ?? throw new DemandException($"Demand {changes.Id} not found.");
        await ValidateAsync(changes, ct);
        db.Entry(current).CurrentValues.SetValues(changes);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "demand.update", SubjectKind = "demand", SubjectId = current.Id.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, string actor, CancellationToken ct = default)
    {
        var d = await db.Demands.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new DemandException($"Demand {id} not found.");
        db.Demands.Remove(d);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "demand.delete", SubjectKind = "demand", SubjectId = id.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DemandView>> ListAsync(int? projectId = null, int? opportunityId = null, CancellationToken ct = default)
    {
        var demands = await db.Demands.AsNoTracking()
            .Where(d => (projectId == null || d.ProjectId == projectId) && (opportunityId == null || d.OpportunityId == opportunityId))
            .OrderBy(d => d.StartDate).ThenBy(d => d.Id).ToListAsync(ct);
        var skills = await db.Skills.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.ProjectNo, ct);
        var people = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        var ratings = await db.PersonSkills.AsNoTracking().ToListAsync(ct);
        var allocations = await db.Allocations.AsNoTracking().Where(a => a.DemandId != null && a.Status == "Confirmed").ToListAsync(ct);

        var views = new List<DemandView>();
        foreach (var d in demands)
        {
            var eligible = ratings.Where(r => r.SkillId == d.SkillId && r.Rating >= d.MinLevel).OrderByDescending(r => r.Rating).ThenBy(r => people.GetValueOrDefault(r.PersonId, "")).ToList();
            var supplied = SuppliedPerWeek(d, allocations.Where(a => a.DemandId == d.Id).ToList());
            var status = d.StatusOverride ?? (supplied >= d.HoursPerWeek - 0.01m && d.HoursPerWeek > 0 ? "Filled" : supplied > 0 ? "Partially Filled" : "Open");
            var best = eligible.FirstOrDefault();
            views.Add(new DemandView(d.Id, d.ProjectId, d.ProjectId is { } pid ? projects.GetValueOrDefault(pid) : null, d.OpportunityId, d.SkillId,
                skills.GetValueOrDefault(d.SkillId, "?"), d.MinLevel, d.HoursPerWeek, d.StartDate, d.EndDate, d.Location, status, Math.Round(supplied, 1),
                eligible.Count, best is null ? null : people.GetValueOrDefault(best.PersonId), best?.Rating ?? 0, d.Notes));
        }
        return views;
    }

    /// <summary>
    /// Eligible engineers for a demand, ranked (spec §9.7): least-sufficient skill first (protect scarce experts), then free capacity,
    /// continuity on the project, and leave in the window. A human decides; the reasons are always shown.
    /// </summary>
    public async Task<IReadOnlyList<Candidate>> RankCandidatesAsync(int demandId, CancellationToken ct = default)
    {
        var d = await db.Demands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, ct) ?? throw new DemandException($"Demand {demandId} not found.");
        var capacity = new CapacityService(db);
        var rows = await capacity.GetCapacityAsync(d.StartDate, d.EndDate, ct: ct);
        var ratings = await db.PersonSkills.AsNoTracking().Where(r => r.SkillId == d.SkillId && r.Rating >= d.MinLevel).ToListAsync(ct);
        var onProject = d.ProjectId is { } pid
            ? (await db.TaskAssignments.AsNoTracking().Where(a => a.PersonId != null).Join(db.Tasks, a => a.TaskId, t => t.Id, (a, t) => new { a.PersonId, t.ProjectId })
                .Where(x => x.ProjectId == pid).Select(x => x.PersonId!.Value).Distinct().ToListAsync(ct)).ToHashSet()
            : [];

        var result = new List<Candidate>();
        foreach (var r in ratings)
        {
            var mine = rows.Where(x => x.PersonId == r.PersonId).ToList();
            if (mine.Count == 0) continue;
            var weeks = Math.Max(1m, (d.EndDate.DayNumber - d.StartDate.DayNumber + 1) / 7m);
            var freeWeekly = mine.Sum(x => x.FreePlannedHours) / weeks;
            var leaveFraction = mine.Sum(x => x.AvailableHours + x.LeaveHours) is var total and > 0 ? mine.Sum(x => x.LeaveHours) / total : 0m;
            var reasons = new List<string>();
            var score = 100m - 15m * (r.Rating - d.MinLevel);
            if (r.Rating > d.MinLevel) reasons.Add($"rating {r.Rating} is above the required {d.MinLevel} (costs {15 * (r.Rating - d.MinLevel)} points: keep experts for harder work)");
            else reasons.Add($"rating {r.Rating} matches the required level");
            score += Math.Min(40m, freeWeekly);
            reasons.Add($"{freeWeekly:0.#} free planned h/week over the window");
            var already = onProject.Contains(r.PersonId);
            if (already) { score += 10m; reasons.Add("already works on this project (continuity)"); }
            if (leaveFraction > 0) { score -= leaveFraction * 50m; reasons.Add($"{leaveFraction:P0} of the window is leave"); }
            if (freeWeekly < d.HoursPerWeek) reasons.Add($"free capacity is below the {d.HoursPerWeek:0.#} h/week required (partial fill)");
            result.Add(new Candidate(r.PersonId, mine[0].PersonName, r.Rating, Math.Round(freeWeekly, 1), already, leaveFraction, Math.Round(score, 1), reasons));
        }
        return result.OrderByDescending(c => c.Score).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    private static decimal SuppliedPerWeek(Demand d, List<Allocation> allocations)
    {
        var perMonth = new List<decimal>();
        for (var m = new DateOnly(d.StartDate.Year, d.StartDate.Month, 1); m <= d.EndDate; m = m.AddMonths(1))
        {
            var hours = allocations.Where(a => a.Month == m).Sum(a => a.Hours);
            perMonth.Add(hours / (DateTime.DaysInMonth(m.Year, m.Month) / 7m));
        }
        return perMonth.Count == 0 ? 0m : perMonth.Average();
    }

    private async Task ValidateAsync(Demand d, CancellationToken ct)
    {
        if (d.ProjectId is null && d.OpportunityId is null) throw new DemandException("A demand belongs to a project or an opportunity.");
        if (!await db.Skills.AnyAsync(s => s.Id == d.SkillId, ct)) throw new DemandException($"Skill {d.SkillId} not found.");
        if (d.EndDate < d.StartDate) throw new DemandException("The demand ends before it starts.");
        if (d.HoursPerWeek <= 0) throw new DemandException("Hours per week must be positive.");
        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var levels = resolver.Get<int>(SettingKeys.SkillLevelCount, SettingsResolver.Chain(), DateOnly.FromDateTime(DateTime.Today));
        if (d.MinLevel < 1 || d.MinLevel > levels) throw new DemandException($"Minimum level must be between 1 and {levels}.");
        if (d.StatusOverride is not null && !await EOS.Infrastructure.Configuration.ConfigurationService.IsValidAsync(db, EOS.Domain.Configuration.Vocabularies.DemandStatus, d.StatusOverride, ct))
            throw new DemandException($"'{d.StatusOverride}' is not an allowed demand status.");
    }
}
