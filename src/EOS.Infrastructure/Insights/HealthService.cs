using EOS.Domain.Planning;
using EOS.Domain.Settings;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Insights;

public sealed record ProjectHealthView(
    int ProjectId, string ProjectNo, string Name, string Health, bool IsOverride, string? OverrideReason,
    int OpenTasks, int DelayedTasks, decimal DelayRatio, int? ScheduleVarianceDays,
    decimal ProgressActual, decimal ProgressExpected, decimal ProgressGapPoints,
    DateOnly? PlannedEnd, DateOnly? ForecastEnd, IReadOnlyList<string> Reasons);

/// <summary>
/// Computed project health (spec §11.6): schedule variance of the forecast finish against the planned end, the share of open
/// tasks that are late, and the gap between expected and actual progress. A person may override with a recorded reason.
/// All thresholds are settings.
/// </summary>
public sealed class HealthService(EosDbContext db)
{
    public async Task<IReadOnlyList<ProjectHealthView>> GetAllAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var ids = await db.Tasks.Select(t => t.ProjectId).Distinct().ToListAsync(ct);
        var views = new List<ProjectHealthView>();
        foreach (var id in ids) views.Add(await GetAsync(id, asOf, ct));
        return views.OrderByDescending(v => v.Health == "At Risk").ThenByDescending(v => v.Health == "Moderate Risk").ThenBy(v => v.ProjectNo).ToList();
    }

    public async Task<ProjectHealthView> GetAsync(int projectId, DateOnly asOf, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId).ToListAsync(ct);
        var parents = tasks.Where(t => t.ParentId != null).Select(t => t.ParentId!.Value).ToHashSet();
        var leaves = tasks.Where(t => !parents.Contains(t.Id)).ToList();
        var cal = await ConfigurationService.LoadCalendarAsync(db, project.CalendarId, ct);
        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var chain = SettingsResolver.Chain(projectId: projectId.ToString());
        decimal Cfg(string key) => resolver.Get<decimal>(key, chain, asOf);

        var open = leaves.Where(t => t.PercentComplete < 1m).ToList();
        var delayed = open.Where(t => t.Status == "Delayed" || (t.PlannedFinish is { } pf && pf < asOf)).ToList();
        var delayRatio = open.Count == 0 ? 0m : (decimal)delayed.Count / open.Count;

        // progress: expected from planned dates, actual from percent complete, both weighted by work (min 1 h)
        var dated = leaves.Where(t => t.PlannedStart is not null && t.PlannedFinish is not null).ToList();
        decimal Weight(PlanTask t) => Math.Max(t.WorkHours, 1m);
        var weightSum = dated.Sum(Weight);
        decimal expected = 0m, actual = 0m;
        if (weightSum > 0)
        {
            foreach (var t in dated)
            {
                var span = t.PlannedFinish!.Value.DayNumber - t.PlannedStart!.Value.DayNumber + 1;
                var elapsed = asOf.DayNumber - t.PlannedStart.Value.DayNumber + 1;
                expected += Weight(t) * Math.Clamp((decimal)elapsed / Math.Max(1, span), 0m, 1m);
                actual += Weight(t) * t.PercentComplete;
            }
            expected /= weightSum; actual /= weightSum;
        }
        var gapPoints = Math.Round((expected - actual) * 100m, 1);

        var forecast = leaves.Where(t => t.ScheduledFinish is not null).Select(t => t.ScheduledFinish!.Value).DefaultIfEmpty().Max();
        DateOnly? forecastEnd = forecast == default ? null : forecast;
        var plannedEnd = project.PlannedEnd ?? project.ContractualEnd;
        int? variance = plannedEnd is { } pe && forecastEnd is { } fe ? cal.WorkingDaysBetween(pe, fe) : null;

        var reasons = new List<string>();
        var level = "On Track";
        if ((variance ?? 0) > Cfg(SettingKeys.HealthAtRiskVarianceDays) || delayRatio > Cfg(SettingKeys.HealthAtRiskDelayRatio) || gapPoints > Cfg(SettingKeys.HealthAtRiskProgressGapPoints))
            level = "At Risk";
        else if ((variance ?? 0) > Cfg(SettingKeys.HealthModerateVarianceDays) || delayRatio > Cfg(SettingKeys.HealthModerateDelayRatio) || gapPoints > Cfg(SettingKeys.HealthModerateProgressGapPoints))
            level = "Moderate Risk";

        if (variance is > 0) reasons.Add($"forecast finish is {variance} working day(s) after the planned end");
        if (delayed.Count > 0) reasons.Add($"{delayed.Count} of {open.Count} open task(s) are late");
        if (gapPoints > 0) reasons.Add($"progress is {gapPoints:0.#} points behind where the planned dates say it should be");
        if (reasons.Count == 0) reasons.Add("nothing flagged");

        var isOverride = !string.IsNullOrEmpty(project.HealthOverride);
        return new ProjectHealthView(project.Id, project.ProjectNo, project.Name, isOverride ? project.HealthOverride! : level, isOverride,
            project.HealthOverrideReason, open.Count, delayed.Count, Math.Round(delayRatio, 3), variance, Math.Round(actual, 3), Math.Round(expected, 3),
            gapPoints, plannedEnd, forecastEnd, reasons);
    }
}
