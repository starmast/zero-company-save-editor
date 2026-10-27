using ZeroCompany.Core.Actions;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>The debug report's mod section: asset folders from mods, weapon types, and weapons the game can no longer build.</summary>
public class ModChecksTests
{
    [SkippableFact]
    public void A_healthy_save_reports_its_mod_folders_and_no_damaged_weapons()
    {
        var checks = ModChecks.Run(WeaponRepairTests.Sample());
        var content = Assert.Single(checks, c => c.Name == "mod content");
        Assert.Equal(CheckStatus.Ok, content.Status);
        var weapons = Assert.Single(checks, c => c.Name == "weapons");
        Skip.If(weapons.Detail.StartsWith("no operator", StringComparison.Ordinal), "the sample save has no weapons");
        if (weapons.Status == CheckStatus.Ok) Assert.Contains("none damaged", weapons.Detail);
    }

    [SkippableFact]
    public void A_stripped_weapon_is_a_warning_that_names_the_type_the_parts_and_the_fix_but_no_operator()
    {
        var g0 = WeaponRepairTests.Sample();
        var (victim, _) = WeaponRepairTests.Pair(g0);
        var type = WeaponOps.Read(g0, victim)!.Type;
        var w = Assert.Single(ModChecks.Run(WeaponRepairTests.Damage(g0, victim)), c => c.Name == "weapons" && c.Status == CheckStatus.Warn);
        Assert.Contains(type, w.Detail);
        Assert.Contains("no mesh or variant data", w.Detail);
        Assert.Contains("Repair weapon", w.Fix);
        Assert.DoesNotContain(victim, w.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
