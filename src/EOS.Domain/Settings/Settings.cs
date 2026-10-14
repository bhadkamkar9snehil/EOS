using System.Text.Json;

namespace EOS.Domain.Settings;

/// <summary>Scope levels of the settings cascade, least to most specific (spec §8.7).</summary>
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

/// <summary>One effective-dated value of a setting at a scope. History is never rewritten; a new entry supersedes.</summary>
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

/// <summary>Setting keys. Values are seed defaults only (see <see cref="SettingDefaults"/>); nothing is hard-coded elsewhere.</summary>
public static class SettingKeys
{
    // Time and hours
    public const string StandardHoursPerMonth = "time.standardHoursPerMonth";
    public const string HoursPerDay = "time.hoursPerDay";
    public const string WorkDays = "time.workDays";                   // JSON array of DayOfWeek numbers (0 = Sunday)
    public const string CapacityMode = "time.capacityMode";           // "calendar" | "fixedMonthly"

    // Utilisation and load bands
    public const string TargetUtilisation = "utilisation.target";
    public const string ProposedCountsTowardLoad = "utilisation.proposedCounts";
    public const string ContextSwitchPenaltyPerExtraProject = "utilisation.contextSwitchPenalty";
    public const string LoadUnderUsedBelow = "load.underUsedBelow";
    public const string LoadOverAllocatedAbove = "load.overAllocatedAbove";

    // Skills
    public const string SkillLevelCount = "skills.levelCount";
    public const string SkillResourceMinLevel = "skills.resourceMinLevel";
    public const string SkillDeployableMinLevel = "skills.deployableMinLevel";
    public const string SkillStaleMonths = "skills.staleMonths";
    public const string SkillDisagreementLevels = "skills.disagreementLevels";
    public const string SkillUseBlendedRating = "skills.useBlendedRating";

    // Training bands (percent known)
    public const string KnowledgeGoodAtLeast = "training.goodAtLeast";
    public const string KnowledgeMediumAtLeast = "training.mediumAtLeast";
    public const string KnowledgeWeakAtLeast = "training.weakAtLeast";

    // Scheduling and what-if
    public const string FactorProfile = "scheduling.factorProfile";   // "compat" | "realistic" | custom name
    public const string WholeDayRule = "scheduling.wholeDayRule";
    public const string LostAllEngineersRule = "scheduling.lostAllEngineersRule";   // "afterOwnTasks" | "none" (spec §14.2 rule 4)
    public const string EstimatedWorkDrivesSchedule = "scheduling.estimatedWorkDrivesSchedule";   // imported tasks: dates (false) or derived work (true)
    public const string DefaultTaskDurationDays = "scheduling.defaultTaskDurationDays";           // a task with no work and no duration
    public const string UnassignedCrewSize = "scheduling.unassignedCrewSize";                     // people assumed on a task with no assignment

    // Alerts (spec §22)
    public const string AlertTrackerStaleDays = "alerts.trackerStaleDays";
    public const string AlertDemandStartWarnDays = "alerts.demandStartWarnDays";
    public const string AlertMaxConcurrentProjects = "alerts.maxConcurrentProjects";
    public const string AlertIdleDays = "alerts.idleDays";
    public const string AlertPendingApprovalDays = "alerts.pendingApprovalDays";
    public const string AlertDoubtUnassignedDays = "alerts.doubtUnassignedDays";
    public const string AlertDoubtOpenDays = "alerts.doubtOpenDays";
    public const string AlertBusFactorMax = "alerts.busFactorMax";

    // Project health
    public const string HealthAtRiskDelayRatio = "health.atRiskDelayRatio";
    public const string HealthModerateDelayRatio = "health.moderateDelayRatio";
    public const string HealthAtRiskVarianceDays = "health.atRiskVarianceDays";
    public const string HealthModerateVarianceDays = "health.moderateVarianceDays";

    // Infrastructure
    public const string HostDiskFreeWarnFraction = "infra.diskFreeWarnFraction";
    public const string VmStaleDays = "infra.vmStaleDays";
}

/// <summary>Seed defaults for every setting (spec §8.7 "Seed default" column). Values are JSON.</summary>
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

        [SettingKeys.HealthAtRiskDelayRatio] = "0.4",
        [SettingKeys.HealthModerateDelayRatio] = "0.15",
        [SettingKeys.HealthAtRiskVarianceDays] = "20",
        [SettingKeys.HealthModerateVarianceDays] = "5",

        [SettingKeys.HostDiskFreeWarnFraction] = "0.1",
        [SettingKeys.VmStaleDays] = "30",
    };
}

/// <summary>
/// Resolves a setting by walking the scope chain from most specific to least specific and, within a scope,
/// choosing the latest entry effective on the requested date; falls back to <see cref="SettingDefaults"/>.
/// </summary>
public sealed class SettingsResolver(IEnumerable<SettingEntry> entries)
{
    private readonly ILookup<string, SettingEntry> _byKey = entries.ToLookup(e => e.Key);

    /// <param name="chainMostSpecificFirst">e.g. [Scenario, Project, Person, Team, Global]. Use <see cref="Chain"/>.</param>
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

    /// <summary>Builds the cascade chain, most specific first. Omit levels that do not apply.</summary>
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
