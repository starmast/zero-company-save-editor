using System.Security.Cryptography;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Tests;

/// <summary>
/// The safety net: every way an edit can be refused leaves the save byte-for-byte alone, and the success path keeps its backups.
/// (Ported from the original editor's refusal tests.)
/// </summary>
public class ServiceGuardTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-guard-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    static string H(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    sealed record Rig(SaveService Svc, string Name, string Path);

    Rig NewRig(Func<string?>? running = null, bool withData = true)
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        GameDatabase? db = null;
        if (withData && RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata")) is { } raw) db = Distiller.Distill(raw);
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), db, running ?? (() => null));
        svc.Open("t", name);
        return new Rig(svc, name, Path.Combine(dir, name));
    }

    static void Refused(SaveService svc, string path, IReadOnlyList<FieldChange>? changes, params SaveAction[] actions)
    {
        var before = H(path);
        Assert.Throws<EditException>(() => svc.Apply(changes ?? Array.Empty<FieldChange>(), force: true, actions: actions));
        Assert.Equal(before, H(path));                                   // a refusal never touches the file
    }

    // ---------------------------------------------------------------- scalar edits
    [SkippableFact]
    public void Bad_values_and_unknown_fields_are_rejected()
    {
        var r = NewRig();
        var intel = r.Svc.Current!.Model.Fields.First(f => f.Group == "Resources" && f.Label == "Intel");
        foreach (var bad in new[] { intel.Max + 1, -1, 1.5, double.NaN, double.PositiveInfinity })
            Refused(r.Svc, r.Path, new[] { new FieldChange(intel.Id, bad) });
        Refused(r.Svc, r.Path, new[] { new FieldChange("o1", 1) });         // not a known field
        Assert.Throws<EditException>(() => r.Svc.Apply(Array.Empty<FieldChange>(), force: true));            // nothing to do
    }

    [SkippableFact]
    public void Editing_credits_keeps_backups_changes_few_bytes_and_restore_brings_it_back()
    {
        var r = NewRig();
        var before = H(r.Path);
        var oldGvas = SaveContainer.Load(File.ReadAllBytes(r.Path)).Gvas;
        var credits = r.Svc.Current!.View.Header.Resources.First(x => x.Key == "Credits");
        var res = r.Svc.Apply(new[] { new FieldChange(credits.Id, 123456) }, force: true);
        Assert.False(string.IsNullOrEmpty(res.Backup));
        var newGvas = SaveContainer.Load(File.ReadAllBytes(r.Path)).Gvas;
        var diff = Enumerable.Range(0, oldGvas.Length).Where(i => oldGvas[i] != newGvas[i]).ToList();
        Assert.Equal(oldGvas.Length, newGvas.Length);
        Assert.True(diff.Count > 0 && diff.Max() - diff.Min() < 4);
        Assert.Equal(123456, r.Svc.Current!.View.Header.Resources.First(x => x.Key == "Credits").Value);
        var bdir = r.Svc.BackupDir(r.Name);
        var files = Directory.GetFiles(bdir);
        Assert.Contains(files, f => !f.EndsWith(".orig.sav") && H(f) == before);      // the pre-edit file
        Assert.Contains(files, f => f.EndsWith(".orig.sav"));                         // and the very first version
        var orig = r.Svc.ListBackups(r.Name).First(b => b.Original).File;
        r.Svc.Restore(orig, force: true);
        Assert.Equal(before, H(r.Path));
    }

    [SkippableFact]
    public void A_save_that_changed_on_disk_is_refused()
    {
        var r = NewRig();
        var credits = r.Svc.Current!.View.Header.Resources.First(x => x.Key == "Credits");
        using (var f = new FileStream(r.Path, FileMode.Append)) f.WriteByte((byte)'x');         // the game "autosaves" meanwhile
        var ex = Assert.Throws<EditException>(() => r.Svc.Apply(new[] { new FieldChange(credits.Id, 5) }, force: true));
        Assert.Contains("changed on disk", ex.Message);
    }

    [SkippableFact]
    public void A_running_game_blocks_writes_unless_overridden_and_never_blocks_copies()
    {
        var r = NewRig(() => "swzerocompany");
        var credits = r.Svc.Current!.View.Header.Resources.First(x => x.Key == "Credits");
        var change = new[] { new FieldChange(credits.Id, credits.Value + 1) };
        var before = H(r.Path);
        var ex = Assert.Throws<EditException>(() => r.Svc.Apply(change));
        Assert.Contains("running", ex.Message);
        Assert.Equal(before, H(r.Path));
        Assert.Throws<EditException>(() => r.Svc.Restore(r.Svc.ListBackups(r.Name)[0].File));
        var copy = r.Svc.Apply(change, asCopy: true);                      // a copy is always safe
        Assert.True(copy.Copy);
        Assert.Equal(before, H(r.Path));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(r.Path)!, copy.Written)));
        r.Svc.Apply(change, force: true);                                  // the explicit override
    }

    [SkippableFact]
    public void Linked_focus_edit_can_be_unlinked()
    {
        var r = NewRig();
        var op = r.Svc.Current!.Model.Operators.Values.First(o => o.Focus is { Linked.Count: > 0 });
        var f = op.Focus!;
        r.Svc.Apply(new[] { new FieldChange(f.Id, f.Value + 2, Link: false) }, force: true);
        var after = r.Svc.Current!.Model.Operators[op.Guid];
        Assert.Equal(f.Value + 2, after.Focus!.Value);
        Assert.Equal(op.TotalFocus, after.TotalFocus);                     // total did not follow
    }

    // ---------------------------------------------------------------- paths and folders
    [SkippableFact]
    public void Path_traversal_and_unknown_folders_are_blocked()
    {
        var r = NewRig();
        foreach (var name in new[] { "../x.sav", "..\\x.sav", "a/b.sav", "x.txt" })
            Assert.Throws<EditException>(() => r.Svc.Resolve("t", name));
        Assert.Throws<EditException>(() => r.Svc.Resolve("nope", r.Name));
    }

    [SkippableFact]
    public void Backups_are_separate_per_folder()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var name = Path.GetFileName(src!);
        foreach (var d in new[] { "a", "b" }) { Directory.CreateDirectory(Path.Combine(_tmp, d)); File.Copy(src!, Path.Combine(_tmp, d, name)); }
        var s = new SaveService(new Dictionary<string, string> { ["a"] = Path.Combine(_tmp, "a"), ["b"] = Path.Combine(_tmp, "b") }, Path.Combine(_tmp, "bk"));
        Assert.NotEqual(s.BackupDir(name, "a"), s.BackupDir(name, "b"));
        s.Open("a", name); s.Open("b", name);
        Assert.Single(s.ListBackups(name));                                // only b's own original
    }

    [SkippableFact]
    public void Old_snapshots_are_pruned_but_the_original_is_kept()
    {
        var r = NewRig();
        for (int i = 0; i < SaveService.KeepBackups + 5; i++)
        {
            var credits = r.Svc.Current!.View.Header.Resources.First(x => x.Key == "Credits");
            r.Svc.Apply(new[] { new FieldChange(credits.Id, credits.Value + 1) }, force: true);
        }
        var list = r.Svc.ListBackups(r.Name);
        Assert.Equal(SaveService.KeepBackups, list.Count(b => !b.Original));
        Assert.Single(list, b => b.Original);
    }

    // ---------------------------------------------------------------- structural refusals
    [SkippableFact]
    public void Verifier_refuses_a_file_whose_metadata_sizes_were_not_synced()
    {
        var r = NewRig();
        var row = r.Svc.Current!.Upgrades.Items.FirstOrDefault(x => x.CanStart);
        Skip.If(row == null, "no startable upgrade");
        r.Svc.SkipMetadataSync = true;
        var before = H(r.Path);
        var ex = Assert.Throws<EditException>(() => r.Svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.StartUpgrade(row!.Id!, row.Name) }));
        Assert.Contains("metadata", ex.Message);
        Assert.Equal(before, H(r.Path));
    }

    [SkippableFact]
    public void Upgrade_gating_and_bad_upgrade_requests_are_refused()
    {
        var r = NewRig();
        var items = r.Svc.Current!.Upgrades.Items;
        var locked = items.FirstOrDefault(x => x.Status == "Available" && !x.CanStart);
        if (locked != null) Refused(r.Svc, r.Path, null, new SaveAction.StartUpgrade(locked.Id!, locked.Name));
        var open = items.First(x => x.CanStart);
        foreach (var bad in new SaveAction[]
        {
            new SaveAction.StartUpgrade("r9999", "x"),
            new SaveAction.StartUpgrade("../1", "x"),
            new SaveAction.StartUpgrade(open.Id!, "WrongName"),
            new SaveAction.ExpediteUpgrade(open.Id!, open.Name),            // not in progress yet
        })
            Refused(r.Svc, r.Path, null, bad);
        Refused(r.Svc, r.Path, null, new SaveAction.StartUpgrade(open.Id!, open.Name), new SaveAction.StartUpgrade(open.Id!, open.Name));   // twice
    }

    [SkippableFact]
    public void Heal_refuses_the_dead_the_healthy_and_garbage()
    {
        var r = NewRig();
        var m = r.Svc.Current!.Model;
        var dead = m.Operators.Values.First(o => o.Dead);
        Refused(r.Svc, r.Path, null, new SaveAction.HealOperator(dead.Guid));
        var healthy = m.Operators.Values.First(o => o.Injuries == 0 && !o.Dead);
        Refused(r.Svc, r.Path, null, new SaveAction.HealOperator(healthy.Guid));
        foreach (var bad in new[] { "nope", "", "../" + new string('0', 29), new string('G', 32), null! })
            Refused(r.Svc, r.Path, null, new SaveAction.HealOperator(bad));
    }

    [SkippableFact]
    public void Revive_refuses_the_living_garbage_and_duplicates()
    {
        var r = NewRig();
        var living = r.Svc.Current!.View.Personnel.Roster[0].Guid;
        var fallen = r.Svc.Current.View.Personnel.Memorial.FirstOrDefault()?.Guid;
        Skip.If(fallen == null, "no fallen operator");
        foreach (var bad in new[] { living, "nope", "", null!, new string('0', 32), new string('G', 32) })
            Refused(r.Svc, r.Path, null, new SaveAction.ReviveOperator(bad));
        Refused(r.Svc, r.Path, null, new SaveAction.ReviveOperator(fallen!), new SaveAction.ReviveOperator(fallen!));
        Assert.Contains(fallen!, ReviveOps.DeadGuids(new GvasFile(SaveContainer.Load(File.ReadAllBytes(r.Path)).Gvas)));
    }

    [SkippableFact]
    public void Revive_refuses_when_the_roster_is_full()
    {
        var r = NewRig();
        var g = r.Svc.Current!.Gvas;
        var fallen = r.Svc.Current.View.Personnel.Memorial.FirstOrDefault()?.Guid;
        Skip.If(fallen == null, "no fallen operator");
        var roster = ReviveOps.RosterOrder(g).Count;
        var ex = Assert.Throws<GvasException>(() => ReviveOps.Revive(g, fallen!, roster, new CutInfo(), new HashSet<string>()));
        Assert.Contains("full", ex.Message);
    }

    [SkippableFact]
    public void Coil_refuses_bad_requests_and_a_second_action()
    {
        var r = NewRig();
        var ids = CoilOps.Active(r.Svc.Current!.Gvas).Select(a => a.Id).ToList();
        Skip.If(ids.Count < 2, "needs two active Coil upgrades");
        var bad = new List<IReadOnlyList<CoilChange>?>
        {
            null, new List<CoilChange>(),
            new[] { new CoilChange("Major.Nobody_Z", "Available") }, new[] { new CoilChange("../x", "Available") },
            new[] { new CoilChange(null!, "Available") }, new[] { new CoilChange("Major.Striker_A.Selected", "Available") },
            new[] { new CoilChange(ids[0], "Selected") }, new[] { new CoilChange(ids[0], "Nope") }, new[] { new CoilChange(ids[0], null!) },
            new[] { new CoilChange(ids[0], "Available"), new CoilChange(ids[0], "Prevented") },
        };
        foreach (var changes in bad) Refused(r.Svc, r.Path, null, new SaveAction.RemoveCoilUpgrades(changes!));
        Refused(r.Svc, r.Path, null,
            new SaveAction.RemoveCoilUpgrades(new[] { new CoilChange(ids[0], "Available") }),
            new SaveAction.RemoveCoilUpgrades(new[] { new CoilChange(ids[1], "Available") }));
        Assert.Equal(ids, CoilOps.Active(new GvasFile(SaveContainer.Load(File.ReadAllBytes(r.Path)).Gvas)).Select(a => a.Id).ToList());
    }

    [SkippableFact]
    public void Roster_refuses_bad_orders()
    {
        var r = NewRig();
        var cur = RosterOps.Order(r.Svc.Current!.Gvas);
        var zeros = new string('0', 32);
        var bad = new List<IReadOnlyList<string>?>
        {
            null, new List<string>(), cur.Take(cur.Count - 1).ToList(), cur.Append(cur[0]).ToList(),
            cur.Take(cur.Count - 1).Append(zeros).ToList(), cur.Take(cur.Count - 1).Append("nope").ToList(),
            Enumerable.Repeat(cur[0], cur.Count).ToList(), cur.Take(cur.Count - 1).Append(null!).ToList(),
            cur,                                                            // unchanged order
        };
        foreach (var order in bad) Refused(r.Svc, r.Path, null, new SaveAction.ReorderRoster(order!));
        Refused(r.Svc, r.Path, null,
            new SaveAction.ReorderRoster(cur.Skip(1).Concat(cur.Take(1)).ToList()),
            new SaveAction.ReorderRoster(cur.Skip(2).Concat(cur.Take(2)).ToList()));
        Assert.Equal(cur, RosterOps.Order(new GvasFile(SaveContainer.Load(File.ReadAllBytes(r.Path)).Gvas)));
    }

    [SkippableFact]
    public void Focus_tree_completion_refuses_a_second_run_and_complete_trees()
    {
        var r = NewRig();
        var gaps = FocusOps.Incomplete(r.Svc.Current!.Gvas);
        Skip.If(gaps.Count == 0, "no operator with an incomplete focus tree");
        var guid = gaps.Keys.First();
        r.Svc.Apply(Array.Empty<FieldChange>(), force: true, actions: new SaveAction[] { new SaveAction.CompleteFocusTree(guid) });
        Refused(r.Svc, r.Path, null, new SaveAction.CompleteFocusTree(guid));                // already complete
        var living = r.Svc.Current!.View.Personnel.Roster[0].Guid;
        foreach (var bad in new[] { living, "nope", "", null!, new string('0', 32) })
            Refused(r.Svc, r.Path, null, new SaveAction.CompleteFocusTree(bad));
    }
}
