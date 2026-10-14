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

    /// <summary>The MOD BASE 14 hub save (crisis missions, an Imperial mission), when it is in saves/.</summary>
    (SaveService Svc, string Path) NewCrisisRig()
    {
        var src = Repo.CombatSaveNamed("Hub_Missions.sav");
        Skip.If(src == null, "no Hub_Missions.sav fixture");
        var dir = Path.Combine(_tmp, "saves2"); Directory.CreateDirectory(dir);
        File.Copy(src!, Path.Combine(dir, "Hub_Missions.sav"));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups2"), gameRunning: () => null);
        svc.Open("t", "Hub_Missions.sav");
        return (svc, Path.Combine(dir, "Hub_Missions.sav"));
    }

    [SkippableFact]
    public void A_missions_time_on_offer_can_be_moved_but_not_into_the_future()
    {
        var (svc, path) = NewCrisisRig();
        var g = Load(path);
        long now = MissionOps.StrategyTurn(g);
        var old = MissionOps.Available(g).First(m => m.Available && m.TurnAdded < now);
        Act(svc, new SaveAction.SetMissionTurnAdded(old.Id, (int)now));
        var after = MissionOps.Available(Load(path));
        Assert.Equal(now, after.Single(m => m.Id == old.Id).TurnAdded);
        foreach (var m in MissionOps.Available(g).Where(m => m.Id != old.Id)) Assert.Equal(m, after.Single(a => a.Id == m.Id));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionTurnAdded(old.Id, (int)now + 1)));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionTurnAdded(old.Id, -1)));
    }

    [SkippableFact]
    public void A_crisis_mission_can_be_pointed_at_another_coil_upgrade_and_other_missions_cannot()
    {
        var (svc, path) = NewCrisisRig();
        var list = MissionOps.Available(Load(path));
        var crisis = list.First(m => m.CrisisEffect.Length > 0);
        const string Other = "/Game/Game/GameData/Abilities/ResultEffects/CrisisEffects/Coil/Brute/DA_Brute_Crisis_Major_A.DA_Brute_Crisis_Major_A";
        Assert.NotEqual(Other, crisis.CrisisEffect);
        Act(svc, new SaveAction.SetMissionCrisisEffect(crisis.Id, Other));
        var after = MissionOps.Available(Load(path));
        Assert.Equal(Other, after.Single(m => m.Id == crisis.Id).CrisisEffect);
        foreach (var m in list.Where(m => m.Id != crisis.Id)) Assert.Equal(m, after.Single(a => a.Id == m.Id));
        var plain = list.First(m => m.CrisisEffect.Length == 0 && m.Available);
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionCrisisEffect(plain.Id, Other)));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetMissionCrisisEffect(crisis.Id, "/Game/Elsewhere/DA_X.DA_X")));
    }

    [Fact]
    public void A_crisis_effect_path_gives_the_id_the_coil_upgrade_list_uses()
    {
        Assert.Equal("Major.Guardian_A", ZeroCompany.GameData.Models.GameDatabase.CrisisIdOf("/Game/x/CrisisEffects/Coil/Guardian/DA_Guardian_Crisis_Major_A.DA_Guardian_Crisis_Major_A"));
        Assert.Equal("Minor.B1_B", ZeroCompany.GameData.Models.GameDatabase.CrisisIdOf("/Game/x/DA_B1_Crisis_Minor_B.DA_B1_Crisis_Minor_B"));
        Assert.Equal("", ZeroCompany.GameData.Models.GameDatabase.CrisisIdOf("/Game/x/DA_Guardian_Crisis.DA_Guardian_Crisis"));
    }

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
