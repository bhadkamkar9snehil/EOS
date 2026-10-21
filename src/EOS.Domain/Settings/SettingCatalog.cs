namespace EOS.Domain.Settings;

public enum SettingKind { Number, Integer, Percent, Toggle, Choice, Days }

public sealed record SettingMeta(
    string Key, string Group, string Label, string Help, SettingKind Kind, string? Unit = null,
    decimal? Min = null, decimal? Max = null, IReadOnlyList<(string Value, string Label)>? Options = null);

public sealed record SettingGroup(string Id, string Title, string Summary);

public static class SettingCatalog
{
    public static IReadOnlyList<SettingGroup> Groups { get; } =
    [
        new("time", "Working time", "How many hours a day an engineer works and on which days. Every capacity number in EOS starts here."),
        new("load", "Capacity and load", "When an engineer counts as under-used, healthy or over-allocated."),
        new("scheduling", "Scheduling", "How the planner turns effort and people into dates."),
        new("skills", "Skills", "The rating scale and the levels that make someone usable on a project."),
        new("training", "Training", "How the share of modules someone knows becomes Good, Medium or Weak."),
        new("health", "Project health", "When a project is shown as Moderate Risk or At Risk."),
        new("infra", "Infrastructure", "Warnings for servers and virtual machines."),
        new("alerts", "Alert thresholds", "Limits used by the Alerts page."),
        new("performance", "Operational score", "How timesheet completion, approval and attendance are weighted into a monthly operational score when ERP reports are imported."),
    ];

    private static readonly (string, string)[] RuleOptions = [("afterOwnTasks", "Stay on the project and pick up its remaining work"), ("none", "Work stops until someone is assigned")];

