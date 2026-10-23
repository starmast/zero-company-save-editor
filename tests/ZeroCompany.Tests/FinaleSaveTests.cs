using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Platform;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>
/// The campaign finale's encounter-start autosave (game 1.2): several encounter actors, story units on no team, timers that start
/// other actors rather than fail objectives. Things the earlier combat samples never had.
/// </summary>
public class FinaleSaveTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-finale-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    SaveEditor Open()
    {
        var src = Repo.CombatSaveNamed("Combat_Finale.sav");
        Skip.If(src == null, "no finale sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(src!));
        File.Copy(src!, path);
        return SaveEditor.Open(ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false), path, () => null);
    }

    [SkippableFact]
    public void Units_on_no_team_are_listed_but_their_team_cannot_be_changed()
    {
        var ed = Open();
        var cb = ed.Save.Combat!;
        var story = cb.Units.Where(u => !CombatOps.Teams.Contains(u.Team)).ToList();
        Assert.NotEmpty(story);
        var ex = Assert.Throws<ZcException>(() => ed.Do("unit-team", new[] { story[0].Index.ToString(), "Player" }));
        Assert.Contains("no side", ex.Message);
        Assert.Equal(0, ed.PendingCount);
        // the Core check holds even when the queue is bypassed
        Assert.Throws<ZeroCompany.Core.Gvas.GvasException>(() => CombatOps.SetTeam(ed.Save.Gvas, story[0].Index, "Player", new CutInfo()));
    }

    [SkippableFact]
    public void Timers_say_what_they_start_and_waves_are_numbered_per_encounter()
    {
        var ed = Open();
        var cb = ed.Save.Combat!;
        Assert.Contains(cb.Timers, t => t.Starts.Count > 0);
        var rv = cb.Reinforcements;
        var logics = rv.Configs.Select(c => c.Logic).Distinct().ToList();
        Assert.True(logics.Count > 1);
        Assert.Equal("Encounter 1 · ", rv.EncounterPrefix(logics.Min()));
        Assert.Equal($"Encounter {logics.Count} · ", rv.EncounterPrefix(logics.Max()));
        Assert.Equal(CombatOps.TurnStandard, CombatOps.TurnStructureOf(ed.Save.Gvas));
    }

    [SkippableFact]
    public void A_normal_edit_still_writes_and_verifies()
    {
        var ed = Open();
        var enemy = ed.Save.Combat!.Units.First(u => u.Team == "Enemy" && !u.Dead);
        ed.Do("unit-dead", new[] { enemy.Index.ToString(), "true" });
        var res = ed.Apply(asCopy: true);
        Assert.True(res.Copy);
    }
}
