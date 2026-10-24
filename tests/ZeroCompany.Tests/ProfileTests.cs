using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>The game's profile file (SharedGameSettings.sav): achievement counters, zone bests and unlocks.</summary>
public class ProfileTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-profile-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (ZcWorkspace Ws, string Path) Rig()
    {
        var src = Repo.CombatSaveNamed("Profile.sav");
        Skip.If(src == null, "no profile sample");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ProfileOps.FileName);
        File.Copy(src!, path);
        return (ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false), path);
    }

    [Fact]
    public void The_achievement_table_has_distinct_ids_and_positive_targets()
    {
        Assert.Equal(52, ProfileOps.Achievements.Count);
        Assert.Equal(52, ProfileOps.Achievements.Select(a => a.Id).Distinct().Count());
        Assert.All(ProfileOps.Achievements, a => Assert.True(a.Target > 0));
        Assert.Equal("There Will Be No Bargain", ProfileOps.Achievements.Single(a => a.Stat == "ThereWillBeNoBargain").Name);
        Assert.Equal(3, ProfileOps.Achievements.Single(a => a.Stat == "ThereWillBeNoBargain").Target);
    }

    [SkippableFact]
    public void The_profile_opens_with_counters_zones_and_unlocks_and_is_listed_as_editable()
    {
        var (ws, path) = Rig();
        Assert.Equal("profile", SaveContainer.Summarize(path).Kind);
        Assert.True(SaveContainer.Summarize(path).Editable);
        var ed = SaveEditor.Open(ws, path, () => null);
        Assert.True(ed.Save.IsProfile);
        var p = ed.Save.Profile!;
        Assert.Contains(p.Achievements, a => a.Stat == "ThereWillBeNoBargain" && a.Count != null);
        Assert.Contains(p.Achievements, a => a.Count == null);                     // achievements the profile has not counted yet
        Assert.Equal(12, p.Zones.Count);
        Assert.Contains(p.Unlocks, u => u.Contains("CampaignComplete"));
        Assert.Contains(ed.Fields.Entries, e => e.Path == "stat.ThereWillBeNoBargain");
        Assert.False(SaveCheck.Run(ws, path).HasFailure());
    }

    [SkippableFact]
    public void A_counter_is_written_in_place_with_a_backup_and_nothing_else_changes()
    {
        var (ws, path) = Rig();
        var before = File.ReadAllBytes(path);
        var ed = SaveEditor.Open(ws, path, () => null);
        var stat = ed.Fields.Resolve("stat.ThereWillBeNoBargain");
        ed.Set(stat.Path, stat.Value + 1);
        var res = ed.Apply();
        Assert.NotNull(res.Backup);

        var after = SaveEditor.Open(ws, path, () => null);
        Assert.Equal(stat.Value + 1, after.Fields.Resolve("stat.ThereWillBeNoBargain").Value);
        var changed = Enumerable.Range(0, before.Length).Count(i => before[i] != File.ReadAllBytes(path)[i]);
        Assert.Equal(before.Length, File.ReadAllBytes(path).Length);                                     // a count is a fixed-size number
        Assert.InRange(changed, 1, 8);                                                                   // one Int64, nothing else
        Assert.Single(ed.Backups(), b => b.Original);
    }

    [SkippableFact]
    public void A_missing_counter_is_started_and_every_other_counter_keeps_its_count()
    {
        var (ws, path) = Rig();
        var ed = SaveEditor.Open(ws, path, () => null);
        var before = ProfileOps.Counters(ed.Save.Gvas).ToDictionary(c => c.Stat, c => ed.Save.Gvas.GetInt(c.Count));
        var missing = ProfileOps.Achievements.First(a => !before.ContainsKey(a.Stat)).Stat;
        Assert.Contains("start counting", ed.Do("add-stat", new[] { missing, "1" }));
        ed.Set("stat.ThereWillBeNoBargain", before["ThereWillBeNoBargain"] + 1);   // a count edit in the same save
        Assert.Contains(ed.Pending.Describe(ed.Save), l => l.Text != null && l.Text.Contains("Start counting"));
        ed.Apply();

        var after = SaveEditor.Open(ws, path, () => null);
        var now = ProfileOps.Counters(after.Save.Gvas).ToDictionary(c => c.Stat, c => after.Save.Gvas.GetInt(c.Count));
        Assert.Equal(before.Count + 1, now.Count);
        Assert.Equal(1, now[missing]);
        Assert.Equal(before["ThereWillBeNoBargain"] + 1, now["ThereWillBeNoBargain"]);
        foreach (var (stat, count) in before.Where(kv => kv.Key != "ThereWillBeNoBargain")) Assert.Equal(count, now[stat]);
        Assert.Equal(1, after.Fields.Resolve("stat." + missing).Value);
        Assert.Contains(after.Save.Profile!.Achievements, a => a.Stat == missing && a.Count != null);
        Assert.False(SaveCheck.Run(ws, path).HasFailure());
    }

    [SkippableFact]
    public void Starting_a_counter_refuses_one_the_profile_has_or_the_game_does_not_know()
    {
        var (ws, path) = Rig();
        var ed = SaveEditor.Open(ws, path, () => null);
        Assert.Throws<ZcException>(() => ed.Do("add-stat", new[] { "ThereWillBeNoBargain", "1" }));     // already counted
        Assert.Throws<ZcException>(() => ed.Do("add-stat", new[] { "NoSuchCounter", "1" }));
        var missing = ProfileOps.Achievements.First(a => ProfileOps.Counters(ed.Save.Gvas).All(c => c.Stat != a.Stat));
        Assert.Throws<ZcException>(() => ed.Do("add-stat", new[] { missing.Stat, "-1" }));
        Assert.Contains(missing.Stat, ed.Do("add-stat", new[] { missing.Id, "0" }));                     // by achievement id too

        // the Core refuses the same things on its own
        var cut = new CutInfo();
        Assert.Throws<GvasException>(() => ProfileOps.AddCounter(ed.Save.Gvas, "ThereWillBeNoBargain", 1, cut));
        Assert.Throws<GvasException>(() => ProfileOps.AddCounter(ed.Save.Gvas, "NoSuchCounter", 1, cut));
    }

    [SkippableFact]
    public void The_profile_is_found_by_its_file_name_but_not_listed_as_a_save()
    {
        var (_, path) = Rig();
        var ws = ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false);
        ws.Settings.SaveDirs.Add(Path.GetDirectoryName(path)!);
        Assert.DoesNotContain(SaveCatalog.List(ws), e => e.Name == ProfileOps.FileName);
        Assert.Equal(path, SaveCatalog.Resolve(ws, "SharedGameSettings.sav").Path);
        Assert.Equal(path, SaveCatalog.Resolve(ws, "SharedGameSettings").Path);
        Assert.Throws<ZcException>(() => SaveCatalog.Resolve(ws, "latest"));                          // no campaign save in the folder
    }

    [SkippableFact]
    public void Another_settings_file_still_does_not_open()
    {
        var (ws, path) = Rig();
        var other = Path.Combine(Path.GetDirectoryName(path)!, "CharacterDatabank_Default_Test.sav");
        File.WriteAllBytes(other, Synth.Gvas(Synth.Cat(Synth.IntProp("Version", 1))));
        Assert.Equal("settings", SaveContainer.Summarize(other).Kind);
        Assert.Throws<ZcException>(() => SaveEditor.Open(ws, other, () => null));
    }
}
