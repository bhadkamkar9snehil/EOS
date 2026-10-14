using EOS.Domain.Planning;
using EOS.Domain.Settings;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Capacity;

public enum LoadBand
{
    UnderUsed,
    Healthy,
    Stretched,
    OverAllocated,
}

public sealed record ProjectHours(int ProjectId, string ProjectNo, decimal Hours, string Source);

/// <summary>One person-month of capacity (spec §8). All thresholds come from settings.</summary>
public sealed record CapacityRow(
    int PersonId, string PersonName, string? Team, DateOnly Month,
    decimal AvailableHours, decimal PlannedHours, decimal LeaveHours,
    decimal AllocatedHours, decimal ProposedHours,
    decimal LoadVsPlanned, decimal LoadVsAvailable, LoadBand Band,
    IReadOnlyList<ProjectHours> ByProject)
{
    public decimal FreePlannedHours => Math.Max(0m, PlannedHours - AllocatedHours);
    public decimal FreeAvailableHours => Math.Max(0m, AvailableHours - AllocatedHours);
}

public sealed record WeeklyLoadRow(
    int PersonId, string PersonName, DateOnly WeekStart, decimal CapacityHours, decimal AllocatedHours,
    decimal LoadVsCapacity, LoadBand Band, IReadOnlyList<ProjectHours> ByProject);

/// <summary>
/// Capacity, allocation and load. Hours-in-a-day come from ONE model: plan task assignments over their scheduled dates
/// (hours/day x units), falling back to manual monthly allocations (spread evenly over working days) for projects where a
/// person has no plan assignments (spec §13.2). Nothing is hard-coded: hours, calendar, availability and bands are settings/data.
/// </summary>
public sealed class CapacityService(EosDbContext db)
{
    private sealed record Daily(int PersonId, int ProjectId, DateOnly Day, decimal Hours, string Source);

    public async Task<IReadOnlyList<CapacityRow>> GetCapacityAsync(
        DateOnly from, DateOnly to, int? teamId = null, int? personId = null, CancellationToken ct = default)
    {
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        var ctx = await LoadAsync(firstMonth, lastMonth.AddMonths(1).AddDays(-1), teamId, personId, ct);
        var rows = new List<CapacityRow>();

        foreach (var person in ctx.People)
        {
            var chain = SettingsResolver.Chain(personId: person.Id.ToString(), teamId: person.TeamId?.ToString());
            for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
            {
                var monthEnd = month.AddMonths(1).AddDays(-1);
                var asOf = month;
                var util = ctx.Resolver.Get<decimal>(SettingKeys.TargetUtilisation, chain, asOf);
                var proposedCounts = ctx.Resolver.Get<bool>(SettingKeys.ProposedCountsTowardLoad, chain, asOf);
                var mode = ctx.Resolver.Get<string>(SettingKeys.CapacityMode, chain, asOf);
                var overAbove = ctx.Resolver.Get<decimal>(SettingKeys.LoadOverAllocatedAbove, chain, asOf);
                var underBelow = ctx.Resolver.Get<decimal>(SettingKeys.LoadUnderUsedBelow, chain, asOf);
                var penalty = ctx.Resolver.Get<decimal>(SettingKeys.ContextSwitchPenaltyPerExtraProject, chain, asOf);

                var baseHours = mode == "fixedMonthly"
                    ? ctx.Resolver.Get<decimal>(SettingKeys.StandardHoursPerMonth, chain, asOf)
                    : WorkingDays(ctx.Calendar, month, monthEnd) * ctx.Calendar.HoursPerDay;
                baseHours *= person.AvailabilityFraction;

                var leaveHours = LeaveDays(ctx, person.Id, month, monthEnd) * ctx.Calendar.HoursPerDay * person.AvailabilityFraction;
                var available = Math.Max(0m, baseHours - leaveHours);
                var planned = available * util;

                var days = ctx.Daily.Where(d => d.PersonId == person.Id && d.Day >= month && d.Day <= monthEnd).ToList();
                var byProject = days.GroupBy(d => d.ProjectId)
                    .Select(g => new ProjectHours(g.Key, ctx.ProjectNos.GetValueOrDefault(g.Key, "?"), Math.Round(g.Sum(x => x.Hours), 2), g.First().Source))
                    .OrderByDescending(p => p.Hours).ToList();
                var allocated = byProject.Sum(p => p.Hours);

                var proposed = ctx.ProposedManual.Where(a => a.PersonId == person.Id && a.Month == month).Sum(a => a.Hours);
                var concurrent = byProject.Count(p => p.Hours > 0);
                if (penalty > 0 && concurrent > 2) allocated *= 1m + penalty * (concurrent - 2);

                var counted = allocated + (proposedCounts ? proposed : 0m);
                var loadPlanned = planned > 0 ? counted / planned : (counted > 0 ? decimal.MaxValue : 0m);
                var loadAvail = available > 0 ? counted / available : (counted > 0 ? decimal.MaxValue : 0m);
                rows.Add(new CapacityRow(person.Id, person.FullName, person.TeamName, month, available, planned, leaveHours,
                    allocated, proposed, Cap(loadPlanned), Cap(loadAvail), Band(loadPlanned, loadAvail, underBelow, overAbove), byProject));
            }
        }
        return rows;
    }

