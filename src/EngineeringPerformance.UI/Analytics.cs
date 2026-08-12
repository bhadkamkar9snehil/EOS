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

    /// <summary>
    /// Billable-capacity baseline per engineer per month. Used for capacity utilization
    /// (billable hours against this figure), which is a different question from timesheet
    /// utilization (hours entered against the ERP's own compliance-hours figure).
    /// </summary>
    public const decimal MonthlyBillableCapacityHours = 200m;

    /// <summary>Billable hours as a share of the 200 h monthly capacity baseline.</summary>
    public static decimal CapacityUtilization(this MonthlyPerformanceItem item) =>
        decimal.Round(item.BillableHours / MonthlyBillableCapacityHours * 100m, 1);

    public static string Format(decimal value, string format = "0.0") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));

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
        decimal FeedbackPerReviewer, decimal EngagementScore, decimal ModelPriorStrength, int EvidenceCoverageTarget,
        PeerStanding? HighestRated, PeerStanding? MostActiveReviewer,
        IReadOnlyList<PeerStanding> Standings);

    public sealed record PeerStanding(
        string Name, decimal Average, decimal ConfidenceAdjustedAverage, decimal ConfidenceLowerBound, decimal EvidenceStrength,
        int ReviewsReceived, int ReviewsGiven, bool IsEstablished);

    /// <summary>
    /// Applies the standing's evidence-derived conservative penalty to one raw
    /// peer aspect. This preserves the person's aspect pattern while ensuring a
    /// sparse perfect result cannot be encoded like a well-supported result.
    /// </summary>
    public static decimal ReliableAspectEstimate(PeerStanding standing, decimal rawAspect, decimal teamAspectAverage, decimal modelPriorStrength)
    {
        if (rawAspect <= 0) return 0;
        var evidenceAdjustedMean = (rawAspect * standing.ReviewsReceived + teamAspectAverage * modelPriorStrength)
            / Math.Max(1m, standing.ReviewsReceived + modelPriorStrength);
        var uncertaintyPenalty = Math.Max(0m, standing.ConfidenceAdjustedAverage - standing.ConfidenceLowerBound);
        // EvidenceStrength is measured against this month's ordinary review
        // coverage. Below that coverage, the same uncertainty gap must carry more
        // weight; otherwise a tiny perfect sample still looks fully established.
        // This remains data-derived: no fixed review-count threshold is introduced.
        var evidenceRatio = Math.Max(0.01m, standing.EvidenceStrength / 100m);
        var conservativePenalty = uncertaintyPenalty / evidenceRatio;
        return decimal.Round(Math.Clamp(evidenceAdjustedMean - conservativePenalty, 1m, 5m), 2);
    }

    public static PeerSummary Peers(IReadOnlyList<PeerReviewItem> reviews)
    {
        if (reviews.Count == 0) return new PeerSummary(0, 0, 0, 0, 0, 0, 0, 0, null, null, []);

        var reviewers = reviews.Select(x => PersonName.Normalize(x.ReviewerName)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var given = reviews.GroupBy(x => PersonName.Normalize(x.ReviewerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var teamAverage = reviews.Average(x => x.Average);
        var groups = reviews
            .GroupBy(x => PersonName.Normalize(x.SubjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                // A reviewer is one independent source of evidence. If an imported
                // package contains duplicate reviewer/subject rows, combine those rows
                // instead of allowing them to manufacture extra confidence.
                var values = group
                    .GroupBy(x => PersonName.Normalize(x.ReviewerName), StringComparer.OrdinalIgnoreCase)
                    .Select(reviewer => reviewer.Average(x => x.Average))
                    .ToArray();
                return new
                {
                    Name = group.Key,
                    Values = values,
                    Average = values.Average(),
                    Received = values.Length,
                    Given = given.TryGetValue(group.Key, out var count) ? count : 0
                };
            })
            .ToArray();

        // "Enough evidence" follows the current month's ordinary coverage rather
        // than a fixed product rule. The lower median is stable for even-sized teams
        // and does not let a handful of unusually dense review sets raise the bar.
        var orderedCoverage = groups.Select(x => x.Received).OrderBy(x => x).ToArray();
        var evidenceCoverageTarget = Math.Max(1, orderedCoverage[(orderedCoverage.Length - 1) / 2]);

        // Empirical-Bayes reliability model. Both the ordinary reviewer noise and
        // the genuine spread between colleagues are estimated from this month's
        // imported ratings, so there is no fixed or hand-tuned review threshold.
        var withinDegrees = groups.Sum(x => Math.Max(0, x.Received - 1));
        var withinSquares = groups.Sum(group => group.Values.Sum(value => Math.Pow((double)(value - group.Average), 2)));
        var pooledVariance = withinDegrees > 0 ? withinSquares / withinDegrees : 0.25d;
        if (pooledVariance < 0.0001d) pooledVariance = 0.25d;

        var meanOfGroupMeans = groups.Average(x => (double)x.Average);
        var observedBetweenVariance = groups.Length > 1
            ? groups.Sum(x => Math.Pow((double)x.Average - meanOfGroupMeans, 2)) / (groups.Length - 1)
            : pooledVariance;
        var meanSamplingVariance = groups.Average(x => pooledVariance / Math.Max(1, x.Received));
        var signalVariance = Math.Max(0.01d, observedBetweenVariance - meanSamplingVariance);
        var priorStrength = pooledVariance / signalVariance;
        const double oneSided95 = 1.645d;

        var standings = groups
            .Select(group =>
            {
                var adjusted = ((double)group.Average * group.Received + (double)teamAverage * priorStrength) / (group.Received + priorStrength);
                // The prior may stabilize the estimated mean, but it is not a real
                // reviewer and must never narrow the uncertainty interval. Only the
                // independent reviewers actually observed contribute to precision.
                var observedStandardError = Math.Sqrt(pooledVariance / Math.Max(1, group.Received));
                var lowerBound = Math.Clamp(adjusted - oneSided95 * observedStandardError, 1d, 5d);
                var evidenceStrength = Math.Min(100d, group.Received * 100d / evidenceCoverageTarget);
                return new PeerStanding(
                    group.Name,
                    decimal.Round(group.Average, 2),
                    decimal.Round((decimal)adjusted, 2),
                    decimal.Round((decimal)lowerBound, 2),
                    decimal.Round((decimal)evidenceStrength, 0),
                    group.Received,
                    group.Given,
                    group.Received >= evidenceCoverageTarget);
            })
            .OrderByDescending(x => x.IsEstablished)
            .ThenByDescending(x => x.ConfidenceLowerBound)
            .ThenByDescending(x => x.ConfidenceAdjustedAverage)
            .ThenByDescending(x => x.ReviewsReceived)
            .ThenBy(x => x.Name)
            .ToArray();

        // Everyone who appears at all — as reviewer, subject, or both — so a person who
        // only gave feedback (never rated) still counts toward the network's population.
        var people = standings.Select(x => x.Name)
            .Union(given.Keys, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Engagement: actual give+receive links against a full round-robin ceiling
        // (everybody rates everybody else once), averaged across the population and
        // scaled to 0-10. Transparent by construction — not a fitted or fuzzy score.
        var ceiling = Math.Max(1, people.Length - 1) * 2;
        var engagement = people.Length == 0 ? 0 : people.Average(name =>
        {
            var received = standings.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.ReviewsReceived ?? 0;
            var giv = given.GetValueOrDefault(name);
            return Math.Clamp((double)(received + giv) / ceiling, 0, 1);
        }) * 10;

        var mostLiked = standings.FirstOrDefault();
        var mostCollaborative = standings.Where(x => x.ReviewsGiven > 0).OrderByDescending(x => x.ReviewsGiven).ThenBy(x => x.Name).FirstOrDefault();

        return new PeerSummary(
            reviews.Count,
            reviewers,
            standings.Length,
            decimal.Round(teamAverage, 2),
            reviewers == 0 ? 0 : decimal.Round((decimal)reviews.Count / reviewers, 1),
            decimal.Round((decimal)engagement, 1),
            decimal.Round((decimal)priorStrength, 1),
            evidenceCoverageTarget,
            mostLiked, mostCollaborative,
            standings);
    }

    public sealed record NetworkNode(string Name, int Received, int Given, string Tooltip);
    public sealed record NetworkLink(string Source, string Target, int Value, string Tooltip);
    public sealed record NetworkGraph(IReadOnlyList<NetworkNode> Nodes, IReadOnlyList<NetworkLink> Links, string? HubName);

    /// <summary>Reviewer-to-subject graph, with the busiest person (most given plus received) named as the hub.</summary>
    public static NetworkGraph Network(IReadOnlyList<PeerReviewItem> reviews)
    {
        if (reviews.Count == 0) return new NetworkGraph([], [], null);

        var received = reviews.GroupBy(x => PersonName.Normalize(x.SubjectName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var given = reviews.GroupBy(x => PersonName.Normalize(x.ReviewerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var people = received.Keys.Union(given.Keys, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var nodes = people.Select(name =>
        {
            var r = received.GetValueOrDefault(name);
            var g = given.GetValueOrDefault(name);
            return new NetworkNode(name, r, g, $"{name} — {r} received, {g} given");
        }).ToArray();

        var links = reviews
            .GroupBy(x => (PersonName.Normalize(x.ReviewerName), PersonName.Normalize(x.SubjectName)))
            .Select(g => new NetworkLink(g.Key.Item1, g.Key.Item2, g.Count(),
                $"{g.Key.Item1} → {g.Key.Item2}: {Format(g.Average(x => x.Average), "0.00")} avg"))
            .ToArray();

        var hub = nodes.OrderByDescending(x => x.Received + x.Given).ThenBy(x => x.Name).FirstOrDefault();
        return new NetworkGraph(nodes, links, hub?.Name);
    }

    /// <summary>This engineer's value on each radar axis, in the same order as <see cref="Radar"/>.</summary>
    public static decimal[] RadarValues(MonthlyPerformanceItem item) =>
        [item.FillRate(), item.ApprovalScore, item.Punctuality(), item.PunchCompliance(), item.DurationCompliance()];

    public sealed record PeerBreakdown(
        int ReceivedCount, int GivenCount, decimal AverageReceived,
        decimal Collaboration, decimal Communication, decimal Reliability, decimal TechnicalHelp,
        IReadOnlyList<PeerReviewItem> Received);

    /// <summary>Feedback received and given by one person, with per-dimension averages of what they received.</summary>
    public static PeerBreakdown PeerBreakdownFor(IReadOnlyList<PeerReviewItem> reviews, string name)
    {
        var received = reviews.Where(x => PersonName.Matches(x.SubjectName, name)).ToArray();
        var given = reviews.Count(x => PersonName.Matches(x.ReviewerName, name));

        decimal Dim(Func<PeerReviewItem, decimal> selector)
        {
            var rated = received.Select(selector).Where(x => x > 0).ToArray();
            return rated.Length == 0 ? 0 : decimal.Round(rated.Average(), 2);
        }

        return new PeerBreakdown(
            received.Length, given,
            received.Length == 0 ? 0 : decimal.Round(received.Average(x => x.Average), 2),
            Dim(x => x.Collaboration), Dim(x => x.Communication), Dim(x => x.Reliability), Dim(x => x.TechnicalHelp),
            received);
    }

    /// <summary>
    /// One engineer's timesheet compliance for a month: days missed, hours awaiting approval,
    /// and whether they are in scope for the billable-capacity baseline.
    /// </summary>
    public sealed record ComplianceRow(
        string Name, string? Code, string? Email, string? TeamName,
        decimal ExpectedDays, decimal FilledDays, decimal MissingDays,
        decimal EnteredHours, decimal ApprovedHours, decimal PendingApprovalHours,
        decimal BillableHours, bool IsNonBillable, bool IsConsultant, bool IsOnProbation)
    {
        public decimal FillRate => ExpectedDays <= 0 ? 0 : decimal.Round(FilledDays * 100m / ExpectedDays, 1);
        public decimal ApprovalRate => EnteredHours <= 0 ? 0 : decimal.Round(ApprovedHours / EnteredHours * 100m, 1);
        public decimal CapacityUtilization => decimal.Round(BillableHours / MonthlyBillableCapacityHours * 100m, 1);
        public bool HasGap => MissingDays > 0;
        public bool HasPendingApproval => PendingApprovalHours > 0.01m;
    }

    /// <summary>
    /// Joins the month's imported figures to the employee master so compliance can be reported
    /// with team, email and billable classification alongside the numbers.
    /// </summary>
    public static IReadOnlyList<ComplianceRow> Compliance(
        IReadOnlyList<MonthlyPerformanceItem> items,
        IReadOnlyList<EmployeeListItem> employees)
    {
        return items.Select(item =>
        {
            var employee = employees.FirstOrDefault(e => PersonName.Matches(e.Name, item.EmployeeName));
            var pending = Math.Max(0m, item.EnteredHours - item.ApprovedHours);
            return new ComplianceRow(
                item.EmployeeName, item.EmployeeCode ?? employee?.EmployeeCode, employee?.Email, employee?.TeamName,
                item.ExpectedTimesheetDays, item.TimesheetFilledDays,
                Math.Max(0m, item.ExpectedTimesheetDays - item.TimesheetFilledDays),
                item.EnteredHours, item.ApprovedHours, pending,
                item.BillableHours,
                employee?.IsNonBillable ?? false, employee?.IsConsultant ?? false, employee?.IsOnProbation ?? false);
        })
        .OrderByDescending(x => x.MissingDays).ThenBy(x => x.Name)
        .ToArray();
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
