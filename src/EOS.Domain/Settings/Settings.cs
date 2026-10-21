using System.Text.Json;

namespace EOS.Domain.Settings;

public enum SettingScopeKind
{
    Global = 0,
    Team = 1,
    Person = 2,
    Project = 3,
    Scenario = 4,
}

public readonly record struct SettingScope(SettingScopeKind Kind, string? Id = null)
{
    public static SettingScope Global { get; } = new(SettingScopeKind.Global);
    public static SettingScope Team(string id) => new(SettingScopeKind.Team, id);
    public static SettingScope Person(string id) => new(SettingScopeKind.Person, id);
    public static SettingScope Project(string id) => new(SettingScopeKind.Project, id);
    public static SettingScope Scenario(string id) => new(SettingScopeKind.Scenario, id);
}

public sealed record SettingEntry(
    string Key,
    SettingScope Scope,
    string JsonValue,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    string? ChangedBy = null,
    string? Reason = null)
{
    public bool IsEffectiveOn(DateOnly date) => date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}

public static class SettingKeys
{
    public const string StandardHoursPerMonth = "time.standardHoursPerMonth";
    public const string HoursPerDay = "time.hoursPerDay";
    public const string WorkDays = "time.workDays";
    public const string CapacityMode = "time.capacityMode";           // "calendar" | "fixedMonthly"

    public const string TargetUtilisation = "utilisation.target";
    public const string ProposedCountsTowardLoad = "utilisation.proposedCounts";
    public const string ContextSwitchPenaltyPerExtraProject = "utilisation.contextSwitchPenalty";
    public const string LoadUnderUsedBelow = "load.underUsedBelow";
    public const string LoadOverAllocatedAbove = "load.overAllocatedAbove";

    public const string SkillLevelCount = "skills.levelCount";
    public const string SkillResourceMinLevel = "skills.resourceMinLevel";
    public const string SkillDeployableMinLevel = "skills.deployableMinLevel";
    public const string SkillStaleMonths = "skills.staleMonths";
    public const string SkillDisagreementLevels = "skills.disagreementLevels";
    public const string SkillUseBlendedRating = "skills.useBlendedRating";

    public const string KnowledgeGoodAtLeast = "training.goodAtLeast";
    public const string KnowledgeMediumAtLeast = "training.mediumAtLeast";
    public const string KnowledgeWeakAtLeast = "training.weakAtLeast";

    public const string FactorProfile = "scheduling.factorProfile";   // "compat" | "realistic" | custom name
    public const string WholeDayRule = "scheduling.wholeDayRule";
    public const string LostAllEngineersRule = "scheduling.lostAllEngineersRule";   // "afterOwnTasks" | "none" (spec §14.2 rule 4)
    public const string EstimatedWorkDrivesSchedule = "scheduling.estimatedWorkDrivesSchedule";
    public const string DefaultTaskDurationDays = "scheduling.defaultTaskDurationDays";
    public const string UnassignedCrewSize = "scheduling.unassignedCrewSize";

    public const string AlertTrackerStaleDays = "alerts.trackerStaleDays";
    public const string AlertDemandStartWarnDays = "alerts.demandStartWarnDays";
    public const string AlertMaxConcurrentProjects = "alerts.maxConcurrentProjects";
    public const string AlertIdleDays = "alerts.idleDays";
    public const string AlertPendingApprovalDays = "alerts.pendingApprovalDays";
    public const string AlertDoubtUnassignedDays = "alerts.doubtUnassignedDays";
    public const string AlertDoubtOpenDays = "alerts.doubtOpenDays";
    public const string AlertBusFactorMax = "alerts.busFactorMax";

    public const string HealthAtRiskDelayRatio = "health.atRiskDelayRatio";
    public const string HealthModerateDelayRatio = "health.moderateDelayRatio";
    public const string HealthAtRiskVarianceDays = "health.atRiskVarianceDays";
    public const string HealthModerateVarianceDays = "health.moderateVarianceDays";
    public const string HealthAtRiskProgressGapPoints = "health.atRiskProgressGapPoints";
    public const string HealthModerateProgressGapPoints = "health.moderateProgressGapPoints";

    public const string HostDiskFreeWarnFraction = "infra.diskFreeWarnFraction";
    public const string VmStaleDays = "infra.vmStaleDays";

    public const string PerfWeightTimesheet = "performance.weightTimesheet";
    public const string PerfWeightApproval = "performance.weightApproval";
    public const string PerfWeightAttendance = "performance.weightAttendance";
    public const string PerfAttendanceFill = "performance.attendanceFill";
    public const string PerfAttendancePunch = "performance.attendancePunch";
    public const string PerfAttendanceDuration = "performance.attendanceDuration";
    public const string PerfAttendancePunctuality = "performance.attendancePunctuality";
}

