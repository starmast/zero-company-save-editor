using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>Giving bond traits: a late-campaign save (operators who learned traits) and an early one (nobody has yet).</summary>
public class TraitTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-traits-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    const string Grenadier = "GE_Offense_Soldier_Grenadier", PackTactics = "GE_Support_Scout_PackTactics";

    (ZcWorkspace Ws, string Path) Rig(string? src, string sub = "a")
    {
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, sub, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(src!));
        File.Copy(src!, path);
        return (ZcWorkspace.Open(Path.Combine(_tmp, sub, "data"), useSystemSaveDirs: false), path);
    }

    static SaveEditor Open(ZcWorkspace ws, string path) => SaveEditor.Open(ws, path, () => null);
    static string CopyOf(SaveEditor ed, ApplyResult r) => Path.Combine(Path.GetDirectoryName(ed.Entry.Path)!, r.Written);
    static string? Late() => Repo.CombatSaveNamed("Hub_Traits.sav");

    static List<byte[]> Records(GvasFile g, string guid) =>
        (MedbayOps.EffectsNode(g, guid)?.Children ?? new List<GvasNode>()).Select(e => g.Slice(e.Start, e.End)).ToList();

    [Fact]
    public void The_table_lists_32_distinct_traits_of_the_three_kinds()
    {
        Assert.Equal(32, TraitOps.All.Count);
        Assert.Equal(32, TraitOps.All.Select(t => t.Asset).Distinct().Count());
        Assert.All(TraitOps.All, t => Assert.Contains(t.Kind, new[] { "Offense", "Defense", "Support" }));
        Assert.All(TraitOps.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
        Assert.Equal("/Game/Game/GameData/BondData/TestCrossTrainingEffects/GE_Offense_Soldier_Grenadier.Default__GE_Offense_Soldier_Grenadier_C",
                     TraitOps.DefPath(Grenadier));
    }

    [SkippableFact]
    public void Traits_with_and_without_a_holder_are_added_after_the_operators_own_effects()
    {
        var (ws, path) = Rig(Late());
        var ed = Open(ws, path);
        var g = ed.Save.Gvas;
        // someone who lacks both, so one trait has a holder in the save and the other has none
        var who = ed.Save.View.Personnel.Roster.First(o => TraitOps.Givable(g, o.Guid).Any(t => t.Asset == PackTactics)
                                                          && TraitOps.Givable(g, o.Guid).Any(t => t.Asset == Grenadier));
        Assert.DoesNotContain(ed.Save.Model.Operators.Keys, gid => TraitOps.Held(g, gid).Contains(Grenadier));
        Assert.Contains(ed.Save.Model.Operators.Keys, gid => TraitOps.Held(g, gid).Contains(PackTactics));
        var before = Records(g, who.Guid);
        var others = ed.Save.Model.Operators.Keys.Where(k => k != who.Guid).ToDictionary(k => k, k => Records(g, k));

        ed.Do("give-trait", new[] { who.Name, "Grenadier" });
        ed.Do("give-trait", new[] { who.Name, "Pack Tactics" });
        var res = ed.Apply(asCopy: true);

        var g2 = Open(ws, CopyOf(ed, res)).Save.Gvas;
        var after = Records(g2, who.Guid);
        Assert.Equal(before.Count + 2, after.Count);
        Assert.All(before.Zip(after), p => Assert.Equal(p.First, p.Second));                       // nothing of theirs moved or changed
        Assert.Equal(new[] { Grenadier, PackTactics }, TraitOps.Held(g2, who.Guid).TakeLast(2));
        Assert.All(others, kv => Assert.Equal(kv.Value, Records(g2, kv.Key)));                       // nobody else changed
        Assert.Throws<ZcException>(() => Open(ws, CopyOf(ed, res)).Do("give-trait", new[] { who.Name, "Grenadier" }));   // already held
    }

    [SkippableFact]
    public void In_an_early_save_a_trait_is_made_from_a_progression_record_without_its_modifiers()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var ed = Open(ws, path);
        var g = ed.Save.Gvas;
        Assert.DoesNotContain(ed.Save.Model.Operators.Keys, gid => TraitOps.Held(g, gid).Count > 0);
        var who = ed.Save.View.Personnel.Roster.First(o => TraitOps.Givable(g, o.Guid).Count == 32);
        ed.Do("give-trait", new[] { who.Name, "Hazard Training" });
        var res = ed.Apply(asCopy: true);

        var g2 = Open(ws, CopyOf(ed, res)).Save.Gvas;
        var added = MedbayOps.EffectsNode(g2, who.Guid)!.Children!.Last();
        Assert.Equal(TraitOps.DefPath("GE_Support_Warrior_ProtectiveLayers"), g2.GetStr(g2.Child(added, "Def")!));
        Assert.Empty(g2.Child(added, "Modifiers")!.Children ?? new List<GvasNode>());
        Assert.Empty(g2.Child(g2.Child(added, "CapturedRelevantAttributes"), "TargetAttributes")?.Children ?? new List<GvasNode>());
        Assert.Equal(1, g2.GetInt(g2.Child(added, "StackCount")!));
        Assert.Equal(-1.0, g2.GetDouble(g2.Child(added, "Duration")!));
    }

    [SkippableFact]
    public void A_game_1_2_campaign_at_its_second_turn_can_be_given_traits_too()
    {
        var (ws, path) = Rig(Repo.CombatSaveNamed("Hub_1_2.sav"));
        var ed = Open(ws, path);
        var who = ed.Save.View.Personnel.Roster.First(o => TraitOps.Givable(ed.Save.Gvas, o.Guid).Count == 32);
        Assert.Throws<ZcException>(() => ed.Do("give-trait", new[] { who.Name, "Not A Trait" }));
        Assert.Equal(0, ed.PendingCount);
        ed.Do("give-trait", new[] { who.Name, "Difficult Target" });
        var res = ed.Apply(asCopy: true);
        Assert.Equal(new[] { "GE_Defense_Scout_DifficultTarget" }, TraitOps.Held(Open(ws, CopyOf(ed, res)).Save.Gvas, who.Guid));
    }

    [SkippableFact]
    public void Core_refuses_unknown_traits_and_one_already_held()
    {
        var src = Late();
        Skip.If(src == null, "no sample save");
        var g = new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
        var holder = MedbayOps.Characters(g).Keys.First(gid => TraitOps.Held(g, gid).Contains(PackTactics));
        Assert.Throws<GvasException>(() => TraitOps.Give(g, holder, PackTactics, new CutInfo()));
        Assert.Throws<GvasException>(() => TraitOps.Give(g, holder, "GE_Offense_Nobody_Nothing", new CutInfo()));
    }
}
