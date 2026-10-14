using EOS.Domain.Settings;

namespace EOS.Domain.Tests;

public class SettingsResolverTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Fact]
    public void Falls_Back_To_Seed_Default_When_Nothing_Is_Set()
    {
        var r = new SettingsResolver([]);
        Assert.Equal(160, r.Get<int>(SettingKeys.StandardHoursPerMonth, SettingsResolver.Chain(), Today));
        Assert.Equal(0.85m, r.Get<decimal>(SettingKeys.TargetUtilisation, SettingsResolver.Chain(), Today));
    }

    [Fact]
    public void More_Specific_Scope_Wins()
    {
        var r = new SettingsResolver(
        [
            new(SettingKeys.HoursPerDay, SettingScope.Global, "8", new DateOnly(2026, 1, 1)),
            new(SettingKeys.HoursPerDay, SettingScope.Team("LPP"), "7.5", new DateOnly(2026, 1, 1)),
            new(SettingKeys.HoursPerDay, SettingScope.Person("E009"), "6", new DateOnly(2026, 1, 1)),
        ]);
        Assert.Equal(8m, r.Get<decimal>(SettingKeys.HoursPerDay, SettingsResolver.Chain(), Today));
        Assert.Equal(7.5m, r.Get<decimal>(SettingKeys.HoursPerDay, SettingsResolver.Chain(teamId: "LPP"), Today));
        Assert.Equal(6m, r.Get<decimal>(SettingKeys.HoursPerDay, SettingsResolver.Chain(personId: "E009", teamId: "LPP"), Today));
    }

    [Fact]
    public void Scenario_Overrides_Project_Overrides_Global()
    {
        var r = new SettingsResolver(
        [
            new(SettingKeys.FactorProfile, SettingScope.Global, "\"compat\"", new DateOnly(2026, 1, 1)),
            new(SettingKeys.FactorProfile, SettingScope.Scenario("S1"), "\"realistic\"", new DateOnly(2026, 1, 1)),
        ]);
        Assert.Equal("compat", r.Get<string>(SettingKeys.FactorProfile, SettingsResolver.Chain(projectId: "P1"), Today));
        Assert.Equal("realistic", r.Get<string>(SettingKeys.FactorProfile, SettingsResolver.Chain(scenarioId: "S1", projectId: "P1"), Today));
    }

    [Fact]
    public void Effective_Dating_Selects_The_Value_In_Force_On_The_Date()
    {
        var r = new SettingsResolver(
        [
            new(SettingKeys.TargetUtilisation, SettingScope.Global, "0.80", new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30)),
            new(SettingKeys.TargetUtilisation, SettingScope.Global, "0.85", new DateOnly(2026, 10, 1)),
        ]);
        var chain = SettingsResolver.Chain();
        Assert.Equal(0.80m, r.Get<decimal>(SettingKeys.TargetUtilisation, chain, new DateOnly(2026, 8, 15)));
        Assert.Equal(0.85m, r.Get<decimal>(SettingKeys.TargetUtilisation, chain, Today));
    }

    [Fact]
    public void Latest_Entry_Wins_When_Several_Are_Effective()
    {
        var r = new SettingsResolver(
        [
            new(SettingKeys.SkillDeployableMinLevel, SettingScope.Global, "3", new DateOnly(2026, 1, 1)),
            new(SettingKeys.SkillDeployableMinLevel, SettingScope.Global, "4", new DateOnly(2026, 6, 1)),
        ]);
        Assert.Equal(4, r.Get<int>(SettingKeys.SkillDeployableMinLevel, SettingsResolver.Chain(), Today));
    }

    [Fact]
    public void Every_Key_Constant_Has_A_Seed_Default()
    {
        var keys = typeof(SettingKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
        foreach (var key in keys)
            Assert.True(SettingDefaults.All.ContainsKey(key), $"No seed default for {key}");
    }

    [Fact]
    public void Unknown_Key_Without_Default_Throws()
    {
        var r = new SettingsResolver([]);
        Assert.Throws<KeyNotFoundException>(() => r.Get<int>("no.such.key", SettingsResolver.Chain(), Today));
    }
}
