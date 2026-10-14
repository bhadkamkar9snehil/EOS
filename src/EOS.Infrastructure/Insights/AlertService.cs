using EOS.Domain.Settings;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Insights;

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Serious = 2,
    Critical = 3,
}

/// <summary>A rule-driven finding with the numbers that triggered it (spec §22). Every threshold is a setting.</summary>
public sealed record Alert(string Rule, AlertSeverity Severity, string SubjectKind, string? SubjectId, string Subject, string Message);

public sealed class AlertService(EosDbContext db)
{
    public async Task<IReadOnlyList<Alert>> EvaluateAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var alerts = new List<Alert>();
        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var chain = SettingsResolver.Chain();
        int Int(string key) => resolver.Get<int>(key, chain, asOf);
        decimal Dec(string key) => resolver.Get<decimal>(key, chain, asOf);

        await CapacityAlertsAsync(alerts, asOf, Int(SettingKeys.AlertMaxConcurrentProjects), ct);
        await DemandAlertsAsync(alerts, asOf, Int(SettingKeys.AlertDemandStartWarnDays), Int(SettingKeys.AlertBusFactorMax), Int(SettingKeys.SkillDeployableMinLevel), ct);
        await ProjectAlertsAsync(alerts, asOf, Int(SettingKeys.AlertTrackerStaleDays), ct);
        await GovernanceAlertsAsync(alerts, asOf, Int(SettingKeys.AlertPendingApprovalDays), ct);
        await TrainingAlertsAsync(alerts, asOf, Int(SettingKeys.AlertDoubtUnassignedDays), Int(SettingKeys.AlertDoubtOpenDays), Int(SettingKeys.SkillStaleMonths), ct);
        await InfrastructureAlertsAsync(alerts, asOf, Dec(SettingKeys.HostDiskFreeWarnFraction), Int(SettingKeys.VmStaleDays), ct);
        await DataQualityAlertsAsync(alerts, ct);