    /// <summary>Hours per person per week (Monday start) against weekly capacity, red when over capacity (spec §8.5, §20.2).</summary>
    public async Task<IReadOnlyList<WeeklyLoadRow>> GetWeeklyLoadAsync(
        DateOnly from, DateOnly to, int? teamId = null, int? personId = null, CancellationToken ct = default)
    {
        var start = StartOfWeek(from);
        var ctx = await LoadAsync(start, to, teamId, personId, ct);
        var rows = new List<WeeklyLoadRow>();
        foreach (var person in ctx.People)
        {
            var chain = SettingsResolver.Chain(personId: person.Id.ToString(), teamId: person.TeamId?.ToString());
            var overAbove = ctx.Resolver.Get<decimal>(SettingKeys.LoadOverAllocatedAbove, chain, start);
            var underBelow = ctx.Resolver.Get<decimal>(SettingKeys.LoadUnderUsedBelow, chain, start);
            var util = ctx.Resolver.Get<decimal>(SettingKeys.TargetUtilisation, chain, start);

            for (var week = start; week <= to; week = week.AddDays(7))
            {
                var weekEnd = week.AddDays(6);
                var capacity = Math.Max(0m, (WorkingDays(ctx.Calendar, week, weekEnd) - LeaveDays(ctx, person.Id, week, weekEnd)) * ctx.Calendar.HoursPerDay * person.AvailabilityFraction);
                var days = ctx.Daily.Where(d => d.PersonId == person.Id && d.Day >= week && d.Day <= weekEnd).ToList();
                var byProject = days.GroupBy(d => d.ProjectId)
                    .Select(g => new ProjectHours(g.Key, ctx.ProjectNos.GetValueOrDefault(g.Key, "?"), Math.Round(g.Sum(x => x.Hours), 2), g.First().Source))
                    .OrderByDescending(p => p.Hours).ToList();
                var allocated = byProject.Sum(p => p.Hours);
                var load = capacity > 0 ? allocated / capacity : (allocated > 0 ? decimal.MaxValue : 0m);
                rows.Add(new WeeklyLoadRow(person.Id, person.FullName, week, capacity, allocated, Cap(load),
                    Band(capacity * util > 0 ? allocated / (capacity * util) : (allocated > 0 ? decimal.MaxValue : 0m), load, underBelow, overAbove), byProject));
            }
        }
        return rows;
    }

    /// <summary>Free planned hours of one person in a month (used by candidate ranking and supply analysis).</summary>
    public async Task<decimal> FreePlannedHoursAsync(int personId, DateOnly month, CancellationToken ct = default) =>
        (await GetCapacityAsync(month, month, personId: personId, ct: ct)).SingleOrDefault()?.FreePlannedHours ?? 0m;

    // ---- internals ------------------------------------------------------------------------------------------

    private sealed record PersonInfo(int Id, string FullName, int? TeamId, string? TeamName, decimal AvailabilityFraction);

    private sealed class LoadContext
    {
        public required List<PersonInfo> People { get; init; }
        public required WorkCalendar Calendar { get; init; }
        public required SettingsResolver Resolver { get; init; }
        public required List<Daily> Daily { get; init; }
        public required List<EOS.Domain.Capacity.Allocation> ProposedManual { get; init; }
        public required List<EOS.Domain.Capacity.Leave> Leaves { get; init; }
        public required Dictionary<int, string> ProjectNos { get; init; }
    }