public static class SettingDefaults
{
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [SettingKeys.StandardHoursPerMonth] = "160",
        [SettingKeys.HoursPerDay] = "8",
        [SettingKeys.WorkDays] = "[1,2,3,4,5,6]",
        [SettingKeys.CapacityMode] = "\"calendar\"",

        [SettingKeys.TargetUtilisation] = "0.85",
        [SettingKeys.ProposedCountsTowardLoad] = "false",
        [SettingKeys.ContextSwitchPenaltyPerExtraProject] = "0",
        [SettingKeys.LoadUnderUsedBelow] = "0.6",
        [SettingKeys.LoadOverAllocatedAbove] = "1.0",

        [SettingKeys.SkillLevelCount] = "5",
        [SettingKeys.SkillResourceMinLevel] = "2",
        [SettingKeys.SkillDeployableMinLevel] = "3",
        [SettingKeys.SkillStaleMonths] = "12",
        [SettingKeys.SkillDisagreementLevels] = "1.5",
        [SettingKeys.SkillUseBlendedRating] = "false",

        [SettingKeys.KnowledgeGoodAtLeast] = "0.8",
        [SettingKeys.KnowledgeMediumAtLeast] = "0.6",
        [SettingKeys.KnowledgeWeakAtLeast] = "0.4",

        [SettingKeys.FactorProfile] = "\"compat\"",
        [SettingKeys.WholeDayRule] = "true",
        [SettingKeys.LostAllEngineersRule] = "\"afterOwnTasks\"",
        [SettingKeys.EstimatedWorkDrivesSchedule] = "false",
        [SettingKeys.DefaultTaskDurationDays] = "1",
        [SettingKeys.UnassignedCrewSize] = "1",

        [SettingKeys.AlertTrackerStaleDays] = "7",
        [SettingKeys.AlertDemandStartWarnDays] = "14",
        [SettingKeys.AlertMaxConcurrentProjects] = "2",
        [SettingKeys.AlertIdleDays] = "14",
        [SettingKeys.AlertPendingApprovalDays] = "3",
        [SettingKeys.AlertDoubtUnassignedDays] = "2",
        [SettingKeys.AlertDoubtOpenDays] = "14",
        [SettingKeys.AlertBusFactorMax] = "3",

        [SettingKeys.HealthAtRiskProgressGapPoints] = "25",
        [SettingKeys.HealthModerateProgressGapPoints] = "10",
        [SettingKeys.HealthAtRiskDelayRatio] = "0.4",
        [SettingKeys.HealthModerateDelayRatio] = "0.15",
        [SettingKeys.HealthAtRiskVarianceDays] = "20",
        [SettingKeys.HealthModerateVarianceDays] = "5",

        [SettingKeys.HostDiskFreeWarnFraction] = "0.1",
        [SettingKeys.VmStaleDays] = "30",

        [SettingKeys.PerfWeightTimesheet] = "0.55",
        [SettingKeys.PerfWeightApproval] = "0.15",
        [SettingKeys.PerfWeightAttendance] = "0.30",
        [SettingKeys.PerfAttendanceFill] = "0.40",
        [SettingKeys.PerfAttendancePunch] = "0.25",
        [SettingKeys.PerfAttendanceDuration] = "0.20",
        [SettingKeys.PerfAttendancePunctuality] = "0.15",
    };
}

public sealed class SettingsResolver(IEnumerable<SettingEntry> entries)
{
    private readonly ILookup<string, SettingEntry> _byKey = entries.ToLookup(e => e.Key);

    public string? ResolveJson(string key, IReadOnlyList<SettingScope> chainMostSpecificFirst, DateOnly asOf)
    {
        foreach (var scope in chainMostSpecificFirst)
        {
            var hit = _byKey[key]
                .Where(e => e.Scope == scope && e.IsEffectiveOn(asOf))
                .OrderByDescending(e => e.EffectiveFrom)
                .FirstOrDefault();
            if (hit is not null) return hit.JsonValue;
        }
        return SettingDefaults.All.TryGetValue(key, out var seed) ? seed : null;
    }

    public T Get<T>(string key, IReadOnlyList<SettingScope> chainMostSpecificFirst, DateOnly asOf)
    {
        var json = ResolveJson(key, chainMostSpecificFirst, asOf)
                   ?? throw new KeyNotFoundException($"Setting '{key}' has no value and no seed default.");
        return JsonSerializer.Deserialize<T>(json)
               ?? throw new InvalidDataException($"Setting '{key}' has a null value.");
    }

    public static IReadOnlyList<SettingScope> Chain(
        string? scenarioId = null, string? projectId = null, string? personId = null, string? teamId = null)
    {
        var chain = new List<SettingScope>(5);
        if (scenarioId is not null) chain.Add(SettingScope.Scenario(scenarioId));
        if (projectId is not null) chain.Add(SettingScope.Project(projectId));
        if (personId is not null) chain.Add(SettingScope.Person(personId));
        if (teamId is not null) chain.Add(SettingScope.Team(teamId));
        chain.Add(SettingScope.Global);
        return chain;
    }
}