        return alerts.OrderByDescending(a => a.Severity).ThenBy(a => a.Rule).ThenBy(a => a.Subject).ToList();
    }

    private async Task CapacityAlertsAsync(List<Alert> alerts, DateOnly asOf, int maxConcurrent, CancellationToken ct)
    {
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        var rows = await new CapacityService(db).GetCapacityAsync(month, month.AddMonths(1), ct: ct);
        foreach (var r in rows)
        {
            var label = $"{r.PersonName}, {r.Month:MMM yyyy}";
            if (r.Band == LoadBand.OverAllocated)
                alerts.Add(new Alert("A01", AlertSeverity.Serious, "person", r.PersonId.ToString(), label, $"allocated {r.AllocatedHours:0.#} h against {r.AvailableHours:0.#} h available ({r.LoadVsAvailable:P0})"));
            else if (r.Band == LoadBand.Stretched)
                alerts.Add(new Alert("A02", AlertSeverity.Warning, "person", r.PersonId.ToString(), label, $"allocated {r.AllocatedHours:0.#} h, above the {r.PlannedHours:0.#} h planned at target utilisation"));
            if (r.ByProject.Count(p => p.Hours > 0) > maxConcurrent)
                alerts.Add(new Alert("A12", AlertSeverity.Warning, "person", r.PersonId.ToString(), label, $"on {r.ByProject.Count(p => p.Hours > 0)} concurrent projects (limit {maxConcurrent})"));
            if (r.Band == LoadBand.UnderUsed && r.AllocatedHours == 0 && r.AvailableHours > 0 && r.Month == month)
                alerts.Add(new Alert("A13", AlertSeverity.Info, "person", r.PersonId.ToString(), label, $"no allocation this month; {r.FreePlannedHours:0.#} planned hours free"));
        }
    }

    private async Task DemandAlertsAsync(List<Alert> alerts, DateOnly asOf, int startWarnDays, int busFactorMax, int deployableMin, CancellationToken ct)
    {
        var demands = await new DemandService(db).ListAsync(ct: ct);
        foreach (var d in demands.Where(d => d.End >= asOf))
        {
            var label = $"{d.ProjectNo ?? "opportunity " + d.OpportunityId}: {d.SkillName} >= {d.MinLevel}";
            if (d.EligibleCount == 0)
                alerts.Add(new Alert("A04", AlertSeverity.Critical, "demand", d.Id.ToString(), label, "no engineer is eligible (skill gap): train, pair or hire"));
            if (d.Status != "Filled" && d.Start <= asOf.AddDays(startWarnDays))
                alerts.Add(new Alert("A03", AlertSeverity.Serious, "demand", d.Id.ToString(), label, $"{d.Status}; starts {d.Start:dd MMM yyyy}; supplied {d.SuppliedHoursPerWeek:0.#} of {d.HoursPerWeek:0.#} h/week"));
        }

        var inDemand = demands.Where(d => d.End >= asOf).Select(d => d.SkillId).ToHashSet();
        var skills = await db.Skills.AsNoTracking().ToListAsync(ct);
        var ratings = await db.PersonSkills.AsNoTracking().Where(r => r.Rating >= deployableMin).ToListAsync(ct);
        foreach (var s in skills.Where(s => inDemand.Contains(s.Id)))
        {
            var n = ratings.Count(r => r.SkillId == s.Id);
            if (n <= busFactorMax)
                alerts.Add(new Alert("A05", AlertSeverity.Warning, "skill", s.Id.ToString(), s.Name, $"only {n} deployable engineer(s) (level {deployableMin}+) for a skill with upcoming demand"));
        }
    }

    private async Task ProjectAlertsAsync(List<Alert> alerts, DateOnly asOf, int staleDays, CancellationToken ct)
    {
        foreach (var h in await new HealthService(db).GetAllAsync(asOf, ct))
        {
            if (h.Health == "At Risk")
                alerts.Add(new Alert("A06", AlertSeverity.Serious, "project", h.ProjectId.ToString(), $"{h.ProjectNo} {Trim(h.Name)}", string.Join("; ", h.Reasons)));
        }

        var parents = await db.Tasks.Where(t => t.ParentId != null).Select(t => t.ParentId!.Value).Distinct().ToListAsync(ct);
        var critical = await db.Tasks.AsNoTracking()
            .Where(t => t.IsCritical && t.PercentComplete < 1m && t.Status == "Delayed" && !parents.Contains(t.Id)).ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.ProjectNo, ct);
        foreach (var t in critical)
            alerts.Add(new Alert("A07", AlertSeverity.Serious, "task", t.Id.ToString(), $"{projects.GetValueOrDefault(t.ProjectId)}: {t.Name}", "a critical-path task is delayed"));

        var withPlan = await db.Tasks.Select(t => t.ProjectId).Distinct().ToListAsync(ct);
        foreach (var p in await db.Projects.AsNoTracking().Where(p => withPlan.Contains(p.Id) && p.TrackerStatus == "Under Development").ToListAsync(ct))
            if (p.LastStatusUpdate is { } u && asOf.DayNumber - u.DayNumber > staleDays)
                alerts.Add(new Alert("A08", AlertSeverity.Warning, "project", p.Id.ToString(), $"{p.ProjectNo} {Trim(p.Name)}", $"tracker last updated {u:dd MMM yyyy} ({asOf.DayNumber - u.DayNumber} days ago)"));
    }

    private async Task GovernanceAlertsAsync(List<Alert> alerts, DateOnly asOf, int pendingDays, CancellationToken ct)
    {
        var today = asOf.ToDateTime(TimeOnly.MinValue);
        foreach (var s in await db.Scenarios.AsNoTracking().Where(s => s.Status == "Analysed").ToListAsync(ct))
            if ((today - s.CreatedUtc.Date).TotalDays > pendingDays)
                alerts.Add(new Alert("A11", AlertSeverity.Warning, "scenario", s.Id.ToString(), s.Name, $"awaiting a decision since {s.CreatedUtc:dd MMM yyyy}"));
        foreach (var s in await db.Scenarios.AsNoTracking().Where(s => s.Status == "Approved").ToListAsync(ct))
            alerts.Add(new Alert("A10", AlertSeverity.Warning, "scenario", s.Id.ToString(), s.Name, "approved but not yet applied"));
        foreach (var m in await db.Movements.AsNoTracking().Where(m => m.Status == "Requested").ToListAsync(ct))
            if ((today - m.CreatedUtc.Date).TotalDays > pendingDays)
                alerts.Add(new Alert("A11", AlertSeverity.Warning, "movement", m.Id.ToString(), $"movement #{m.Id}", $"requested {m.CreatedUtc:dd MMM yyyy}, still pending"));
    }

    private async Task TrainingAlertsAsync(List<Alert> alerts, DateOnly asOf, int unassignedDays, int openDays, int staleMonths, CancellationToken ct)
    {
        foreach (var d in await db.Doubts.AsNoTracking().Where(d => d.Result != EOS.Domain.Training.DoubtResult.Solved && d.RaisedOn != null).ToListAsync(ct))
        {
            var age = asOf.DayNumber - d.RaisedOn!.Value.DayNumber;
            var label = $"{d.Module} / {d.SubModule}";
            if (d.AssignedToName is null && age > unassignedDays)
                alerts.Add(new Alert("A14", AlertSeverity.Warning, "doubt", d.Id.ToString(), label, $"unassigned for {age} days"));
            else if (age > openDays)
                alerts.Add(new Alert("A14", AlertSeverity.Info, "doubt", d.Id.ToString(), label, $"open for {age} days"));
        }

        var cutoff = asOf.AddMonths(-staleMonths);
        var people = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        var skills = await db.Skills.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        foreach (var r in await db.PersonSkills.AsNoTracking().Where(r => r.AssessedOn != null && r.AssessedOn < cutoff).ToListAsync(ct))
            alerts.Add(new Alert("A15", AlertSeverity.Info, "person", r.PersonId.ToString(), $"{people.GetValueOrDefault(r.PersonId)} / {skills.GetValueOrDefault(r.SkillId)}",
                $"rating last reviewed {r.AssessedOn:dd MMM yyyy} (older than {staleMonths} months): consider a review"));
    }

    private async Task InfrastructureAlertsAsync(List<Alert> alerts, DateOnly asOf, decimal diskWarn, int vmStaleDays, CancellationToken ct)
    {
        var hosts = await db.Hosts.AsNoTracking().Where(h => !h.Removed).ToListAsync(ct);
        var disks = await db.HostDisks.AsNoTracking().ToListAsync(ct);
        foreach (var d in disks.GroupBy(d => (d.HostId, d.Drive)).Select(g => g.OrderByDescending(x => x.CapturedOn).First()))
            if (d.TotalGb > 0 && d.FreeGb / d.TotalGb < diskWarn)
            {
                var host = hosts.FirstOrDefault(h => h.Id == d.HostId);
                alerts.Add(new Alert("A17", AlertSeverity.Serious, "host", d.HostId.ToString(), $"{host?.Ip} {host?.Name}", $"drive {d.Drive}: only {d.FreeGb:0.##} of {d.TotalGb:0.##} GB free ({d.FreeGb / d.TotalGb:P1})"));
            }

        var finished = (await db.Projects.AsNoTracking().Where(p => p.TrackerStatus == "Completed" || p.ErpStatus == "CLS").Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var links = await db.ProjectHosts.AsNoTracking().ToListAsync(ct);
        foreach (var vm in hosts.Where(h => h.Kind == "vm"))
        {
            if (vm.State == "Running" && links.Any(l => l.HostId == vm.Id && finished.Contains(l.ProjectId)))
                alerts.Add(new Alert("A18", AlertSeverity.Warning, "host", vm.Id.ToString(), vm.Name, "running although its project is completed/closed: decommission?"));
            if (vm.State == "Off" && (vm.MarkedForDeletion == true || string.Equals(vm.Purpose, "Unknown", StringComparison.OrdinalIgnoreCase)))
                alerts.Add(new Alert("A19", AlertSeverity.Info, "host", vm.Id.ToString(), vm.Name, "off, purpose unknown or marked for deletion: reclaim candidate"));
            if (vm.NeedBackup == true && (vm.LastBackupOn is null || asOf.DayNumber - vm.LastBackupOn.Value.DayNumber > vmStaleDays))
                alerts.Add(new Alert("A20", AlertSeverity.Warning, "host", vm.Id.ToString(), vm.Name, "needs backup but no recent backup is recorded"));
        }
    }

    private async Task DataQualityAlertsAsync(List<Alert> alerts, CancellationToken ct)
    {
        var unresolved = await db.PersonAliases.AsNoTracking().Where(a => a.PersonId == null).Select(a => a.Alias).ToListAsync(ct);
        if (unresolved.Count > 0)
            alerts.Add(new Alert("A21", AlertSeverity.Info, "data", null, "unresolved people", $"{unresolved.Count} alias(es) need mapping to a person: {string.Join(", ", unresolved.Take(12))}"));
        var noTeam = await db.People.CountAsync(p => p.IsActive && p.TeamId == null, ct);
        if (noTeam > 0) alerts.Add(new Alert("A21", AlertSeverity.Info, "data", null, "people without a team", $"{noTeam} active person(s) are not assigned to a team"));
    }

    private static string Trim(string s) => s.Length <= 50 ? s : s[..49] + "…";
}
