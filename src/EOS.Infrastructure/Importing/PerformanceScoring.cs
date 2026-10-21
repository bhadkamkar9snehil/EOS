using EOS.Domain.People;
using EOS.Domain.Settings;
using EOS.Infrastructure.Persistence;

namespace EOS.Infrastructure.Importing;

/// <summary>The V1 operational score, with every weight read from settings (seed defaults match V1).</summary>
internal sealed record PerformanceWeights(
    decimal Timesheet, decimal Approval, decimal Attendance, decimal Fill, decimal Punch, decimal Duration, decimal Punctuality)
{
    public static async Task<PerformanceWeights> LoadAsync(EosDbContext db, CancellationToken ct)
    {
        var r = await new SettingsStore(db).LoadResolverAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        decimal G(string key) => r.Get<decimal>(key, SettingsResolver.Chain(), today);
        return new(G(SettingKeys.PerfWeightTimesheet), G(SettingKeys.PerfWeightApproval), G(SettingKeys.PerfWeightAttendance),
            G(SettingKeys.PerfAttendanceFill), G(SettingKeys.PerfAttendancePunch), G(SettingKeys.PerfAttendanceDuration), G(SettingKeys.PerfAttendancePunctuality));
    }

    public void Apply(MonthlyPerformance m)
    {
        m.TimesheetCompletionScore = Pct(m.EnteredHours, m.ComplianceHours);
        m.ApprovalScore = Pct(m.ApprovedHours, m.EnteredHours);
        m.AttendanceDisciplineScore = 0;
        if (m.ExpectedTimesheetDays > 0)
        {
            var fill = Pct(m.TimesheetFilledDays, m.ExpectedTimesheetDays);
            var punch = 100m - Pct(m.MissingPunchDays, m.ExpectedTimesheetDays);
            var duration = 100m - Pct(m.LessDurationDays, m.ExpectedTimesheetDays);
            var punctuality = 100m - Pct(m.LateDays + m.EarlyDays, m.ExpectedTimesheetDays * 2m);
            var total = Fill + Punch + Duration + Punctuality;
            m.AttendanceDisciplineScore = total <= 0 ? 0 : decimal.Round((fill * Fill + punch * Punch + duration * Duration + punctuality * Punctuality) / total, 2);
        }

        var parts = new List<(decimal Score, decimal Weight)>();
        if (m.ComplianceHours > 0) parts.Add((m.TimesheetCompletionScore, Timesheet));
        if (m.EnteredHours > 0) parts.Add((m.ApprovalScore, Approval));
        if (m.ExpectedTimesheetDays > 0) parts.Add((m.AttendanceDisciplineScore, Attendance));
        var weight = parts.Sum(p => p.Weight);
        m.OperationalScore = weight <= 0 ? 0 : decimal.Round(parts.Sum(p => p.Score * p.Weight) / weight, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal Pct(decimal value, decimal denominator) =>
        denominator <= 0 ? 0 : Math.Clamp(decimal.Round(value / denominator * 100m, 2), 0, 100);
}
