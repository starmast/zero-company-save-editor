using ZeroCompany.Core;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Tests;

public class PendingAndEnvTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-pend-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    static FieldRef Ref(string id = "o1", double v = 10, double min = 0, double max = 100, string kind = "int") =>
        new() { Id = id, Value = v, Min = min, Max = max, Kind = kind };

    static SaveView ViewWith(params string[] roster) => new()
    {
        Personnel = new PersonnelView { Roster = roster.Select(g => new OperatorView { Guid = g, Name = "N" + g }).ToList() },
    };

    [Fact]
    public void SetEdit_to_the_original_value_removes_the_edit()
    {
        var p = new PendingChanges();
        var r = Ref();
        p.SetEdit(r, 20);
        Assert.True(p.IsChanged(r)); Assert.Equal(20, p.GetValue(r)); Assert.Equal(1, p.Count);
        p.SetEdit(r, 10);
        Assert.False(p.IsChanged(r)); Assert.Equal(10, p.GetValue(r)); Assert.Equal(0, p.Count);
    }

    [Fact]
    public void Validation_follows_the_field_limits_and_kind()
    {
        var r = Ref(min: 0, max: 100);
        Assert.True(PendingChanges.Valid(r, 100));
        Assert.False(PendingChanges.Valid(r, 101));
        Assert.False(PendingChanges.Valid(r, 1.5));
        Assert.False(PendingChanges.Valid(r, double.NaN));
        Assert.True(PendingChanges.Valid(Ref(kind: "float"), 1.5));
    }

    [Fact]
    public void Unlinked_edits_are_passed_on_and_the_set_raises_events()
    {
        var p = new PendingChanges();
        int raised = 0; p.Changed += () => raised++;
        p.SetEdit(Ref("o1"), 11, link: false);
        p.SetEdit(Ref("o2"), 12);
        var ch = p.BuildChanges();
        Assert.False(ch.Single(c => c.Id == "o1").Link);
        Assert.True(ch.Single(c => c.Id == "o2").Link);
        Assert.Equal(2, raised);
        p.Discard();
        Assert.Equal(0, p.Count); Assert.Equal(3, raised);
    }

    [Fact]
    public void Roster_moves_track_the_pending_order_and_clear_when_back_to_the_saved_one()
    {
        var p = new PendingChanges();
        var v = ViewWith("A", "B", "C");
        p.MoveOperator("A", 1, v);
        Assert.Equal(new[] { "B", "A", "C" }, p.RosterOrder(v));
        Assert.Equal(1, p.Count);
        p.MoveOperator("A", -1, v);
        Assert.Null(p.RosterOrderOverride); Assert.Equal(0, p.Count);
        p.MoveOperatorTo("C", 0, v);
        Assert.Equal(new[] { "C", "A", "B" }, p.RosterOrder(v));
        p.MoveOperator("C", -1, v);                          // already first: nothing happens
        Assert.Equal(new[] { "C", "A", "B" }, p.RosterOrder(v));
    }

    [Fact]
    public void Actions_are_built_in_the_original_order()
    {
        var p = new PendingChanges { AlsoExpedite = true };
        var up = new ZeroCompany.Core.Actions.UpgradeList
        {
            Items = { new() { Id = "r1", Name = "Up_A_1" }, new() { Id = "r2", Name = "Up_A_2" } },
        };
        p.ToggleStart("r1"); p.ToggleExpedite("r2"); p.ToggleHeal("G1"); p.ToggleRevive("G2"); p.ToggleTreeFix("G3");
        p.SetCoil("Major.Striker_A", "Prevented");
        var a = p.BuildActions(up);
        Assert.Equal(new SaveAction.StartExpediteUpgrade("r1", "Up_A_1"), a[0]);
        Assert.Equal(new SaveAction.ExpediteUpgrade("r2", "Up_A_2"), a[1]);
        Assert.IsType<SaveAction.HealOperator>(a[2]);
        Assert.IsType<SaveAction.CompleteFocusTree>(a[3]);
        Assert.IsType<SaveAction.ReviveOperator>(a[4]);
        Assert.IsType<SaveAction.RemoveCoilUpgrades>(a[5]);
        p.ToggleStart("r1");
        Assert.DoesNotContain(p.BuildActions(up), x => x is SaveAction.StartExpediteUpgrade);
    }

    [Fact]
    public void Upgrade_queue_helpers_batch_and_notify_once()
    {
        var p = new PendingChanges();
        int raised = 0; p.Changed += () => raised++;
        p.QueueUpgrades(new[] { "r1", "r2" }, new[] { "r3" });
        Assert.Equal(3, p.Count); Assert.Equal(1, raised);
        p.QueueUpgrades(new[] { "r1" }, Array.Empty<string>());      // already queued: no duplicates
        Assert.Equal(3, p.Count);
        p.AlsoExpedite = true; p.AlsoExpedite = true;                 // only a real change notifies
        Assert.Equal(3, raised);
        p.ClearUpgradeQueue();
        Assert.Equal(0, p.Count);
    }

    [Fact]
    public void Proton_prefixes_are_found_under_a_home_folder()
    {
        var save = Path.Combine(_tmp, ".local", "share", "Steam", "steamapps", "compatdata", "123", "pfx", "drive_c", "users",
            "steamuser", "AppData", "Local", "SWZeroCompany", "Saved", "SaveGames");
        Directory.CreateDirectory(save);
        Assert.Equal(new[] { save }, AppEnvironment.ProtonSaveDirs(_tmp));
    }

    [Fact]
    public void User_folders_get_stable_ids_and_missing_ones_are_skipped()
    {
        var env = new AppEnvironment(Path.Combine(_tmp, "data")) { UseSystemSaveDirs = false };
        var a = Path.Combine(_tmp, "a"); Directory.CreateDirectory(a);
        var dirs = env.DiscoverSaveDirs(new[] { Path.Combine(_tmp, "missing"), a });
        Assert.Equal(a, dirs["user2"]);                       // position in the settings list, so ids do not shift
        Assert.DoesNotContain("user1", dirs.Keys);
    }

    [Fact]
    public void Settings_round_trip_and_a_bad_file_gives_defaults()
    {
        var env = new AppEnvironment(Path.Combine(_tmp, "data"));
        var s = new AppSettings { GameDir = "G", UsmapPath = "U", SaveDirs = { "x" } };
        s.Save(env);
        var back = AppSettings.Load(env);
        Assert.Equal("G", back.GameDir); Assert.Equal(new[] { "x" }, back.SaveDirs);
        File.WriteAllText(env.SettingsPath, "{nope");
        Assert.Equal("", AppSettings.Load(env).GameDir);
    }

    [SkippableFact]
    public void Raw_tree_ids_are_unique_and_every_row_resolves_to_its_own_node()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var env = new AppEnvironment(Path.Combine(_tmp, "data")) { UseSystemSaveDirs = false };
        var saves = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(saves);
        File.Copy(src!, Path.Combine(saves, Path.GetFileName(src!)));
        var s = new EditorSession(env, () => null);
        s.Settings.SaveDirs.Add(saves); s.Refresh();
        var open = s.Open(s.Service.ListSaves().Single().Dir, Path.GetFileName(src!));
        var ids = open.Gvas.Walk().Select(OpenSave.TreeId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());                          // used to collide on shared start offsets
        foreach (var row in s.Service.Tree(null))
            Assert.Equal(row.Name, open.NodeIndex[row.Id].Name);
        var parent = open.Gvas.Root[0];
        foreach (var row in s.Service.Tree(OpenSave.TreeId(parent)).Take(20))
            Assert.Same(parent, open.NodeIndex[row.Id].Parent);
    }

    [SkippableFact]
    public void Session_applies_pending_changes_to_the_open_save()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var env = new AppEnvironment(Path.Combine(_tmp, "data")) { UseSystemSaveDirs = false };
        var saves = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(saves);
        File.Copy(src!, Path.Combine(saves, Path.GetFileName(src!)));
        var s = new EditorSession(env, () => null);
        s.Settings.SaveDirs.Add(saves);
        s.Refresh();
        var dirId = s.Service.ListSaves().Single().Dir;
        var open = s.Open(dirId, Path.GetFileName(src!));
        var credits = open.View.Header.Resources.First(r => r.Key == "Credits");
        s.Pending.SetEdit(credits, credits.Value + 123);
        Assert.Equal(1, s.Pending.Count);
        var res = s.Apply(asCopy: false, force: false);
        Assert.Equal(1, res.Count);
        Assert.Equal(0, s.Pending.Count);
        var after = s.Service.Current!.View.Header.Resources.First(r => r.Key == "Credits");
        Assert.Equal(credits.Value + 123, after.Value);
        Assert.Contains(s.Service.ListBackups(Path.GetFileName(src!)), b => b.Original);
        Assert.Equal(2, s.Service.ListBackups(Path.GetFileName(src!)).Count);       // original + the pre-edit snapshot
    }
}
