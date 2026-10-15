using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.People;

public sealed record HourBucket(string Label, decimal Hours);

public sealed record MonthHours(DateOnly Month, decimal Hours, decimal ProjectHours);

public sealed record SkillEvidence(string Skill, decimal Hours, int CurrentRating);

public sealed record PersonTimesheetStats(
    int PersonId, DateOnly From, DateOnly To, decimal TotalHours, decimal ProjectHours, decimal InternalHours, int DaysWorked,
    decimal ProductiveHoursPerDay, decimal? SuggestedAvailability, IReadOnlyList<MonthHours> Months,
    IReadOnlyList<HourBucket> Projects, IReadOnlyList<HourBucket> Activities, IReadOnlyList<SkillEvidence> Skills);

public sealed record ProjectActuals(string ProjectNo, decimal TotalHours, IReadOnlyList<HourBucket> ByPerson, IReadOnlyList<HourBucket> ByPhase, IReadOnlyList<MonthHours> ByMonth);

public sealed class TimesheetService(EosDbContext db)
{
    public async Task<PersonTimesheetStats> PersonAsync(int personId, DateOnly asOf, int months = 6, CancellationToken ct = default)
    {
        var to = asOf; var from = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-(months - 1));
        var rows = await db.Timesheets.AsNoTracking().Where(t => t.PersonId == personId && t.Date >= from && t.Date <= to)
            .Select(t => new { t.Date, t.Hours, t.ProjectNo, t.ProjectName, t.Activity, t.Milestone, t.Task }).ToListAsync(ct);

        var total = rows.Sum(r => r.Hours);
        var project = rows.Where(r => !string.IsNullOrEmpty(r.ProjectNo)).Sum(r => r.Hours);
        var days = rows.Select(r => r.Date).Distinct().Count();
        var workingDays = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).Count(d => d.DayOfWeek != DayOfWeek.Sunday);
        var perDay = workingDays == 0 ? 0m : project / workingDays;

        var monthly = Enumerable.Range(0, 12).Select(i => new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-11 + i)).ToList();
        var history = await db.Timesheets.AsNoTracking().Where(t => t.PersonId == personId && t.Date >= monthly[0])
            .Select(t => new { t.Date, t.Hours, t.ProjectNo }).ToListAsync(ct);
        var series = monthly.Select(m =>
        {
            var inMonth = history.Where(h => h.Date.Year == m.Year && h.Date.Month == m.Month).ToList();
            return new MonthHours(m, inMonth.Sum(h => h.Hours), inMonth.Where(h => !string.IsNullOrEmpty(h.ProjectNo)).Sum(h => h.Hours));
        }).ToList();

        var projects = rows.Where(r => !string.IsNullOrEmpty(r.ProjectNo)).GroupBy(r => r.ProjectNo!)
            .Select(g => new HourBucket(g.Key + " " + Trim(g.Select(x => x.ProjectName).FirstOrDefault(n => n is not null) ?? "", 36), g.Sum(x => x.Hours)))
            .OrderByDescending(b => b.Hours).Take(6).ToList();
        var activities = rows.Where(r => !string.IsNullOrEmpty(r.Activity)).GroupBy(r => r.Activity!)
            .Select(g => new HourBucket(g.Key, g.Sum(x => x.Hours))).OrderByDescending(b => b.Hours).Take(6).ToList();

        var allRows = await db.Timesheets.AsNoTracking().Where(t => t.PersonId == personId).Select(t => new { t.Hours, t.Activity, t.Milestone, t.Task }).ToListAsync(ct);
        var skills = await db.Skills.AsNoTracking().ToListAsync(ct);
        var ratings = await db.PersonSkills.AsNoTracking().Where(r => r.PersonId == personId).ToDictionaryAsync(r => r.SkillId, r => r.Rating, ct);
        var evidence = new List<SkillEvidence>();
        foreach (var s in skills)
        {
            var names = new[] { s.Name, s.Code }.Where(n => n.Length >= 3).ToArray();
            var hours = allRows.Where(r => names.Any(n => Mentions(r.Activity, n) || Mentions(r.Milestone, n) || Mentions(r.Task, n))).Sum(r => r.Hours);
            if (hours > 0) evidence.Add(new SkillEvidence(s.Name, hours, ratings.GetValueOrDefault(s.Id)));
        }

        return new PersonTimesheetStats(personId, from, to, total, project, total - project, days, perDay, days < workingDays * 0.6m ? null : Math.Round(Math.Clamp(perDay / 8m, 0.2m, 1m), 2),
            series, projects, activities, evidence.OrderByDescending(e => e.Hours).Take(8).ToList());
    }

    public async Task<ProjectActuals> ProjectAsync(string projectNo, CancellationToken ct = default)
    {
        var rows = await db.Timesheets.AsNoTracking().Where(t => t.ProjectNo == projectNo)
            .Join(db.People, t => t.PersonId, p => p.Id, (t, p) => new { t.Date, t.Hours, t.Milestone, p.FullName }).ToListAsync(ct);
        var byPerson = rows.GroupBy(r => r.FullName).Select(g => new HourBucket(g.Key, g.Sum(x => x.Hours))).OrderByDescending(b => b.Hours).ToList();
        var byPhase = rows.GroupBy(r => r.Milestone ?? "(none)").Select(g => new HourBucket(g.Key, g.Sum(x => x.Hours))).OrderByDescending(b => b.Hours).ToList();
        var byMonth = rows.GroupBy(r => new DateOnly(r.Date.Year, r.Date.Month, 1)).OrderBy(g => g.Key).Select(g => new MonthHours(g.Key, g.Sum(x => x.Hours), g.Sum(x => x.Hours))).ToList();
        return new ProjectActuals(projectNo, rows.Sum(r => r.Hours), byPerson, byPhase, byMonth);
    }

    private static bool Mentions(string? text, string needle) => text is not null && text.Contains(needle, StringComparison.OrdinalIgnoreCase);
    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