    public static IReadOnlyList<SettingMeta> All { get; } =
    [
        new(SettingKeys.HoursPerDay, "time", "Hours in a working day", "Used to turn effort in hours into days on the plan and in capacity.", SettingKind.Number, "hours", 1, 24),
        new(SettingKeys.WorkDays, "time", "Working days", "Days of the week that count as working days. Holidays are set under Calendars.", SettingKind.Days),
        new(SettingKeys.StandardHoursPerMonth, "time", "Standard hours per month", "Only used when capacity is set to a fixed monthly figure.", SettingKind.Number, "hours", 1, 400),
        new(SettingKeys.CapacityMode, "time", "How capacity is counted", "From the calendar (real working days minus holidays and leave) or a fixed number of hours every month.", SettingKind.Choice, null, null, null,
            [("calendar", "From the calendar"), ("fixedMonthly", "Fixed hours per month")]),

        new(SettingKeys.TargetUtilisation, "load", "Target utilisation", "The share of an engineer's time you aim to keep booked.", SettingKind.Percent, "%", 0, 1.5m),
        new(SettingKeys.LoadUnderUsedBelow, "load", "Under-used below", "Load under this share of capacity is shown as under-used.", SettingKind.Percent, "%", 0, 1.5m),
        new(SettingKeys.LoadOverAllocatedAbove, "load", "Over-allocated above", "Load over this share of capacity is shown in red.", SettingKind.Percent, "%", 0.5m, 3),
        new(SettingKeys.ProposedCountsTowardLoad, "load", "Count proposed allocations as load", "Include allocations that are only proposed, not yet committed.", SettingKind.Toggle),
        new(SettingKeys.ContextSwitchPenaltyPerExtraProject, "load", "Penalty per extra project", "Capacity lost for each additional project an engineer is spread across.", SettingKind.Percent, "%", 0, 0.5m),

        new(SettingKeys.FactorProfile, "scheduling", "Experience factor profile", "Compatible treats every engineer as equally fast; realistic scales speed with skill rating.", SettingKind.Choice, null, null, null,
            [("compat", "Compatible (everyone at full speed)"), ("realistic", "Realistic (speed follows rating)")]),
        new(SettingKeys.LostAllEngineersRule, "scheduling", "When a task loses all its engineers", "What happens to remaining work when the last assigned engineer is moved away.", SettingKind.Choice, null, null, null, RuleOptions),
        new(SettingKeys.WholeDayRule, "scheduling", "Round tasks up to whole days", "A task that needs part of a day still takes the whole day.", SettingKind.Toggle),
        new(SettingKeys.EstimatedWorkDrivesSchedule, "scheduling", "Estimated effort drives dates", "When on, effort marked as an estimate sets the task length; when off, a fixed duration wins.", SettingKind.Toggle),
        new(SettingKeys.DefaultTaskDurationDays, "scheduling", "Default task length", "Used for a task with neither work nor duration.", SettingKind.Integer, "days", 1, 60),
        new(SettingKeys.UnassignedCrewSize, "scheduling", "Assumed crew on unassigned tasks", "How many engineers EOS assumes on a task nobody is assigned to.", SettingKind.Integer, "people", 1, 10),

        new(SettingKeys.SkillLevelCount, "skills", "Top rating", "The highest rating on the scale (ratings run from 0 to this).", SettingKind.Integer, null, 2, 10),
        new(SettingKeys.SkillResourceMinLevel, "skills", "Minimum level to count as a resource", "Below this rating a skill is not counted as available capacity.", SettingKind.Integer, null, 0, 10),
        new(SettingKeys.SkillDeployableMinLevel, "skills", "Minimum level to deploy", "Rating at which someone can be put on a project alone.", SettingKind.Integer, null, 0, 10),
        new(SettingKeys.SkillStaleMonths, "skills", "Rating goes stale after", "A rating not reviewed for this long is flagged as out of date.", SettingKind.Integer, "months", 1, 60),
        new(SettingKeys.SkillDisagreementLevels, "skills", "Self and manager disagree by", "Flag a skill when the two ratings differ by more than this many levels.", SettingKind.Number, "levels", 0.5m, 5),
        new(SettingKeys.SkillUseBlendedRating, "skills", "Use the blended rating", "Plan with a blend of manager and self ratings instead of the manager rating alone.", SettingKind.Toggle),

        new(SettingKeys.KnowledgeGoodAtLeast, "training", "Good at or above", "Share of modules known for a Good rating.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.KnowledgeMediumAtLeast, "training", "Medium at or above", "Share of modules known for a Medium rating.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.KnowledgeWeakAtLeast, "training", "Weak at or above", "Below this share someone is shown as having a gap.", SettingKind.Percent, "%", 0, 1),

        new(SettingKeys.HealthModerateDelayRatio, "health", "Moderate risk: share of open tasks late", "A project with at least this share of its open tasks late is Moderate Risk.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.HealthAtRiskDelayRatio, "health", "At risk: share of open tasks late", "At least this share late makes a project At Risk.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.HealthModerateVarianceDays, "health", "Moderate risk: finish slip", "Working days the forecast finish is later than planned.", SettingKind.Integer, "days", 0, 365),
        new(SettingKeys.HealthAtRiskVarianceDays, "health", "At risk: finish slip", "Working days of slip that make a project At Risk.", SettingKind.Integer, "days", 0, 365),
        new(SettingKeys.HealthModerateProgressGapPoints, "health", "Moderate risk: progress behind plan", "Percentage points behind where the planned dates say it should be.", SettingKind.Integer, "points", 0, 100),
        new(SettingKeys.HealthAtRiskProgressGapPoints, "health", "At risk: progress behind plan", "Percentage points behind that make a project At Risk.", SettingKind.Integer, "points", 0, 100),

        new(SettingKeys.HostDiskFreeWarnFraction, "infra", "Warn when disk free is below", "Share of a disk that must stay free.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.VmStaleDays, "infra", "A VM is stale after", "Days since a VM was last touched before it is a clean-up candidate.", SettingKind.Integer, "days", 1, 730),

        new(SettingKeys.PerfWeightTimesheet, "performance", "Weight: timesheet completion", "Entered hours against compliance hours.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.PerfWeightApproval, "performance", "Weight: approval", "Approved hours against entered hours.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.PerfWeightAttendance, "performance", "Weight: attendance discipline", "Filled days, punches, duration and punctuality combined.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.PerfAttendanceFill, "performance", "Attendance: timesheet filled", "Share of attendance discipline from days with a filled timesheet.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.PerfAttendancePunch, "performance", "Attendance: punch present", "Share from days with no missing punch.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.PerfAttendanceDuration, "performance", "Attendance: full duration", "Share from days without a short working duration.", SettingKind.Percent, "%", 0, 1),
        new(SettingKeys.PerfAttendancePunctuality, "performance", "Attendance: punctuality", "Share from days without late arrival or early departure.", SettingKind.Percent, "%", 0, 1),

        new(SettingKeys.AlertTrackerStaleDays, "alerts", "Tracker not updated for", "Days without a tracker update before a project is flagged.", SettingKind.Integer, "days", 1, 90),
        new(SettingKeys.AlertDemandStartWarnDays, "alerts", "Warn this far before unfilled demand starts", "Days before the start of demand with nobody assigned.", SettingKind.Integer, "days", 1, 90),
        new(SettingKeys.AlertMaxConcurrentProjects, "alerts", "Most projects per engineer", "Flag engineers spread across more projects than this.", SettingKind.Integer, "projects", 1, 10),
        new(SettingKeys.AlertIdleDays, "alerts", "Engineer idle for", "Days without allocation before an engineer is flagged as idle.", SettingKind.Integer, "days", 1, 90),
        new(SettingKeys.AlertPendingApprovalDays, "alerts", "Scenario waiting for approval", "Days an analysed scenario may wait.", SettingKind.Integer, "days", 1, 30),
        new(SettingKeys.AlertDoubtUnassignedDays, "alerts", "Doubt without a mentor", "Days a doubt may stay unassigned.", SettingKind.Integer, "days", 1, 30),
        new(SettingKeys.AlertDoubtOpenDays, "alerts", "Doubt still open", "Days a doubt may stay open.", SettingKind.Integer, "days", 1, 90),
        new(SettingKeys.AlertBusFactorMax, "alerts", "Bus factor", "Flag a skill known by this many people or fewer.", SettingKind.Integer, "people", 1, 10),
    ];

    public static SettingMeta? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    public static string? Validate(string key, string json)
    {
        var meta = Find(key);
        if (meta is null) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (meta.Kind)
            {
                case SettingKind.Number or SettingKind.Percent or SettingKind.Integer:
                    if (root.ValueKind != System.Text.Json.JsonValueKind.Number) return $"{meta.Label} must be a number.";
                    var v = root.GetDecimal();
                    if (meta.Kind == SettingKind.Integer && v != Math.Truncate(v)) return $"{meta.Label} must be a whole number.";
                    if (meta.Min is { } lo && v < lo) return $"{meta.Label} cannot be below {Show(meta, lo)}.";
                    if (meta.Max is { } hi && v > hi) return $"{meta.Label} cannot be above {Show(meta, hi)}.";
                    return null;
                case SettingKind.Toggle:
                    return root.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? null : $"{meta.Label} must be on or off.";
                case SettingKind.Choice:
                    return root.ValueKind == System.Text.Json.JsonValueKind.String && meta.Options!.Any(o => o.Value == root.GetString()) ? null : $"Pick one of the listed options for {meta.Label}.";
                case SettingKind.Days:
                    return root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0 && root.EnumerateArray().All(e => e.ValueKind == System.Text.Json.JsonValueKind.Number && e.GetInt32() is >= 0 and <= 6)
                        ? null : "Pick at least one working day.";
            }
            return null;
        }
        catch (System.Text.Json.JsonException) { return "Value is not valid."; }
    }

    private static string Show(SettingMeta meta, decimal v) => meta.Kind == SettingKind.Percent ? $"{v * 100:0.##}%" : v.ToString("0.##");
}
