using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>Editing a save through the SDK (what <c>zc edit</c> runs): the same checked write as the editor, on temporary copies of the local sample saves.</summary>
public class SdkEditTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-sdkedit-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    /// <summary>A workspace whose data (backups, settings) lives in the temp folder, and a copy of <paramref name="src"/> to edit.</summary>
    (ZcWorkspace Ws, string Path) Rig(string? src, string sub = "a")
    {
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, sub, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(src!));
        File.Copy(src!, path);
        return (ZcWorkspace.Open(Path.Combine(_tmp, sub, "data"), useSystemSaveDirs: false), path);
    }

    static SaveEditor Open(ZcWorkspace ws, string path, string? running = null) => SaveEditor.Open(ws, path, () => running);
    static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string Copy(SaveEditor ed, ApplyResult r) => Path.Combine(Path.GetDirectoryName(ed.Entry.Path)!, r.Written);

    static string? Combat() => Repo.CombatSaveNamed("Combat_EncounterStart.sav");

    // ---- the field catalog -----------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_path_is_unique_and_resolves_back_to_its_field(bool combat)
    {
        var (ws, path) = Rig(combat ? Combat() : Repo.SampleSave());
        var ed = Open(ws, path);
        var all = ed.Fields.Entries;
        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(e => e.Path.ToLowerInvariant().Replace("_", "")).Distinct().Count());
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
        foreach (var e in all)
        {
            Assert.Equal(e.Id, ed.Fields.Resolve(e.Path).Id);
            Assert.Equal(e.Id, ed.Fields.Resolve(e.Path.ToUpperInvariant()).Id);                  // case does not matter
            Assert.Equal(e.Id, ed.Fields.Resolve(e.Id).Id);                                       // nor does using the raw id
        }
        Assert.Contains(all, e => e.Path.StartsWith("resource."));
        if (combat) Assert.Contains(all, e => e.Path.Contains(".attr.Health"));
    }

    [SkippableFact]
    public void An_unknown_path_names_the_nearest_ones()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var ed = Open(ws, path);
        var ex = Assert.Throws<ZcException>(() => ed.Fields.Resolve("resource.Nope"));
        Assert.Contains("did you mean", ex.Message);
        Assert.Contains("resource.", ex.Message);
        Assert.Throws<ZcException>(() => ed.Fields.Resolve("o999999999"));
    }

    // ---- scalar edits ----------------------------------------------------------------------------

    [SkippableFact]
    public void A_preview_changes_nothing_on_disk_and_a_bad_value_queues_nothing()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var before = Sha(path);
        var ed = Open(ws, path);
        var intel = ed.Fields.Resolve("resource.Intelligence");
        ed.Set(intel.Path, intel.Value == 100 ? 101 : 100);
        var line = Assert.Single(ed.Preview());
        Assert.Equal(intel.Value, line.From);
        Assert.Equal(before, Sha(path));
        Assert.Equal(1, ed.PendingCount);

        var fresh = Open(ws, path);
        Assert.Throws<ZcException>(() => fresh.Set("resource.Intelligence", 100000));          // over the in-game cap
        Assert.Throws<ZcException>(() => fresh.Set("resource.Intelligence", 1.5));             // whole numbers only
        Assert.Throws<ZcException>(() => fresh.Set("resource.Intelligence", double.NaN));
        Assert.Equal(0, fresh.PendingCount);
        Assert.Throws<ZcException>(() => fresh.Apply());                                         // nothing to write
    }

    [SkippableFact]
    public void A_copy_edit_leaves_the_original_alone_and_the_diff_names_exactly_what_changed()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var before = Sha(path);
        var ed = Open(ws, path);
        var credits = ed.Fields.Resolve("resource.Credits");
        ed.Set("resource.Credits", credits.Value + 1);
        var res = ed.Apply(asCopy: true, copyName: "SDK TEST COPY");
        Assert.True(res.Copy);
        Assert.Equal(before, Sha(path));

        var copy = Copy(ed, res);
        var reopened = Open(ws, copy);
        Assert.Equal(credits.Value + 1, reopened.Fields.Resolve("resource.Credits").Value);
        Assert.Equal("SDK TEST COPY", reopened.Save.Container.Comment);

        var diff = FieldDiff.Compare(ws, path, copy);
        var c = Assert.Single(diff.Changes);
        Assert.Equal(("resource.Credits", credits.Value, credits.Value + 1), (c.Path, c.From!.Value, c.To!.Value));
        Assert.Empty(diff.Notes);
    }

    [SkippableFact]
    public void Editing_an_operators_focus_moves_their_total_focus_with_it()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var ed = Open(ws, path);
        var op = ed.Save.View.Personnel.Roster.First(o => o.Focus != null && o.Focus.TotalRef != null && o.Focus.Value + 1 <= o.Focus.Max);
        var focus = $"operator.{FieldCatalog.Slug(op.Name)}.focus";
        var total = ed.Fields.Resolve(focus.Replace(".focus", ".total_focus"));
        ed.Set(focus, op.Focus!.Value + 1);
        var lines = ed.Preview();
        Assert.Equal(2, lines.Count);                                                           // focus and total focus
        var res = ed.Apply(asCopy: true);
        var after = Open(ws, Copy(ed, res));
        Assert.Equal(op.Focus.Value + 1, after.Fields.Resolve(focus).Value);
        Assert.Equal(total.Value + 1, after.Fields.Resolve(total.Path).Value);
    }

    [SkippableFact]
    public void The_sdk_writes_the_same_bytes_as_calling_the_service_directly()
    {
        var src = Repo.SampleSave();
        var (ws, path) = Rig(src, "sdk");
        var (_, direct) = Rig(src, "direct");
        var ed = Open(ws, path);
        var credits = ed.Fields.Resolve("resource.Credits");
        var intel = ed.Fields.Resolve("resource.Intelligence");
        ed.Set(credits.Path, credits.Value + 7);
        ed.Set(intel.Path, intel.Value == 100 ? 101 : 100);
        var viaSdk = Copy(ed, ed.Apply(asCopy: true));

        var dir = Path.GetDirectoryName(direct)!;
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "direct", "backups"), gameRunning: () => null);
        svc.Open("t", Path.GetFileName(direct));
        var res = svc.Apply(new[] { new FieldChange(credits.Id, credits.Value + 7), new FieldChange(intel.Id, intel.Value == 100 ? 101 : 100) }, asCopy: true);

        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, res.Written)), File.ReadAllBytes(viaSdk));
    }

    // ---- refusals, backups ----------------------------------------------------------------------

    [SkippableFact]
    public void A_running_game_refuses_an_in_place_write_but_not_a_copy_or_a_forced_write()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var before = Sha(path);
        var ed = Open(ws, path, running: "swzerocompany-win64-shipping");
        var credits = ed.Fields.Resolve("resource.Credits");
        ed.Set(credits.Path, credits.Value + 1);

        var refused = Assert.Throws<ZcRefusal>(() => ed.Apply());
        Assert.Contains("running", refused.Message);
        Assert.Equal(before, Sha(path));
        Assert.True(ed.Apply(asCopy: true).Copy);                                               // a copy never touches the original
        Assert.NotEqual(before, Sha(ed.Apply(force: true).Written is { } w ? Path.Combine(Path.GetDirectoryName(path)!, w) : path));
    }

    [SkippableFact]
    public void A_write_in_place_keeps_a_backup_that_restore_puts_back()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var before = Sha(path);
        var ed = Open(ws, path);
        var credits = ed.Fields.Resolve("resource.Credits");
        ed.Set(credits.Path, credits.Value + 1);
        var res = ed.Apply();
        Assert.False(res.Copy);
        Assert.NotNull(res.Backup);
        Assert.NotEqual(before, Sha(path));

        var again = Open(ws, path);
        var backup = again.Backups().First(b => !b.Original);
        again.Restore(backup.File);
        Assert.Equal(before, Sha(path));
        Assert.Throws<ZcRefusal>(() => Open(ws, path, running: "swzerocompany").Restore(backup.File));
        Assert.Throws<ZcException>(() => again.Restore("nope.sav"));
    }

    [SkippableFact]
    public void A_file_that_changed_after_it_was_opened_is_refused()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var ed = Open(ws, path);
        var credits = ed.Fields.Resolve("resource.Credits");
        ed.Set(credits.Path, credits.Value + 1);
        File.AppendAllText(path, "x");                                                          // the game autosaved, say
        Assert.Throws<ZcRefusal>(() => ed.Apply());
    }

    // ---- structural edits ------------------------------------------------------------------------

    [SkippableFact]
    public void A_script_of_values_and_actions_is_written_in_one_go_on_a_combat_save()
    {
        var (ws, path) = Rig(Combat());
        var ed = Open(ws, path);
        Assert.True(ed.IsCombat);
        var civilian = ed.Save.Combat!.Units.First(u => u.Team == "Civilian" && !u.Dead);
        var hero = ed.Save.Combat.Units.First(u => u.Team == "Player");
        var script = EditScript.Parse($$"""
            { "set": { "unit.{{hero.Index}}.attr.Health": 5 },
              "actions": [ { "do": "unit-dead", "args": [{{civilian.Index}}, true] }, { "do": "round", "args": [4] } ] }
            """);
        var queued = script.Queue(ed);
        Assert.Equal(3, queued.Count);
        var copy = Copy(ed, ed.Apply(asCopy: true));

        var after = Open(ws, copy);
        Assert.True(after.Save.Combat!.Units.First(u => u.Index == civilian.Index).Dead);
        Assert.Equal(4, after.Save.Combat.Round);
        Assert.Equal(5, after.Fields.Resolve($"unit.{hero.Index}.attr.Health").Value);

        var diff = FieldDiff.Compare(ws, path, copy);
        Assert.Contains(diff.Notes, n => n.Contains("died"));
        Assert.Contains(diff.Notes, n => n.StartsWith("round"));
        Assert.Contains(diff.Changes, c => c.Path == $"unit.{hero.Index}.attr.Health" && c.To == 5);
    }

    [SkippableFact]
    public void Actions_name_their_mistakes_and_refuse_what_the_save_cannot_do()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var hub = Open(ws, path);
        Assert.Contains("unknown action", Assert.Throws<ZcException>(() => hub.Do("nonsense", new[] { "x" })).Message);
        Assert.Contains("usage", Assert.Throws<ZcException>(() => hub.Do("heal", Array.Empty<string>())).Message);
        Assert.Contains("combat save", Assert.Throws<ZcException>(() => hub.Do("round", new[] { "3" })).Message);
        Assert.Contains("no operator", Assert.Throws<ZcException>(() => hub.Do("heal", new[] { "Nobody At All" })).Message);

        var (cws, cpath) = Rig(Combat(), "c");
        var combat = Open(cws, cpath);
        Assert.Contains("no unit 99", Assert.Throws<ZcException>(() => combat.Do("unit-dead", new[] { "99", "true" })).Message);
        Assert.Contains("true or false", Assert.Throws<ZcException>(() => combat.Do("unit-dead", new[] { "0", "maybe" })).Message);
        Assert.Contains("whole number", Assert.Throws<ZcException>(() => combat.Do("round", new[] { "1.5" })).Message);
        Assert.Equal(0, combat.PendingCount);
    }

    // ---- save check ------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_saves_pass_the_check(bool combat)
    {
        var (ws, path) = Rig(combat ? Combat() : Repo.SampleSave());
        var checks = SaveCheck.Run(ws, path);
        Assert.False(checks.HasFailure(), string.Join("; ", checks.Where(c => c.Status == CheckStatus.Fail).Select(c => $"{c.Name}: {c.Detail}")));
        Assert.Contains(checks, c => c.Name == "screens" && c.Status == CheckStatus.Ok);
    }

    [SkippableFact]
    public void A_metadata_size_that_disagrees_with_the_data_fails_the_check()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var c = SaveContainer.Load(File.ReadAllBytes(path));
        var text = Encoding.Unicode.GetString(c.Blobs["SaveGameMetaData.json"]);
        var broken = Regex.Replace(text, "(\"GameInstanceSize\"\\s*:\\s*)\\d+", "${1}1");
        Skip.If(broken == text, "this save records no GameInstanceSize");
        File.WriteAllBytes(path, c.Rebuild(c.Gvas, new Dictionary<string, byte[]> { ["SaveGameMetaData.json"] = Encoding.Unicode.GetBytes(broken) }));
        var checks = SaveCheck.Run(ws, path);
        var meta = checks.Single(x => x.Name == "metadata");
        Assert.Equal(CheckStatus.Fail, meta.Status);
        Assert.Contains("GameInstanceSize", meta.Detail);
    }
}
