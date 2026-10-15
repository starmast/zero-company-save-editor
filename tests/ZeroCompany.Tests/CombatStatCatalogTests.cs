using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>The stat catalog names and limits the attributes a combat save stores; it must never make a real value look invalid.</summary>
public class CombatStatCatalogTests
{
    static readonly string[] Samples =
    {
        "Combat_EncounterStart.sav", "Combat_Imperial.sav", "Combat_Rescue_after.sav", "Combat_Rescue_before.sav",
        "Combat_Rescue_late.sav", "Combat_Scenery.sav", "Combat_ThreeWay.sav",
    };

    [Theory]
    [InlineData("LongRangeFalloffIncrement", "Long range falloff increment")]
    [InlineData("MaxHealth", "Max health")]
    [InlineData("LOSTraceRadius", "Los trace radius")]
    [InlineData("Aim", "Aim")]
    public void Spaces_a_camel_case_name(string name, string expected) => Assert.Equal(expected, CombatStatCatalog.Spaced(name));

    [Fact]
    public void Knows_which_set_a_name_belongs_to()
    {
        Assert.Equal(CombatStatCatalog.Internal, CombatStatCatalog.Of("BitReactorHealthSet", "Damage").Group);
        Assert.Equal(CombatStatCatalog.Ranged, CombatStatCatalog.Of("BitReactorRangedWeaponSet", "Damage").Group);
        Assert.Equal(CombatStatCatalog.Melee, CombatStatCatalog.Of("BitReactorMeleeWeaponSet", "Damage").Group);
        Assert.Equal(CombatStatCatalog.Grenade, CombatStatCatalog.Of("BitReactorGrenadeUserSet", "GrenadeDamage").Group);
        Assert.Equal("Other", CombatStatCatalog.Of("BitReactorSomethingNewSet", "Whatever").Group);
        Assert.Equal("Whatever", CombatStatCatalog.Of("BitReactorSomethingNewSet", "Whatever").Label);
    }

    [Fact]
    public void Every_group_a_stat_can_land_in_has_a_place_in_the_order()
    {
        foreach (var set in new[] { "BitReactorHealthSet", "BitReactorCombatSet", "BitReactorAdvantageSet", "BitReactorGrenadeUserSet", "BitReactorRangedWeaponSet", "BitReactorMeleeWeaponSet", "Other" })
            foreach (var name in new[] { "Health", "Attack", "Damage", "Range", "Advantage", "GrenadeDamage", "Nope" })
                Assert.Contains(CombatStatCatalog.Of(set, name).Group, CombatStatCatalog.GroupOrder);
    }

    [SkippableFact]
    public void No_saved_value_falls_outside_its_limits()
    {
        int checkedSaves = 0, checkedValues = 0;
        foreach (var file in Samples)
        {
            var path = Repo.CombatSaveNamed(file);
            if (path == null) continue;
            var g = new GvasFile(SaveContainer.Load(File.ReadAllBytes(path)).Gvas);
            var view = CombatOps.Build(g, file);
            foreach (var u in view.Units)
                foreach (var a in u.Attributes)
                {
                    checkedValues++;
                    Assert.True(a.Ref.Value >= a.Ref.Min && a.Ref.Value <= a.Ref.Max,
                        $"{file}: {u.Actor} {a.Set}.{a.Name} = {a.Ref.Value} is outside {a.Ref.Min}..{a.Ref.Max}");
                    Assert.False(string.IsNullOrEmpty(a.Info.Label), $"{a.Set}.{a.Name} has no label");
                }
            checkedSaves++;
        }
        Skip.If(checkedSaves == 0, "no combat sample saves");
        Assert.True(checkedValues > 0);
    }
}
