using ZeroCompany.Core.Actions;
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
    public void Another_settings_file_still_does_not_open()
    {
        var (ws, path) = Rig();
        var other = Path.Combine(Path.GetDirectoryName(path)!, "CharacterDatabank_Default_Test.sav");
        File.WriteAllBytes(other, Synth.Gvas(Synth.Cat(Synth.IntProp("Version", 1))));
        Assert.Equal("settings", SaveContainer.Summarize(other).Kind);
        Assert.Throws<ZcException>(() => SaveEditor.Open(ws, other, () => null));
    }
}
