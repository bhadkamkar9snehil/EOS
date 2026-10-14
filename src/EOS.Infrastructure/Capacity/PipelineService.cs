using EOS.Domain.Audit;
using EOS.Domain.Projects;
using EOS.Domain.Settings;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Capacity;

public sealed record OpportunityView(
    int Id, string Code, string Name, string? LeadName, decimal? ExpectedHours, decimal WinProbability, decimal WeightedHours,
    DateOnly? LikelyBookingMonth, int? DurationMonths, string? Stage, string Status, string? RequiredSkills, int? ConvertedProjectId);

public sealed record SupplyDemandRow(
    int SkillId, string SkillName, DateOnly Month, decimal ProjectDemandHours, decimal PipelineHours, decimal SupplyHours,
    decimal GapHours, int DeployableCount);

/// <summary>Pipeline (opportunities) and the forward supply-vs-demand view per skill and month (spec §12.4-12.5).</summary>
public sealed class PipelineService(EosDbContext db)
{
    public async Task<Opportunity> AddAsync(Opportunity o, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(o.Code) || string.IsNullOrWhiteSpace(o.Name)) throw new DemandException("An opportunity needs a code and a name.");
        if (o.WinProbability is < 0 or > 1) throw new DemandException("Win probability must be between 0 and 1.");
        if (await db.Opportunities.AnyAsync(x => x.Code == o.Code, ct)) throw new DemandException($"Opportunity {o.Code} already exists.");
        db.Opportunities.Add(o);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "opportunity.add", SubjectKind = "opportunity", SubjectId = o.Code });
        await db.SaveChangesAsync(ct);
        return o;
    }

    public async Task<IReadOnlyList<OpportunityView>> ListAsync(CancellationToken ct = default) =>
        (await db.Opportunities.AsNoTracking().OrderBy(o => o.Code).ToListAsync(ct))
        .Select(o => new OpportunityView(o.Id, o.Code, o.Name, o.LeadName, o.ExpectedHours, o.WinProbability, Math.Round((o.ExpectedHours ?? 0m) * o.WinProbability, 1),
            o.LikelyBookingMonth, o.DurationMonths, o.Stage, o.Status, o.RequiredSkills, o.ConvertedProjectId)).ToList();

    /// <summary>A won opportunity becomes a project carrying its demand lines (spec §19.1-19.2).</summary>
    public async Task<Project> ConvertToProjectAsync(int opportunityId, string projectNo, string actor, CancellationToken ct = default)
    {
        var o = await db.Opportunities.SingleOrDefaultAsync(x => x.Id == opportunityId, ct) ?? throw new DemandException($"Opportunity {opportunityId} not found.");
        if (o.ConvertedProjectId is not null) throw new DemandException("This opportunity has already been converted.");
        if (await db.Projects.AnyAsync(p => p.ProjectNo == projectNo, ct)) throw new DemandException($"Project {projectNo} already exists.");
        var project = new Project { ProjectNo = projectNo, Name = o.Name, TrackerStatus = "Entered", PoStart = o.LikelyBookingMonth, IsSample = o.IsSample };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        foreach (var d in await db.Demands.Where(d => d.OpportunityId == o.Id).ToListAsync(ct)) { d.ProjectId = project.Id; d.OpportunityId = null; }
        o.Status = "Won"; o.ConvertedProjectId = project.Id;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "opportunity.convert", SubjectKind = "opportunity", SubjectId = o.Code, Detail = projectNo });
        await db.SaveChangesAsync(ct);
        return project;
    }

    /// <summary>
    /// Per skill and month: demand (project demand lines + weighted pipeline) against pooled free planned hours of deployable
    /// engineers. "Pooled" means an engineer's free hours are counted for every skill they are deployable in (spec §12.5).
    /// </summary>
    public async Task<IReadOnlyList<SupplyDemandRow>> SupplyDemandAsync(DateOnly fromMonth, int months, CancellationToken ct = default)
    {
        var first = new DateOnly(fromMonth.Year, fromMonth.Month, 1);
        var last = first.AddMonths(months).AddDays(-1);
        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var deployableMin = resolver.Get<int>(SettingKeys.SkillDeployableMinLevel, SettingsResolver.Chain(), first);

        var skills = await db.Skills.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var demands = await db.Demands.AsNoTracking().Where(d => d.EndDate >= first && d.StartDate <= last).ToListAsync(ct);
        var opportunities = (await db.Opportunities.AsNoTracking().Where(o => o.Status == "Expected").ToListAsync(ct));
        var oppWithDemands = demands.Where(d => d.OpportunityId != null).Select(d => d.OpportunityId!.Value).ToHashSet();
        var ratings = await db.PersonSkills.AsNoTracking().Where(r => r.Rating >= deployableMin).ToListAsync(ct);
        var capacity = (await new CapacityService(db).GetCapacityAsync(first, last, ct: ct)).ToList();

        var rows = new List<SupplyDemandRow>();
        foreach (var skill in skills)
        {
            var deployable = ratings.Where(r => r.SkillId == skill.Id).Select(r => r.PersonId).ToHashSet();
            for (var m = first; m <= last; m = m.AddMonths(1))
            {
                var monthEnd = m.AddMonths(1).AddDays(-1);
                decimal ProjectHours() => demands.Where(d => d.SkillId == skill.Id && d.ProjectId != null).Sum(d => OverlapWeeks(d.StartDate, d.EndDate, m, monthEnd) * d.HoursPerWeek);
                decimal PipelineHours()
                {
                    var total = demands.Where(d => d.SkillId == skill.Id && d.OpportunityId != null)
                        .Sum(d => OverlapWeeks(d.StartDate, d.EndDate, m, monthEnd) * d.HoursPerWeek * (opportunities.FirstOrDefault(o => o.Id == d.OpportunityId)?.WinProbability ?? 0m));
                    foreach (var o in opportunities.Where(o => !oppWithDemands.Contains(o.Id) && o.ExpectedHours > 0 && o.LikelyBookingMonth is not null))
                    {
                        var codes = (o.RequiredSkills ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (!codes.Contains(skill.Code, StringComparer.OrdinalIgnoreCase) && !codes.Contains(skill.Name, StringComparer.OrdinalIgnoreCase)) continue;
                        var start = new DateOnly(o.LikelyBookingMonth!.Value.Year, o.LikelyBookingMonth.Value.Month, 1);
                        var len = Math.Max(1, o.DurationMonths ?? 1);
                        if (m >= start && m < start.AddMonths(len)) total += o.ExpectedHours!.Value * o.WinProbability / len / codes.Length;
                    }
                    return total;
                }
                var supply = capacity.Where(c => c.Month == m && deployable.Contains(c.PersonId)).Sum(c => c.FreePlannedHours);
                var pd = Math.Round(ProjectHours(), 1);
                var pl = Math.Round(PipelineHours(), 1);
                rows.Add(new SupplyDemandRow(skill.Id, skill.Name, m, pd, pl, Math.Round(supply, 1), Math.Round(pd + pl - supply, 1), deployable.Count));
            }
        }
        return rows;
    }

    private static decimal OverlapWeeks(DateOnly start, DateOnly end, DateOnly monthStart, DateOnly monthEnd)
    {
        var s = start > monthStart ? start : monthStart;
        var e = end < monthEnd ? end : monthEnd;
        return e < s ? 0m : (e.DayNumber - s.DayNumber + 1) / 7m;
    }
}
