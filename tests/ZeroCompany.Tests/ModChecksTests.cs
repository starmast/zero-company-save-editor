using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>The debug report's mod section: asset folders from mods, weapon types, and weapons the game can no longer build.</summary>
public class ModChecksTests
{
    [SkippableFact]
    public void A_healthy_save_reports_its_mod_folders_and_no_damaged_weapons()
    {
        var checks = ModChecks.Run(WeaponCopyTests.Sample());
        var content = Assert.Single(checks, c => c.Name == "mod content");
        Assert.Equal(CheckStatus.Ok, content.Status);
        var weapons = Assert.Single(checks, c => c.Name == "weapons");
        Skip.If(weapons.Detail.StartsWith("no operator", StringComparison.Ordinal), "the sample save has no weapons");
        if (weapons.Status == CheckStatus.Ok) Assert.Contains("none damaged", weapons.Detail);
    }

    [Fact]
    public void Only_assets_outside_the_games_own_folders_count_as_mod_content()
    {
        Assert.False(WeaponOps.IsMod("/Game/Game/Weapons/Blaster_Rifle/EE-3/Model/SK_Rifle_EE-3_Base_01"));
        Assert.False(WeaponOps.IsMod("/Game/WwiseAudio/Switches/Foley_Material/Foley_Material"));      // the game's own audio
        Assert.False(WeaponOps.IsMod("CPD_WP_Type_Rifle_EE-3"));
        Assert.True(WeaponOps.IsMod("/Game/ZCCompleteBlasters/SK_BF2_SE44_Mod_TriggerGroup"));
        Assert.True(WeaponOps.IsMod("CPD_GK_BF2_EE4"));
    }

    [SkippableFact]
    public void A_damaged_weapon_on_a_character_that_is_not_on_the_roster_is_mentioned_but_does_not_warn()
    {
        var g0 = WeaponCopyTests.Sample();
        var roster = (g0.Child(g0.StrategyDataOrNull(), "Roster")?.Children ?? new List<GvasNode>()).Select(n => g0.GuidHex(n)).ToHashSet();
        var off = WeaponOps.Operators(g0).FirstOrDefault(o => !roster.Contains(o) && WeaponOps.Read(g0, o) is { Broken: false });
        Skip.If(off == null, "every weapon in the sample save belongs to a roster operator");
        var before = Assert.Single(ModChecks.Run(g0), c => c.Name == "weapons");
        var after = Assert.Single(ModChecks.Run(WeaponCopyTests.Damage(g0, off!)), c => c.Name == "weapons");
        Assert.Equal(before.Status, after.Status);                                     // a leftover character does not turn a clean report into a warning
        Assert.Contains("not on the roster", after.Detail);
    }

    [SkippableFact]
    public void A_stripped_weapon_is_a_warning_that_names_the_type_the_parts_and_the_fix_but_no_operator()
    {
        var g0 = WeaponCopyTests.Sample();
        var (victim, _) = WeaponCopyTests.Pair(g0);
        var type = WeaponOps.Read(g0, victim)!.Type;
        var w = Assert.Single(ModChecks.Run(WeaponCopyTests.Damage(g0, victim)), c => c.Name == "weapons" && c.Status == CheckStatus.Warn);
        Assert.Contains(type, w.Detail);
        Assert.Contains("no mesh or variant data", w.Detail);
        Assert.Contains("Copy weapon", w.Fix);
        Assert.DoesNotContain(victim, w.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
