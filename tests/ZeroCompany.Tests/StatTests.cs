using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>Giving cross-training stat upgrades to operators who have not trained them.</summary>
public class StatTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-stats-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (ZcWorkspace Ws, string Path) Rig(string? src)
    {
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "a", "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(src!));
        File.Copy(src!, path);
        return (ZcWorkspace.Open(Path.Combine(_tmp, "a", "data"), useSystemSaveDirs: false), path);
    }

    static SaveEditor Open(ZcWorkspace ws, string path) => SaveEditor.Open(ws, path, () => null);
    static string CopyOf(SaveEditor ed, ApplyResult r) => Path.Combine(Path.GetDirectoryName(ed.Entry.Path)!, r.Written);

    static List<byte[]> Records(GvasFile g, string guid) =>
        (MedbayOps.EffectsNode(g, guid)?.Children ?? new List<GvasNode>()).Select(e => g.Slice(e.Start, e.End)).ToList();

    [Fact]
    public void The_table_lists_20_distinct_stats()
    {
        Assert.Equal(20, StatOps.All.Count);
        Assert.Equal(20, StatOps.All.Select(s => s.Asset).Distinct().Count());
        Assert.All(StatOps.All, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        Assert.Equal("/Game/Game/GameData/BondData/CrossTrainingStatRewards/GE_CrossTraining_RangedDamage.Default__GE_CrossTraining_RangedDamage_C",
                     StatOps.DefPath("GE_CrossTraining_RangedDamage"));
        Assert.Equal("/Game/Game/GameData/Abilities/ResultEffects/GE_CrossTraining_CoverBonus1.Default__GE_CrossTraining_CoverBonus1_C",
                     StatOps.DefPath("GE_CrossTraining_CoverBonus1"));
    }

    [SkippableFact]
    public void A_stat_is_added_after_the_operators_own_effects_and_nobody_else_changes()
    {
        var (ws, path) = Rig(Repo.CombatSaveNamed("Hub_Traits.sav") ?? Repo.SampleSave());
        var ed = Open(ws, path);
        var g = ed.Save.Gvas;
        var who = ed.Save.View.Personnel.Roster.FirstOrDefault(o => StatOps.Givable(g, o.Guid).Count > 0);
        Skip.If(who == null, "nobody can be given a stat in this save");
        var stat = StatOps.Givable(g, who!.Guid)[0];
        var before = Records(g, who.Guid);
        var others = ed.Save.Model.Operators.Keys.Where(k => k != who.Guid).ToDictionary(k => k, k => Records(g, k));

        ed.Do("give-stat", new[] { who.Name, stat.Name });
        var res = ed.Apply(asCopy: true);

        var g2 = Open(ws, CopyOf(ed, res)).Save.Gvas;
        var after = Records(g2, who.Guid);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.All(before.Zip(after), p => Assert.Equal(p.First, p.Second));
        Assert.Equal(stat.Asset, StatOps.Held(g2, who.Guid).Last());
        var added = MedbayOps.EffectsNode(g2, who.Guid)!.Children!.Last();
        Assert.Equal(1, g2.GetInt(g2.Child(added, "StackCount")!));
        Assert.All(others, kv => Assert.Equal(kv.Value, Records(g2, kv.Key)));
        Assert.Throws<ZcException>(() => Open(ws, CopyOf(ed, res)).Do("give-stat", new[] { who.Name, stat.Name }));
    }

    [SkippableFact]
    public void A_stat_nobody_has_gets_its_modifier_entries_and_values_from_the_game_data()
    {
        var raw = ZeroCompany.GameData.Distill.RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        Skip.If(raw == null, "no local game data");
        var db = ZeroCompany.GameData.Distill.Distiller.Distill(raw!);
        var src = Repo.CombatSaveNamed("Hub_Traits.sav") ?? Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var g = new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
        var ops = MedbayOps.Characters(g).Keys.ToList();
        // a stat no operator holds, so there is no record of it to copy
        var stat = StatOps.All.FirstOrDefault(s => ops.All(o => !StatOps.Held(g, o).Contains(s.Asset)) && StatOps.Magnitudes(db, s.Asset).Count > 0);
        Skip.If(stat == null, "every stat is held by someone in the sample save");
        var who = ops.First(o => StatOps.Givable(g, o).Any(s => s.Asset == stat!.Asset));
        var expected = StatOps.Magnitudes(db, stat!.Asset);

        var g2 = StatOps.Give(g, who, stat.Asset, new CutInfo(), db);

        var added = MedbayOps.EffectsNode(g2, who)!.Children!.Last();
        var mods = g2.Child(added, "Modifiers")!.Children!;
        Assert.Equal(expected.Count, mods.Count);
        Assert.Equal(expected, mods.Select(m => g2.GetDouble(g2.Child(m, "EvaluatedMagnitude")!)).Select(v => Math.Round(v, 4)).ToList());
        Assert.Equal(1, g2.GetInt(g2.Child(added, "StackCount")!));
        Assert.Equal(StatOps.DefPath(stat.Asset), g2.GetStr(g2.Child(added, "Def")!));
    }

    [SkippableFact]
    public void Stack_fields_are_capped_at_the_games_stack_limit_when_the_game_data_has_it()
    {
        var raw = ZeroCompany.GameData.Distill.RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        Skip.If(raw == null, "no local game data");
        var db = ZeroCompany.GameData.Distill.Distiller.Distill(raw!);
        Assert.Equal(99, db.Effects["GE_CrossTraining_RangedDamage"].StackLimit);
        var src = Repo.CombatSaveNamed("Hub_Traits.sav");
        Skip.If(src == null, "no sample save");
        var model = new ZeroCompany.Core.Model.SaveModel(new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas), null, db);
        var stacks = model.Operators.Values.SelectMany(o => o.Effects).Where(e => e.Stacks != null && db.Effects.TryGetValue(e.Asset, out var info) && info.StackLimit > 0).ToList();
        Assert.NotEmpty(stacks);
        Assert.All(stacks, e => Assert.Equal(Math.Max(db.Effects[e.Asset].StackLimit, e.Stacks!.Value), e.Stacks.Max));
        Assert.Contains(stacks, e => e.Stacks!.Max != 100);                                       // some effect really is limited below the old fixed 100
    }

    [SkippableFact]
    public void Cover_combat_practice_lives_outside_the_stat_reward_folder_and_is_given_like_the_others()
    {
        const string Cover = "GE_CrossTraining_CoverBonus1";
        var (ws, path) = Rig(Repo.CombatSaveNamed("Hub_Traits.sav"));
        var ed = Open(ws, path);
        var g = ed.Save.Gvas;
        Assert.Contains(ed.Save.Model.Operators.Keys, gid => StatOps.Held(g, gid).Contains(Cover));
        var who = ed.Save.View.Personnel.Roster.First(o => StatOps.Givable(g, o.Guid).Any(s => s.Asset == Cover));

        ed.Do("give-stat", new[] { who.Name, "Cover Bonus" });
        var res = ed.Apply(asCopy: true);

        var g2 = Open(ws, CopyOf(ed, res)).Save.Gvas;
        var added = MedbayOps.EffectsNode(g2, who.Guid)!.Children!.Last();
        Assert.Equal(StatOps.DefPath(Cover), g2.GetStr(g2.Child(added, "Def")!));
        Assert.Equal(1, StatOps.Count(g2, who.Guid, Cover));
        Assert.NotEmpty(g2.Child(added, "Modifiers")!.Children!);                      // copied from another operator's record, modifiers and all
    }
}
