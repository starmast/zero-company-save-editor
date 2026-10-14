using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>The enemy faction and threat tier of the missions the Den offers (StrategyData/AvailableMissions).</summary>
public class MissionTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-mission-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (SaveService Svc, string Path) NewRig()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), gameRunning: () => null);
        svc.Open("t", name);
        return (svc, Path.Combine(dir, name));
    }

    static GvasFile Load(string path) => new(SaveContainer.Load(File.ReadAllBytes(path)).Gvas);
    static void Act(SaveService svc, params SaveAction[] actions) => svc.Apply(Array.Empty<FieldChange>(), force: true, actions: actions);

    [SkippableFact]
    public void The_den_lists_its_missions_with_their_enemy_sets()
    {
        var (_, path) = NewRig();
        var list = MissionOps.Available(Load(path));
        Skip.If(list.Count == 0, "the sample save offers no missions");
        Assert.All(list, m => { Assert.Equal(32, m.Id.Length); Assert.NotEmpty(m.Asset); });
        Assert.Contains(list, m => m.Available && m.SpawnSet.Contains("/SpawnSets/Faction/"));
    }

    [SkippableFact]
    public void Changing_a_missions_enemy_set_rewrites_both_paths_and_nothing_else()
    {
        var (svc, path) = NewRig();
        var before = MissionOps.Available(Load(path));
        var target = before.FirstOrDefault(m => m.Available && m.SpawnSet.Length > 0);
        Skip.If(target == null, "no available mission with an enemy set");
        var other = CombatOps.SpawnSetChoices(target!.SpawnSet).First(c => CombatOps.IsOtherFaction(target.SpawnSet, c));

        Act(svc, new SaveAction.SetMissionSpawnSet(target.Id, other));

        var after = MissionOps.Available(Load(path));
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(other, after.Single(m => m.Id == target.Id).SpawnSet);
        foreach (var m in before.Where(m => m.Id != target.Id)) Assert.Equal(m, after.Single(a => a.Id == m.Id));
        // the soft path the game also keeps has to agree with the object path
        var g = Load(path);
        var soft = g.Walk(new[] { g.StrategyDataOrNull()! }).Where(n => n.Name == "SoftSelectedSpawnSet").Select(n => g.GetStr(n)).ToList();
        Assert.Contains(other[..other.IndexOf('.')], soft);
    }

    [SkippableFact]
    public void Unknown_missions_unknown_sets_and_repeated_changes_are_refused()
    {
        var (svc, path) = NewRig();
        var target = MissionOps.Available(Load(path)).FirstOrDefault(m => m.Available && m.SpawnSet.Length > 0);
        Skip.If(target == null, "no available mission with an enemy set");
        var other = CombatOps.SpawnSetChoices(target!.SpawnSet)[1];
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionSpawnSet(new string('0', 32), other)));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionSpawnSet(target.Id, "/Game/Elsewhere/SS_X.SS_X")));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionSpawnSet(target.Id, other), new SaveAction.SetMissionSpawnSet(target.Id, other)));
        Assert.Equal(target.SpawnSet, MissionOps.Available(Load(path)).Single(m => m.Id == target.Id).SpawnSet);       // nothing was written
    }
}