    private async Task<LoadContext> LoadAsync(DateOnly from, DateOnly to, int? teamId, int? personId, CancellationToken ct)
    {
        var people = await (from p in db.People.AsNoTracking()
                            where p.IsActive && (teamId == null || p.TeamId == teamId) && (personId == null || p.Id == personId)
                            join t in db.Teams.AsNoTracking() on p.TeamId equals t.Id into teams
                            from t in teams.DefaultIfEmpty()
                            orderby p.FullName
                            select new PersonInfo(p.Id, p.FullName, p.TeamId, t == null ? null : t.Name, p.AvailabilityFraction)).ToListAsync(ct);
        var cal = await ConfigurationService.LoadCalendarAsync(db, null, ct);
        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var leaves = await db.Leaves.AsNoTracking().Where(l => l.ToDate >= from && l.FromDate <= to).ToListAsync(ct);
        var projectNos = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.ProjectNo, ct);

        // 1) plan-derived daily hours
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ScheduledStart != null && t.ScheduledFinish != null && t.Kind == TaskKinds.Task).ToListAsync(ct);
        var parentIds = tasks.Where(t => t.ParentId != null).Select(t => t.ParentId!.Value).ToHashSet();
        var allParents = await db.Tasks.AsNoTracking().Where(t => t.ParentId != null).Select(t => t.ParentId!.Value).Distinct().ToListAsync(ct);
        var summaryIds = parentIds.Union(allParents).ToHashSet();
        var leafTasks = tasks.Where(t => !summaryIds.Contains(t.Id)).ToDictionary(t => t.Id);
        var assignments = await db.TaskAssignments.AsNoTracking().Where(a => a.PersonId != null).ToListAsync(ct);

        var daily = new List<Daily>();
        var planPairs = new HashSet<(int Person, int Project)>();
        foreach (var a in assignments)
        {
            if (!leafTasks.TryGetValue(a.TaskId, out var t)) continue;
            var start = Max(t.ScheduledStart!.Value, a.FromDate ?? DateOnly.MinValue);
            var finish = Min(t.ScheduledFinish!.Value, a.ToDate ?? DateOnly.MaxValue);
            planPairs.Add((a.PersonId!.Value, t.ProjectId));
            if (finish < from || start > to) continue;
            for (var d = Max(start, from); d <= Min(finish, to); d = d.AddDays(1))
                if (cal.IsWorkingDay(d))
                    daily.Add(new Daily(a.PersonId!.Value, t.ProjectId, d, cal.HoursPerDay * a.Units, "plan"));
        }

        // 2) manual monthly allocations for pairs with no plan assignments (Confirmed counts; Proposed is reported separately)
        var manual = await db.Allocations.AsNoTracking().Where(a => a.Month >= new DateOnly(from.Year, from.Month, 1) && a.Month <= to).ToListAsync(ct);
        foreach (var a in manual.Where(a => a.Status == "Confirmed" && !planPairs.Contains((a.PersonId, a.ProjectId))))
        {
            var monthStart = a.Month;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var workDays = Enumerable.Range(0, (monthEnd.DayNumber - monthStart.DayNumber) + 1).Select(i => monthStart.AddDays(i)).Where(cal.IsWorkingDay).ToList();
            if (workDays.Count == 0) continue;
            foreach (var d in workDays.Where(d => d >= from && d <= to))
                daily.Add(new Daily(a.PersonId, a.ProjectId, d, a.Hours / workDays.Count, "manual"));
        }
        var proposed = manual.Where(a => a.Status == "Proposed").ToList();

        return new LoadContext { People = people, Calendar = cal, Resolver = resolver, Daily = daily, ProposedManual = proposed, Leaves = leaves, ProjectNos = projectNos };
    }

    private static int WorkingDays(WorkCalendar cal, DateOnly from, DateOnly to)
    {
        var n = 0;
        for (var d = from; d <= to; d = d.AddDays(1)) if (cal.IsWorkingDay(d)) n++;
        return n;
    }

    private static int LeaveDays(LoadContext ctx, int personId, DateOnly from, DateOnly to)
    {
        var days = new HashSet<DateOnly>();
        foreach (var l in ctx.Leaves.Where(l => l.PersonId == personId))
            for (var d = Max(l.FromDate, from); d <= Min(l.ToDate, to); d = d.AddDays(1))
                if (ctx.Calendar.IsWorkingDay(d)) days.Add(d);
        return days.Count;
    }

    private static LoadBand Band(decimal loadVsPlanned, decimal loadVsAvailable, decimal underBelow, decimal overAbove) =>
        loadVsAvailable > overAbove ? LoadBand.OverAllocated
        : loadVsPlanned > 1m ? LoadBand.Stretched
        : loadVsPlanned < underBelow ? LoadBand.UnderUsed
        : LoadBand.Healthy;

    private static decimal Cap(decimal v) => v == decimal.MaxValue ? 999m : Math.Round(v, 4);
    internal static DateOnly StartOfWeek(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
