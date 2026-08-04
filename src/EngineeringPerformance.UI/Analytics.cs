using System.Globalization;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.UI;

/// <summary>Severity of a derived alert, matching the status palette used in the UI.</summary>
public enum AlertLevel { Critical, Serious, Warning }

public sealed record Anomaly(AlertLevel Level, string EmployeeName, string Headline, string Detail);

public sealed record QuartileRow(string EmployeeName, decimal Score, decimal? PreviousScore)
{
    public decimal? Delta => PreviousScore is null ? null : decimal.Round(Score - PreviousScore.Value, 1);
}

public sealed record RadarAxis(string Label, decimal TeamAverage, decimal TopQuartile, decimal BottomQuartile);

/// <summary>
/// Derives every Overview figure from imported monthly rows. Kept out of the
/// component so the rules are testable on their own.
/// </summary>
public static class Analytics
{
    /// <summary>Utilization at or above this share of capacity counts as fully engaged.</summary>
    public const decimal UtilizationTarget = 75m;

    /// <summary>Average punch hours per accountable day above which workload reads as high.</summary>
    public const decimal WorkloadCeiling = 9m;

    public static string Format(decimal value, string format = "0.0") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    public static string Svg(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Average punch hours per accountable day.</summary>
    public static decimal Workload(this MonthlyPerformanceItem item) =>
        item.ExpectedTimesheetDays <= 0 ? 0 : decimal.Round(item.PunchHours / item.ExpectedTimesheetDays, 2);

    public static string WorkloadBand(this MonthlyPerformanceItem item) =>
        item.Workload() > WorkloadCeiling ? "high"
        : !item.HasSummaryData || item.Utilization < UtilizationTarget ? "under"
        : "optimal";

    public static string ScoreBand(decimal score) =>
        score >= 85 ? "good" : score >= 70 ? "warning" : score >= 55 ? "serious" : "critical";

    public static string ScoreBandLabel(string band) => band switch
    {
        "good" => "Strong (85+)",
        "warning" => "On track (70–84)",
        "serious" => "At risk (55–69)",
        _ => "Critical (<55)"
    };

    /// <summary>Sequential blue ramp, light to dark, for heatmap magnitude.</summary>
    public static string HeatColor(decimal score) => score switch
    {
        >= 90 => "#0d366b",
        >= 80 => "#184f95",
        >= 70 => "#256abf",
        >= 60 => "#3987e5",
        >= 50 => "#6da7ec",
        >= 40 => "#9ec5f4",
        >= 25 => "#b7d3f6",
        _ => "#cde2fb"
    };

    public static bool HeatTextIsLight(decimal score) => score >= 60;

    /// <summary>Percentage of accountable days on which a timesheet was filled.</summary>
    public static decimal FillRate(this MonthlyPerformanceItem item) =>
        item.ExpectedTimesheetDays <= 0 ? 0 : Clamp(item.TimesheetFilledDays * 100m / item.ExpectedTimesheetDays);

    public static decimal PunchCompliance(this MonthlyPerformanceItem item) =>
        item.ExpectedTimesheetDays <= 0 ? 0 : Clamp(100m - item.MissingPunchDays * 100m / item.ExpectedTimesheetDays);

    public static decimal DurationCompliance(this MonthlyPerformanceItem item) =>
        item.ExpectedTimesheetDays <= 0 ? 0 : Clamp(100m - item.LessDurationDays * 100m / item.ExpectedTimesheetDays);

    public static decimal Punctuality(this MonthlyPerformanceItem item) =>
        item.ExpectedTimesheetDays <= 0 ? 0 : Clamp(100m - (item.LateDays + item.EarlyDays) * 100m / (item.ExpectedTimesheetDays * 2m));

    private static decimal Clamp(decimal value) => Math.Clamp(decimal.Round(value, 1), 0, 100);

    /// <summary>Team average, top-quartile average and bottom-quartile average per operational dimension.</summary>
    public static IReadOnlyList<RadarAxis> Radar(IReadOnlyList<MonthlyPerformanceItem> items)
    {
        if (items.Count == 0) return [];
        var ranked = items.OrderByDescending(x => x.OperationalScore).ToArray();
        var size = Math.Max(1, ranked.Length / 4);
        var top = ranked.Take(size).ToArray();
        var bottom = ranked.Reverse().Take(size).ToArray();

        RadarAxis Axis(string label, Func<MonthlyPerformanceItem, decimal> selector) => new(
            label,
            decimal.Round(items.Average(selector), 1),
            decimal.Round(top.Average(selector), 1),
            decimal.Round(bottom.Average(selector), 1));

        return
        [
            Axis("Timesheet fill", x => x.FillRate()),
            Axis("Approval", x => x.ApprovalScore),
            Axis("Punctuality", x => x.Punctuality()),
            Axis("Punch record", x => x.PunchCompliance()),
            Axis("Full duration", x => x.DurationCompliance())
        ];
    }

    public static IReadOnlyList<QuartileRow> Quartile(
        IReadOnlyList<MonthlyPerformanceItem> items,
        IReadOnlyDictionary<string, decimal> previous,
        bool top)
    {
        var ordered = top
            ? items.OrderByDescending(x => x.OperationalScore)
            : items.OrderBy(x => x.OperationalScore);
        return ordered.Take(5)
            .Select(x => new QuartileRow(x.EmployeeName, x.OperationalScore,
                previous.TryGetValue(x.EmployeeName, out var p) ? p : null))
            .ToArray();
    }

    /// <summary>
    /// Rules run against imported figures only. Every alert names the number it fired on
    /// so it can be checked against the source export.
    /// </summary>
    public static IReadOnlyList<Anomaly> Anomalies(
        IReadOnlyList<MonthlyPerformanceItem> items,
        IReadOnlyDictionary<string, decimal> previous)
    {
        var found = new List<Anomaly>();
        foreach (var item in items)
        {
            if (!item.HasSummaryData && item.ExpectedTimesheetDays > 0)
                found.Add(new Anomaly(AlertLevel.Critical, item.EmployeeName,
                    "Absent from the utilization export",
                    $"{item.ExpectedTimesheetDays} accountable days on attendance but no compliance hours, so no timesheet score."));

            if (item.HasSummaryData && item.Utilization > 110)
                found.Add(new Anomaly(AlertLevel.Serious, item.EmployeeName,
                    "Booked well above capacity",
                    $"{Format(item.Utilization)}% of compliance hours entered ({Format(item.EnteredHours)} of {Format(item.ComplianceHours)} h)."));

            if (Math.Abs(item.ReconciliationVariance) > 20)
                found.Add(new Anomaly(AlertLevel.Serious, item.EmployeeName,
                    "Punch and timesheet hours disagree",
                    $"{Format(Math.Abs(item.ReconciliationVariance))} h {(item.ReconciliationVariance > 0 ? "more punched than booked" : "more booked than punched")}."));

            if (item.MissingPunchDays >= 5)
                found.Add(new Anomaly(AlertLevel.Warning, item.EmployeeName,
                    "Repeated missing punches",
                    $"{item.MissingPunchDays} days without a punch record out of {item.ExpectedTimesheetDays}."));

            if (previous.TryGetValue(item.EmployeeName, out var was) && was - item.OperationalScore >= 10)
                found.Add(new Anomaly(AlertLevel.Warning, item.EmployeeName,
                    "Score dropped sharply",
                    $"Down {Format(was - item.OperationalScore)} points from {Format(was)} last month."));
        }
        return found.OrderBy(x => x.Level).ThenBy(x => x.EmployeeName).ToArray();
    }

    /// <summary>Aggregate view of who reviewed whom, and how they were rated.</summary>
    public sealed record PeerSummary(
        int TotalFeedback, int UniqueReviewers, int PeopleReviewed, decimal AverageRating,
        decimal FeedbackPerReviewer, IReadOnlyList<PeerStanding> Standings);

    public sealed record PeerStanding(string Name, decimal Average, int ReviewsReceived, int ReviewsGiven);

    public static PeerSummary Peers(IReadOnlyList<PeerReviewItem> reviews)
    {
        if (reviews.Count == 0) return new PeerSummary(0, 0, 0, 0, 0, []);

        var reviewers = reviews.Select(x => PersonName.Normalize(x.ReviewerName)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var given = reviews.GroupBy(x => PersonName.Normalize(x.ReviewerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var standings = reviews
            .GroupBy(x => PersonName.Normalize(x.SubjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group => new PeerStanding(
                group.Key,
                decimal.Round(group.Average(x => x.Average), 2),
                group.Count(),
                given.TryGetValue(group.Key, out var count) ? count : 0))
            .OrderByDescending(x => x.Average).ThenBy(x => x.Name)
            .ToArray();

        return new PeerSummary(
            reviews.Count,
            reviewers,
            standings.Length,
            decimal.Round(reviews.Average(x => x.Average), 2),
            reviewers == 0 ? 0 : decimal.Round((decimal)reviews.Count / reviewers, 1),
            standings);
    }

    /// <summary>Least-squares projection of the next point, clamped to the score range.</summary>
    public static decimal? Forecast(IReadOnlyList<decimal> series)
    {
        if (series.Count < 2) return null;
        var n = series.Count;
        var sumX = 0m; var sumY = 0m; var sumXy = 0m; var sumXx = 0m;
        for (var i = 0; i < n; i++)
        {
            sumX += i; sumY += series[i]; sumXy += i * series[i]; sumXx += (decimal)i * i;
        }
        var denominator = n * sumXx - sumX * sumX;
        if (denominator == 0) return null;
        var slope = (n * sumXy - sumX * sumY) / denominator;
        var intercept = (sumY - slope * sumX) / n;
        return Math.Clamp(decimal.Round(intercept + slope * n, 1), 0, 100);
    }
}
