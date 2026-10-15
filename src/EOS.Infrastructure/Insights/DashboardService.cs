using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Insights;

public sealed record Kpis(
    int ActiveProjects, int AtRiskProjects, int DelayedTasks, int OverAllocatedEngineers, decimal FreePlannedHoursNext4Weeks,
    int OpenGaps, int PendingApprovals, int OpenDoubts, int VmsRunning, int VmsOff, int HostsLowOnDisk, int UnresolvedAliases);

public sealed record MilestoneView(int TaskId, string ProjectNo, string Name, DateOnly? Date);

public sealed record CommandCenter(
    DateOnly AsOf, Kpis Kpis, IReadOnlyList<ProjectHealthView> Projects, IReadOnlyList<Alert> Alerts,
    IReadOnlyList<WeeklyLoadRow> LoadNext8Weeks, IReadOnlyList<SupplyDemandRow> SupplyDemand, IReadOnlyList<MilestoneView> Milestones);

public sealed class DashboardService(EosDbContext db)
{
    public async Task<CommandCenter> GetAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var health = await new HealthService(db).GetAllAsync(asOf, ct);
        var alerts = await new AlertService(db).EvaluateAsync(asOf, ct);
        var capacity = new CapacityService(db);
        var weekStart = CapacityService.StartOfWeek(asOf);
        var weeks = await capacity.GetWeeklyLoadAsync(weekStart, weekStart.AddDays(8 * 7 - 1), ct: ct);
        var thisMonth = await capacity.GetCapacityAsync(asOf, asOf, ct: ct);
        var supply = await new PipelineService(db).SupplyDemandAsync(asOf, 3, ct);

        var firstWeeks = weeks.Where(w => w.WeekStart < asOf.AddDays(28)).ToList();
        var free = firstWeeks.Sum(w => Math.Max(0m, w.CapacityHours * 0.85m - w.AllocatedHours));

        var hosts = await db.Hosts.AsNoTracking().Where(h => !h.Removed).ToListAsync(ct);
        var lowDisk = alerts.Count(a => a.Rule == "A17");
        var activeStatuses = new[] { "Entered", "Under Development" };
        var kpis = new Kpis(
            ActiveProjects: await db.Projects.CountAsync(p => p.IsActive && p.TrackerStatus != null && activeStatuses.Contains(p.TrackerStatus), ct),
            AtRiskProjects: health.Count(h => h.Health == "At Risk"),
            DelayedTasks: health.Sum(h => h.DelayedTasks),
            OverAllocatedEngineers: thisMonth.Count(r => r.Band == LoadBand.OverAllocated),
            FreePlannedHoursNext4Weeks: Math.Round(free, 0),
            OpenGaps: await db.ChangeGapLog.CountAsync(g => g.Status == "Open", ct),
            PendingApprovals: await db.Scenarios.CountAsync(s => s.Status == "Analysed" || s.Status == "Approved", ct) + await db.Movements.CountAsync(m => m.Status == "Requested", ct),
            OpenDoubts: await db.Doubts.CountAsync(d => d.Result != EOS.Domain.Training.DoubtResult.Solved, ct),
            VmsRunning: hosts.Count(h => h.Kind == "vm" && h.State == "Running"),
            VmsOff: hosts.Count(h => h.Kind == "vm" && h.State == "Off"),
            HostsLowOnDisk: lowDisk,
            UnresolvedAliases: await db.PersonAliases.CountAsync(a => a.PersonId == null, ct));

        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.ProjectNo, ct);
        var milestones = (await db.Tasks.AsNoTracking().Where(t => t.Kind == "milestone" && t.ScheduledStart >= asOf && t.ScheduledStart <= asOf.AddDays(30)).OrderBy(t => t.ScheduledStart).Take(20).ToListAsync(ct))
            .Select(t => new MilestoneView(t.Id, projects.GetValueOrDefault(t.ProjectId, "?"), t.Name, t.ScheduledStart)).ToList();

        return new CommandCenter(asOf, kpis, health, alerts.Take(60).ToList(), weeks, supply, milestones);
    }
}
