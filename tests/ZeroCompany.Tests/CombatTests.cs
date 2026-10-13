using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Tests;

/// <summary>Combat autosave editing: unit values, dead flag, active effects and position, all through the normal verified apply.</summary>
public class CombatTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-combat-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (SaveService Svc, string Name, string Path) NewRig(string? file = null)
    {
        var src = file == null ? Repo.CombatSave() : Repo.CombatSaveNamed(file);
        Skip.If(src == null, "no combat sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), gameRunning: () => null);
        svc.Open("t", name);
        return (svc, name, Path.Combine(dir, name));
    }

    static GvasFile Gvas(string path) => new(SaveContainer.Load(File.ReadAllBytes(path)).Gvas);

    /// <summary>Apply combat actions with no field edits.</summary>
    static void Act(SaveService svc, params SaveAction[] actions) => svc.Apply(Array.Empty<FieldChange>(), force: true, actions: actions);

    static CombatUnitView Enemy(CombatView v) => v.Units.First(u => u.Team == "Enemy");

    [SkippableFact]
    public void Reads_every_unit_with_health_ap_position_and_effects()
    {
        var (svc, _, _) = NewRig();
        var cb = svc.Current!.Combat;
        Assert.NotNull(cb);
        Assert.Equal(17, cb!.Units.Count);
        Assert.Equal(4, cb.Units.Count(u => u.Team == "Player"));
        Assert.True(cb.Units.Count(u => u.Team == "Enemy") >= 7);
        var e = Enemy(cb);
        Assert.Equal(62, e.Attr("Health")!.Value);
        Assert.Equal(62, e.Attr("MaxHealth")!.Value);
        Assert.Equal(3, e.Attr("ActionPoints")!.Value);
        Assert.Contains(e.Attributes, a => a.Set == "BitReactorHealthSet" && a.Name == "Health");
        Assert.Contains(e.Attributes, a => a.Set == "BitReactorCombatSet" && a.Name == "ActionPoints");
        Assert.NotNull(e.X); Assert.NotNull(e.Y); Assert.NotNull(e.Z);
        Assert.NotEmpty(e.Effects);
        Assert.All(cb.Units, u => Assert.False(u.Dead));
    }

    [SkippableFact]
    public void Hub_saves_have_no_combat_view()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "hub"); Directory.CreateDirectory(dir);
        File.Copy(src!, Path.Combine(dir, Path.GetFileName(src!)));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "b2"), gameRunning: () => null);
        Assert.Null(svc.Open("t", Path.GetFileName(src!)).Combat);
    }

    [SkippableFact]
    public void Scalar_edits_health_ap_advantage_and_position_round_trip_in_place()
    {
        var (svc, _, path) = NewRig();
        var cb = svc.Current!.Combat!;
        var e = Enemy(cb);
        var sizeBefore = SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length;
        var res = svc.Apply(new[]
        {
            new FieldChange(e.Attr("Health")!.Id, 1),
            new FieldChange(e.Attr("ActionPoints")!.Id, 7),
            new FieldChange(e.Advantage!.Id, 4),
            new FieldChange(e.X!.Id, e.X.Value + 150.5),
            new FieldChange(e.Z!.Id, e.Z.Value - 20),
        }, force: true);
        Assert.Equal(5, res.Count);

        var e2 = Enemy(svc.Current!.Combat!);
        Assert.Equal(1, e2.Attr("Health")!.Value);
        Assert.Equal(62, e2.Attr("MaxHealth")!.Value);
        Assert.Equal(7, e2.Attr("ActionPoints")!.Value);
        Assert.Equal(4, e2.Advantage!.Value);
        Assert.Equal(e.X.Value + 150.5, e2.X!.Value, 6);
        Assert.Equal(e.Y!.Value, e2.Y!.Value);
        Assert.Equal(e.Z.Value - 20, e2.Z!.Value, 6);
        Assert.Equal(sizeBefore, SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length);   // nothing grew or shrank
        foreach (var (u0, u1) in cb.Units.Zip(svc.Current.Combat!.Units).Where(p => p.First.Index != e.Index))
            Assert.Equal(u0.Attr("Health")!.Value, u1.Attr("Health")!.Value);
    }

    [SkippableFact]
    public void Dead_flag_can_be_set_and_cleared()
    {
        var (svc, _, _) = NewRig();
        var e = Enemy(svc.Current!.Combat!);
        Act(svc, new SaveAction.SetUnitDead(e.Index, true));
        var cb = svc.Current!.Combat!;
        Assert.True(cb.Units[e.Index].Dead);
        Assert.Single(cb.Units, u => u.Dead);
        Act(svc, new SaveAction.SetUnitDead(e.Index, false));
        Assert.All(svc.Current!.Combat!.Units, u => Assert.False(u.Dead));
    }

    [SkippableFact]
    public void Removing_an_effect_shrinks_only_that_list_and_keeps_the_save_consistent()
    {
        var (svc, _, path) = NewRig();
        var cb = svc.Current!.Combat!;
        var e = Enemy(cb);
        var eff = e.Effects[0];
        var sizeBefore = SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length;
        Act(svc, new SaveAction.RemoveUnitEffect(e.Index, eff.Index, eff.Def));

        var cb2 = svc.Current!.Combat!;
        Assert.Equal(e.Effects.Count - 1, cb2.Units[e.Index].Effects.Count);
        Assert.Equal(e.Effects.Skip(1).Select(x => x.Def), cb2.Units[e.Index].Effects.Select(x => x.Def));
        foreach (var u in cb.Units.Where(u => u.Index != e.Index))
            Assert.Equal(u.Effects.Select(x => x.Def), cb2.Units[u.Index].Effects.Select(x => x.Def));
        Assert.True(SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length < sizeBefore);
    }

    [SkippableFact]
    public void Stale_effect_reference_is_refused_and_nothing_is_written()
    {
        var (svc, _, path) = NewRig();
        var e = Enemy(svc.Current!.Combat!);
        var before = File.ReadAllBytes(path);
        var ex = Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.RemoveUnitEffect(e.Index, 0, "GE_NotWhatIsThere") }));
        Assert.Contains("re-open", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void Copying_an_effect_onto_another_unit_appends_it()
    {
        var (svc, _, _) = NewRig();
        var cb = svc.Current!.Combat!;
        var enemies = cb.Units.Where(u => u.Team == "Enemy").ToList();
        var src = enemies[0]; var dst = enemies[1];
        var eff = src.Effects[0];
        svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.CopyUnitEffect(dst.Index, src.Index, eff.Index, eff.Def) });
        var cb2 = svc.Current!.Combat!;
        Assert.Equal(dst.Effects.Count + 1, cb2.Units[dst.Index].Effects.Count);
        Assert.Equal(eff.Def, cb2.Units[dst.Index].Effects[^1].Def);
        Assert.Equal(src.Effects.Count, cb2.Units[src.Index].Effects.Count);
    }

    [SkippableFact]
    public void Everything_together_in_one_apply_including_a_removal_and_a_copy()
    {
        var (svc, _, _) = NewRig();
        var cb = svc.Current!.Combat!;
        var enemies = cb.Units.Where(u => u.Team == "Enemy").ToList();
        var a = enemies[0]; var b = enemies[1];
        var pending = new PendingChanges();
        pending.SetEdit(a.Attr("Health")!, 5);
        pending.SetDead(b.Index, true, saved: false);
        pending.ToggleEffectRemoval(a.Index, 0);
        pending.AddEffectCopy(b.Index, a.Index, 0);
        Assert.Equal(4, pending.Count);
        svc.Apply(pending.BuildChanges(), force: true, actions: pending.BuildCombatActions(cb));
        var cb2 = svc.Current!.Combat!;
        Assert.Equal(5, cb2.Units[a.Index].Attr("Health")!.Value);
        Assert.True(cb2.Units[b.Index].Dead);
        Assert.Equal(a.Effects.Count - 1, cb2.Units[a.Index].Effects.Count);
        Assert.Equal(b.Effects.Count + 1, cb2.Units[b.Index].Effects.Count);
    }

    [SkippableFact]
    public void Objectives_are_listed_and_can_be_marked_succeeded_and_back()
    {
        var (svc, _, path) = NewRig();
        var cb = svc.Current!.Combat!;
        var interacts = cb.Objectives.Where(o => o.Name.Contains("CriticalInteract")).ToList();
        Assert.NotEmpty(interacts);
        Assert.All(interacts.Where(o => o.Status != null), o => Assert.Equal("Active", o.Status));
        var target = interacts.First(o => o.Status == "Active");
        var sizeBefore = SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length;

        svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetObjectiveStatus(target.Index, target.Name, "Succeeded") });
        var cb2 = svc.Current!.Combat!;
        Assert.Equal("Succeeded", cb2.Objectives.First(o => o.Index == target.Index).Status);
        foreach (var o in cb.Objectives.Where(o => o.Index != target.Index))
            Assert.Equal(o.Status, cb2.Objectives.First(x => x.Index == o.Index).Status);
        Assert.Equal(sizeBefore + ("Succeeded".Length - "Active".Length), SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length);
        Assert.Equal(cb.Units.Select(u => u.Attr("Health")!.Value), cb2.Units.Select(u => u.Attr("Health")!.Value));

        svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetObjectiveStatus(target.Index, target.Name, "Active") });
        Assert.Equal(sizeBefore, SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length);
        Assert.Equal("Active", svc.Current!.Combat!.Objectives.First(o => o.Index == target.Index).Status);
    }

    [SkippableFact]
    public void Objective_changes_with_a_wrong_name_or_an_unknown_status_are_refused()
    {
        var (svc, _, path) = NewRig();
        var o = svc.Current!.Combat!.Objectives.First(x => x.CanEdit);
        var before = File.ReadAllBytes(path);
        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetObjectiveStatus(o.Index, "BP_Something_Else_C_0", "Succeeded") }));
        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetObjectiveStatus(o.Index, o.Name, "Exploded") }));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void Pending_objective_change_is_dropped_when_it_matches_the_saved_status()
    {
        var (svc, _, _) = NewRig();
        var cb = svc.Current!.Combat!;
        var o = cb.Objectives.First(x => x.CanEdit);
        var p = new PendingChanges();
        p.SetObjective(o.Index, "Succeeded", o.Status);
        Assert.Equal(1, p.Count);
        Assert.Single(p.BuildCombatActions(cb));
        p.SetObjective(o.Index, o.Status!, o.Status);
        Assert.Equal(0, p.Count);
    }

    [SkippableFact]
    public void A_unit_can_change_team_in_both_directions_touching_only_its_tag_and_friendly_flag()
    {
        var (svc, _, path) = NewRig();
        var cb = svc.Current!.Combat!;
        var enemy = cb.Units.First(u => u.Team == "Enemy");
        var ally = cb.Units.First(u => u.Team == "Player");

        svc.Apply(Array.Empty<FieldChange>(), force: true, actions: new SaveAction[]
        {
            new SaveAction.SetUnitTeam(enemy.Index, "Player"),
            new SaveAction.SetUnitTeam(ally.Index, "Enemy"),
        });
        var g = Gvas(path);
        Assert.Equal("Player", CombatOps.TeamOf(g, enemy.Index));
        Assert.True(CombatOps.IsFriendly(g, enemy.Index));
        Assert.Equal("Enemy", CombatOps.TeamOf(g, ally.Index));
        Assert.False(CombatOps.IsFriendly(g, ally.Index));

        var cb2 = svc.Current!.Combat!;
        Assert.Equal("Player", cb2.Units[enemy.Index].Team);
        Assert.Equal("Enemy", cb2.Units[ally.Index].Team);
        // nothing else about either unit moved: health, position, effects, and every other unit's team
        Assert.Equal(enemy.Attr("Health")!.Value, cb2.Units[enemy.Index].Attr("Health")!.Value);
        Assert.Equal(enemy.X!.Value, cb2.Units[enemy.Index].X!.Value);
        Assert.Equal(enemy.Effects.Select(e => e.Def), cb2.Units[enemy.Index].Effects.Select(e => e.Def));
        foreach (var u in cb.Units.Where(u => u.Index != enemy.Index && u.Index != ally.Index))
            Assert.Equal(u.Team, cb2.Units[u.Index].Team);
        Assert.Equal(cb.Units.Count, cb2.Units.Count);
        // behaviour and the tag container follow the team, as the game does it
        Assert.True(CombatOps.BehaviorMatches(g, enemy.Index, "Player"));
        Assert.True(CombatOps.BehaviorMatches(g, ally.Index, "Enemy"));
        Assert.Contains("BitReactor.Team.Player", CombatOps.UnitTags(g, enemy.Index)!);
        Assert.DoesNotContain("BitReactor.Team.Enemy", CombatOps.UnitTags(g, enemy.Index)!);
        Assert.Contains("BitReactor.Team.Enemy", CombatOps.UnitTags(g, ally.Index)!);
        Assert.DoesNotContain("BitReactor.Team.Player", CombatOps.UnitTags(g, ally.Index)!);
    }

    /// <summary>
    /// The rescue in COMBAT BASE 2.3 -> 2.4: the game turned the civilian Baker into a squad member. Doing the same to the
    /// earlier save must give the same team tag, friendly flag, behaviour and team tag container the game wrote.
    /// </summary>
    [SkippableFact]
    public void Switching_a_rescued_civilian_matches_what_the_game_itself_wrote()
    {
        var (svc, _, path) = NewRig("Combat_Rescue_before.sav");
        Skip.If(Repo.CombatSaveNamed("Combat_Rescue_after.sav") == null, "no 'after' sample");
        var baker = svc.Current!.Combat!.Units.First(u => u.Actor == "Char_Baker_C_0");
        Assert.Equal("Civilian", baker.Team);

        Act(svc, new SaveAction.SetUnitTeam(baker.Index, "Player"));
        var mine = Gvas(path);
        var theirs = new GvasFile(SaveContainer.Load(File.ReadAllBytes(Repo.CombatSaveNamed("Combat_Rescue_after.sav")!)).Gvas);
        int i = baker.Index;
        int j = Enumerable.Range(0, CombatOps.Units(theirs).Count).First(k => CombatOps.ActorName(theirs, CombatOps.Units(theirs)[k]) == "Char_Baker_C_0");

        Assert.Equal(CombatOps.TeamTagOf(theirs, j), CombatOps.TeamTagOf(mine, i));
        Assert.Equal(CombatOps.IsFriendly(theirs, j), CombatOps.IsFriendly(mine, i));
        string Prop(GvasFile g, int u, string n) =>
            g.GetStr(g.Child(g.Child(g.Child(CombatOps.Units(g)[u], "ActorWrapper"), "ArchiveBytes"), n)!);
        Assert.Equal(Prop(theirs, j, "BehaviorClass"), Prop(mine, i, "BehaviorClass"));
        Assert.Equal(Prop(theirs, j, "StateMachine"), Prop(mine, i, "StateMachine"));
        // the game also keeps a copy of a spawned unit's behaviour in SaveGameSpawnedActors; it must match the game's too
        var mineSpawned = new GvasFile(SaveContainer.Load(File.ReadAllBytes(path)).Blobs["SaveGameSpawnedActors"]);
        var theirSpawned = new GvasFile(SaveContainer.Load(File.ReadAllBytes(Repo.CombatSaveNamed("Combat_Rescue_after.sav")!)).Blobs["SaveGameSpawnedActors"]);
        Assert.Equal(CombatOps.SpawnedBehavior(theirSpawned, "Char_Baker_C_0"), CombatOps.SpawnedBehavior(mineSpawned, "Char_Baker_C_0"));
        Assert.Contains("Behavior_Player", CombatOps.SpawnedBehavior(mineSpawned, "Char_Baker_C_0")!);
        var tt = CombatOps.UnitTags(theirs, j)!; var mt = CombatOps.UnitTags(mine, i)!;
        Assert.Contains("BitReactor.Team.Player", mt);
        Assert.DoesNotContain("BitReactor.Team.Civilian", mt);
        // the only tags that differ from the game's are the ones the game adds/removes for being rescued (downed state, advantage)
        var diff = mt.Except(tt).Concat(tt.Except(mt)).OrderBy(x => x).ToList();
        Assert.All(diff, d => Assert.True(d.Contains("Downed") || d.Contains("BlockRally") || d.Contains("HasAdvantage"), d));
    }

    [SkippableFact]
    public void A_team_switch_changes_only_that_units_copy_in_the_spawned_actors_entry()
    {
        var (svc, _, path) = NewRig("Combat_Rescue_before.sav");
        var before = SaveContainer.Load(File.ReadAllBytes(path)).Blobs["SaveGameSpawnedActors"];
        var baker = svc.Current!.Combat!.Units.First(u => u.Actor == "Char_Baker_C_0");
        Act(svc, new SaveAction.SetUnitTeam(baker.Index, "Player"));
        var after = SaveContainer.Load(File.ReadAllBytes(path)).Blobs["SaveGameSpawnedActors"];

        GvasFile b = new(before), a = new(after);
        Dictionary<string, byte[]> Els(GvasFile g) => g.Root.First(r => r.Name == "TrackedSpawnedActors").Children!
            .ToDictionary(e => g.GetStr(g.Child(e, "ActorName")!) + "#" + e.Name, e => g.Slice(e.Start, e.End));
        var eb = Els(b); var ea = Els(a);
        Assert.Equal(eb.Count, ea.Count);
        var changed = eb.Keys.Where(k => !eb[k].AsSpan().SequenceEqual(ea[k])).Select(k => k.Split('#')[0]).ToList();
        Assert.Equal(new[] { "Char_Baker_C_0" }, changed);
        Assert.Contains("Behavior_Enemy", CombatOps.SpawnedBehavior(b, "Char_Baker_C_0")!);
        Assert.Contains("Behavior_Player", CombatOps.SpawnedBehavior(a, "Char_Baker_C_0")!);
    }


    /// <summary>COMBAT BASE 3.1: Coil (Enemy) and Imperials (Enemy2) fight each other and the squad. Enemy2 is a normal hostile team.</summary>
    [SkippableFact]
    public void Enemy_and_Enemy2_switch_by_team_tag_alone_and_keep_enemy_behaviour()
    {
        var (svc, _, path) = NewRig("Combat_ThreeWay.sav");
        var cb = svc.Current!.Combat!;
        Assert.Equal(6, cb.Units.Count(u => u.Team == "Enemy2"));
        var coil = cb.Units.First(u => u.Actor == "Char_Enemy_Coil_Enforcer_C_1");
        var imp = cb.Units.First(u => u.Actor == "Char_Enemy_Imperial_Trooper_C_1");
        var before = Gvas(path);
        var impTagsBefore = CombatOps.UnitTags(before, imp.Index)!;

        Act(svc, new SaveAction.SetUnitTeam(coil.Index, "Enemy2"), new SaveAction.SetUnitTeam(imp.Index, "Enemy"));
        var g = Gvas(path);
        Assert.Equal("Enemy2", CombatOps.TeamOf(g, coil.Index));
        Assert.Equal("Enemy", CombatOps.TeamOf(g, imp.Index));
        Assert.False(CombatOps.IsFriendly(g, coil.Index));
        Assert.False(CombatOps.IsFriendly(g, imp.Index));
        Assert.True(CombatOps.BehaviorMatches(g, coil.Index, "Enemy2"));
        var ct = CombatOps.UnitTags(g, coil.Index)!;
        Assert.Contains("BitReactor.Team.Enemy2", ct);
        Assert.DoesNotContain("BitReactor.Team.Enemy", ct);
        var it = CombatOps.UnitTags(g, imp.Index)!;
        Assert.Contains("BitReactor.Team.Enemy", it);
        Assert.DoesNotContain("BitReactor.Team.Enemy2", it);
        Assert.Equal(impTagsBefore.Count, it.Count);
        Assert.Equal(coil.Attr("Health")!.Value, svc.Current!.Combat!.Units[coil.Index].Attr("Health")!.Value);
    }

    [SkippableFact]
    public void Fire_zones_and_destructibles_are_listed_and_edit_in_place()
    {
        var (svc, _, path) = NewRig("Combat_ThreeWay.sav");
        var cb = svc.Current!.Combat!;
        Assert.Equal(5, cb.Hazards.Count);
        Assert.All(cb.Hazards, h => { Assert.Equal(250, h.Radius); Assert.NotNull(h.X); Assert.InRange(h.TurnsLeft.Value, 9000, 9999); });
        Assert.Equal(28, cb.Destructibles.Count);
        Assert.Contains(cb.Destructibles, d => d.Health.Value < d.MaxHealth.Value && d.Health.Value > 0);   // the half-broken crate
        Assert.Contains(cb.Destructibles, d => d.Health.Value == 0);

        var changes = cb.Destructibles.Where(d => d.Health.Value < d.MaxHealth.Value)
            .Select(d => new FieldChange(d.Health.Id, d.MaxHealth.Value)).ToList();
        changes.Add(new FieldChange(cb.Hazards[0].TurnsLeft.Id, 2));
        var sizeBefore = SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length;
        svc.Apply(changes, force: true);
        Assert.Equal(sizeBefore, SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length);        // scalars: edited in place
        var after = svc.Current!.Combat!;
        Assert.All(after.Destructibles, d => Assert.Equal(d.MaxHealth.Value, d.Health.Value));
        Assert.Equal(2, after.Hazards[0].TurnsLeft.Value);
        Assert.Equal(cb.Hazards[1].TurnsLeft.Value, after.Hazards[1].TurnsLeft.Value);
    }

    [SkippableFact]
    public void Enemy_spawn_set_lists_known_tiers_and_can_be_swapped_and_bad_ones_are_refused()
    {
        var (svc, _, path) = NewRig("Combat_ThreeWay.sav");
        var sv = svc.Current!.Combat!.SpawnSet!;
        Assert.Contains("SS_Coil_Threat_10", sv.Current);
        Assert.Equal("Coil threat 10", CombatOps.SpawnSetLabel(sv.Current));
        Assert.Equal(10, sv.Choices.Count(c => !c.Label.Contains("other faction")));      // Coil 1-10, as the game ships them
        Assert.Equal("Coil threat 10", sv.Choices[0].Label);
        Assert.Contains(sv.Choices, c => c.Label == "Separatist threat 3 (other faction)");
        Assert.StartsWith("Coil threat", sv.Choices[0].Label);
        var other = sv.Choices.First(c => c.Label == "Coil threat 5").Value;

        Act(svc, new SaveAction.SetEnemySpawnSet(other));
        Assert.Equal(other, CombatOps.EnemySpawnSetOf(Gvas(path)));
        Assert.Equal(other, svc.Current!.Combat!.SpawnSet!.Current);
        var cross = svc.Current!.Combat!.SpawnSet!.Choices.First(c => c.Label == "Separatist threat 3 (other faction)").Value;
        Act(svc, new SaveAction.SetEnemySpawnSet(cross));
        Assert.Equal(cross, CombatOps.EnemySpawnSetOf(Gvas(path)));

        var before = File.ReadAllBytes(path);
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetEnemySpawnSet("/Game/Nope.Nope")));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void An_imperial_mission_offers_its_only_tier_first_then_the_other_factions_marked_experimental()
    {
        var (svc, _, _) = NewRig("Combat_Imperial.sav");
        var sv = svc.Current!.Combat!.SpawnSet!;
        Assert.Equal("Imperial threat 9", sv.Choices[0].Label);
        Assert.Equal(2, sv.Choices.Count(c => !c.Label.Contains("other faction")));       // Imperial 1 and 9
        Assert.Contains(sv.Choices, c => c.Label == "Coil threat 10 (other faction)");
        Assert.Equal(sv.Choices.Count, sv.Choices.Select(c => c.Value).Distinct().Count());
    }

    [SkippableFact]
    public void Turn_structure_can_be_added_and_removed_and_re_adding_it_reproduces_the_games_own_bytes()
    {
        // COMBAT BASE 3.1 saves the three-way structure itself: remove it, add it back, and the file must be byte-identical.
        var (svc, _, path) = NewRig("Combat_ThreeWay.sav");
        var original = SaveContainer.Load(File.ReadAllBytes(path)).Gvas;
        Assert.Equal(CombatOps.TurnThreeWay, svc.Current!.Combat!.TurnStructure!.Current);
        Act(svc, new SaveAction.SetTurnStructure(CombatOps.TurnStandard));
        Assert.Equal(CombatOps.TurnStandard, CombatOps.TurnStructureOf(Gvas(path)));
        Assert.True(SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length < original.Length);
        Act(svc, new SaveAction.SetTurnStructure(CombatOps.TurnThreeWay));
        Assert.Equal(original, SaveContainer.Load(File.ReadAllBytes(path)).Gvas);
    }


    [SkippableFact]
    public void Enemy2_and_the_turn_structure_are_only_offered_in_three_way_battles()
    {
        var (svc, _, path) = NewRig("Combat_ThreeWay.sav");
        Assert.True(svc.Current!.Combat!.ThreeWay);
        Assert.NotNull(svc.Current.Combat.TurnStructure);

        var (svc2, _, path2) = NewRig("Combat_Imperial.sav");               // Brentaal: an ordinary two-sided mission
        var cb = svc2.Current!.Combat!;
        Assert.False(cb.ThreeWay);
        Assert.Null(cb.TurnStructure);
        var before = File.ReadAllBytes(path2);
        var unit = cb.Units.First(u => u.Team == "Enemy");
        Assert.ThrowsAny<Exception>(() => Act(svc2, new SaveAction.SetUnitTeam(unit.Index, "Enemy2")));
        Assert.Equal(before, File.ReadAllBytes(path2));
        Act(svc2, new SaveAction.SetUnitTeam(unit.Index, "Civilian"));       // the other teams still work
    }


    // ---- scenery: a prop stays wrecked because its actor record says Destroyed, not because of its health -----------------
    [SkippableFact]
    public void Destroyed_props_are_found_by_the_tag_in_their_actor_record()
    {
        var (svc, _, _) = NewRig("Combat_Scenery.sav");                      // COMBAT BASE 5.3, the game's own save: four wrecked props
        var cb = svc.Current!.Combat!;
        Assert.Equal(4, cb.Destructibles.Count);
        Assert.All(cb.Destructibles, d => { Assert.True(d.Destroyed); Assert.Equal(0, d.Health.Value); });
        Assert.Equal(4, cb.DestroyedProps.Count);
        Assert.Equal(cb.Destructibles.Select(d => d.Actor).OrderBy(x => x), cb.DestroyedProps.OrderBy(x => x));
    }

    [SkippableFact]
    public void Restoring_destroyed_scenery_flips_the_record_to_pristine_and_puts_health_back()
    {
        var (svc, _, path) = NewRig("Combat_Scenery.sav");
        var cb = svc.Current!.Combat!;
        var before = Gvas(path);
        var lengthBefore = SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length;
        Act(svc, new SaveAction.RestoreScenery(cb.DestroyedProps));

        var g = Gvas(path);
        Assert.Empty(CombatOps.DestroyedActors(g));
        foreach (var d in cb.Destructibles)
        {
            Assert.True(CombatOps.IsPristine(g, d.Actor));
            var h = CombatOps.PropHealth(g, d.Actor)!.Value;
            Assert.Equal(h.Max, h.Health);
            Assert.True(h.Max > 0);
        }
        Assert.Equal(lengthBefore - 4, SaveContainer.Load(File.ReadAllBytes(path)).Gvas.Length);        // each record is one byte shorter
        Assert.All(svc.Current!.Combat!.Destructibles, d => Assert.False(d.Destroyed));
        Assert.Empty(svc.Current.Combat.DestroyedProps);
        Assert.Equal(CombatOps.Units(before).Count, CombatOps.Units(g).Count);                          // nothing else was touched
    }

    [SkippableFact]
    public void Restoring_an_intact_prop_or_one_not_in_the_save_is_refused_and_damaged_props_come_back_too()
    {
        var (svc, _, path) = NewRig("Combat_ThreeWay.sav");
        var cb = svc.Current!.Combat!;
        Assert.NotEmpty(cb.DestroyedProps);
        var damaged = cb.Destructibles.FirstOrDefault(d => d.Condition == "Damaged");                   // the half-broken crate in 3.1
        Skip.If(damaged == null, "no damaged prop in this save");
        Assert.True(damaged!.NeedsRestore);
        Assert.Contains(damaged.Actor, cb.WreckedProps);
        Assert.DoesNotContain(damaged.Actor, cb.DestroyedProps);

        var pristine = CombatOps.ActorRecords(Gvas(path)).Keys.First(a => CombatOps.IsPristine(Gvas(path), a));
        var before = File.ReadAllBytes(path);
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.RestoreScenery(new[] { pristine })));                    // intact
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.RestoreScenery(new[] { "/Game/Nope.Nope:PersistentLevel.BP_Nope_C_0" })));
        Assert.Equal(before, File.ReadAllBytes(path));

        Act(svc, new SaveAction.RestoreScenery(cb.WreckedProps));                                                          // destroyed and damaged
        var g = Gvas(path);
        Assert.Empty(CombatOps.WreckedActors(g));
        Assert.True(CombatOps.IsPristine(g, damaged.Actor));
        Assert.True(CombatOps.IsPristine(g, pristine));                                                                    // an intact prop was left alone
        var h = CombatOps.PropHealth(g, damaged.Actor)!.Value;
        Assert.Equal(h.Max, h.Health);
    }

    [SkippableFact]
    public void The_pending_store_queues_scenery_restores_into_one_action()
    {
        var (svc, _, _) = NewRig("Combat_Scenery.sav");
        var cb = svc.Current!.Combat!;
        var p = new PendingChanges();
        p.ToggleSceneryRestore(cb.DestroyedProps[0]);
        p.ToggleSceneryRestore(cb.DestroyedProps[0]);                                                    // toggled off again
        Assert.Equal(0, p.ActionCount);
        p.QueueSceneryRestores(cb.DestroyedProps);
        Assert.Equal(4, p.SceneryRestores.Count);
        Assert.Equal(1, p.ActionCount - 3);                                                              // ActionCount counts each prop
        var action = Assert.IsType<SaveAction.RestoreScenery>(Assert.Single(p.BuildCombatActions(cb)));
        Assert.Equal(cb.DestroyedProps.OrderBy(x => x, StringComparer.Ordinal), action.Actors);
        Assert.Contains(p.Describe(svc.Current!), l => l.Text == "Restore 4 destroyed or damaged scenery props");
    }
    [SkippableFact]
    public void Countdown_timer_is_listed_with_its_turns_left_and_what_it_fails_and_can_be_extended()
    {
        var (svc, _, _) = NewRig("Combat_Rescue_before.sav");
        var cb = svc.Current!.Combat!;
        var t = Assert.Single(cb.Timers);
        Assert.Equal("TeamTurnEnd", t.CountsOn);
        Assert.Equal(1, t.Remaining!.Value);
        Assert.Equal(3, t.Total!.Value);
        Assert.Contains("Critical Interact 1", t.Fails);

        svc.Apply(new[] { new FieldChange(t.Remaining.Id, 3) }, force: true);
        var t2 = Assert.Single(svc.Current!.Combat!.Timers);
        Assert.Equal(3, t2.Remaining!.Value);
        Assert.Equal(3, t2.Total!.Value);
    }

    /// <summary>The game's AI-controlled allies (e.g. the Umbaran fighters) are civilian team + friendly + enemy-style behaviour.</summary>
    [SkippableFact]
    public void An_enemy_can_become_a_friendly_ai_unit_even_when_the_save_has_no_civilians()
    {
        var (svc, _, path) = NewRig("Combat_Rescue_after.sav");           // no civilians left in this save
        var cb = svc.Current!.Combat!;
        Assert.DoesNotContain(cb.Units, u => u.Team == "Civilian");
        var e = cb.Units.First(u => u.Team == "Enemy");
        Act(svc, new SaveAction.SetUnitTeam(e.Index, "Civilian"));
        var g = Gvas(path);
        Assert.Equal("Civilian", CombatOps.TeamOf(g, e.Index));
        Assert.True(CombatOps.IsFriendly(g, e.Index));
        Assert.Equal("BitReactor.Team.Civilian", CombatOps.TeamTagOf(g, e.Index));
        Assert.True(CombatOps.BehaviorMatches(g, e.Index, "Civilian"));          // enemy-style behaviour, i.e. AI-controlled
        var tags = CombatOps.UnitTags(g, e.Index)!;
        Assert.Contains("BitReactor.Team.Civilian", tags);
        Assert.DoesNotContain("BitReactor.Team.Enemy", tags);
    }

    /// <summary>
    /// A hero turned enemy lacks what the enemy AI works from (AI area and faction tags, enemy action-point refresh values), so
    /// those are copied from a native enemy. Everything about the hero that is not team or AI setup stays as it was.
    /// </summary>
    [SkippableFact]
    public void An_ally_turned_enemy_gets_the_ai_setup_of_a_native_enemy()
    {
        var (svc, _, path) = NewRig("Combat_Rescue_before.sav");
        var cb = svc.Current!.Combat!;
        var hero = cb.Units.First(u => u.Actor == "Char_Hero_Humanoid_C_0");
        var g0 = Gvas(path);
        var nativeTags = cb.Units.Where(u => u.Team == "Enemy" && !u.Dead).Select(u => CombatOps.UnitTags(g0, u.Index)!).ToList();
        Assert.NotEmpty(nativeTags);
        Assert.DoesNotContain(CombatOps.UnitTags(g0, hero.Index)!, t => t.StartsWith("br.AI.") || t.StartsWith("br.Faction."));

        Act(svc, new SaveAction.SetUnitTeam(hero.Index, "Enemy"));
        var g = Gvas(path);
        var cb2 = svc.Current!.Combat!;
        var tags = CombatOps.UnitTags(g, hero.Index)!;
        var ai = tags.Where(t => t.StartsWith("br.AI.") || t.StartsWith("br.Faction.")).ToList();
        Assert.NotEmpty(ai);
        Assert.Contains(nativeTags, nt => ai.All(nt.Contains));                      // all of them come from one native enemy
        Assert.Contains("BitReactor.Team.Enemy", tags);
        Assert.DoesNotContain("BitReactor.Team.Player", tags);

        // the action-point refresh values now match that kind of enemy, and the hero's own numbers are otherwise untouched
        var u2 = cb2.Units[hero.Index];
        var natives = cb2.Units.Where(u => u.Team == "Enemy" && u.Index != hero.Index && !u.Dead).ToList();
        Assert.Contains(natives, n => n.Attr("RefreshActionPoints")!.Value == u2.Attr("RefreshActionPoints")!.Value
                                       && n.Attr("MovementActionPoints")!.Value == u2.Attr("MovementActionPoints")!.Value
                                       && n.Attr("RefreshMovementActionPoints")!.Value == u2.Attr("RefreshMovementActionPoints")!.Value);
        Assert.Equal(hero.Attr("Health")!.Value, u2.Attr("Health")!.Value);
        Assert.Equal(hero.Attr("Attack")!.Value, u2.Attr("Attack")!.Value);
        Assert.Equal(hero.X!.Value, u2.X!.Value);
        Assert.Equal(hero.Effects.Select(e => e.Def), u2.Effects.Select(e => e.Def));
    }

    [SkippableFact]
    public void Unknown_team_and_duplicate_team_changes_are_refused()
    {
        var (svc, _, path) = NewRig();
        var e = Enemy(svc.Current!.Combat!);
        var before = File.ReadAllBytes(path);
        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetUnitTeam(e.Index, "Sith") }));
        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true, actions: new SaveAction[]
            { new SaveAction.SetUnitTeam(e.Index, "Player"), new SaveAction.SetUnitTeam(e.Index, "Civilian") }));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void Team_change_works_together_with_dead_flag_and_pending_store()
    {
        var (svc, _, _) = NewRig();
        var cb = svc.Current!.Combat!;
        var e = Enemy(cb);
        var p = new PendingChanges();
        p.SetTeam(e.Index, "Player", e.Team);
        p.SetDead(e.Index, true, saved: false);
        Assert.Equal(2, p.Count);
        svc.Apply(p.BuildChanges(), force: true, actions: p.BuildCombatActions(cb));
        var u = svc.Current!.Combat!.Units[e.Index];
        Assert.Equal("Player", u.Team);
        Assert.True(u.Dead);
        p.SetTeam(e.Index, e.Team, e.Team);
        Assert.Equal(1, p.Count);
    }

    [SkippableFact]
    public void Reinforcement_waves_and_definitions_are_listed_and_their_numbers_can_be_edited()
    {
        var (svc, _, _) = NewRig("Combat_Rescue_after.sav");
        var rv = svc.Current!.Combat!.Reinforcements;
        Assert.Equal(3, rv.Waves.Count);
        Assert.All(rv.Waves, w => { Assert.NotNull(w.Round); Assert.NotEmpty(w.Group); Assert.StartsWith("Spawner", CombatOps.SpawnerLabel(w.EntryPoint)); });
        Assert.Equal(7, rv.Configs.Count);
        Assert.NotEmpty(rv.Triggers);
        Assert.True(rv.Groups.Count >= 2);

        var wave = rv.Waves[0]; var cfg = rv.Configs[0];
        svc.Apply(new[]
        {
            new FieldChange(wave.Round!.Id, 9),
            new FieldChange(cfg.Delay!.Id, 5),
            new FieldChange(cfg.TimesToSpawn!.Id, 1),
        }, force: true);
        var rv2 = svc.Current!.Combat!.Reinforcements;
        Assert.Equal(9, rv2.Waves[0].Round!.Value);
        Assert.Equal(rv.Waves[1].Round!.Value, rv2.Waves[1].Round!.Value);          // the other waves are untouched
        Assert.Equal(5, rv2.Configs[0].Delay!.Value);
        Assert.Equal(1, rv2.Configs[0].TimesToSpawn!.Value);
        Assert.Equal(rv.Configs[1].Delay!.Value, rv2.Configs[1].Delay!.Value);
    }

    [SkippableFact]
    public void A_waves_spawn_group_can_be_swapped_for_another_one_the_encounter_uses()
    {
        var (svc, _, path) = NewRig("Combat_Rescue_after.sav");
        var rv = svc.Current!.Combat!.Reinforcements;
        var wave = rv.Waves[0];
        var other = rv.Groups.First(x => x != wave.Group);
        var cfg = rv.Configs.First(c => c.Group != other);

        svc.Apply(Array.Empty<FieldChange>(), force: true, actions: new SaveAction[]
        {
            new SaveAction.SetSpawnGroup("wave", wave.Logic, wave.Index, other),
            new SaveAction.SetSpawnGroup("config", cfg.Logic, cfg.Index, other),
        });
        var g = Gvas(path);
        Assert.Equal(other, CombatOps.SpawnGroupOf(g, "wave", wave.Logic, wave.Index));
        var rv2 = svc.Current!.Combat!.Reinforcements;
        Assert.Equal(other, rv2.Waves[0].Group);
        Assert.Equal(other, rv2.Configs[cfg.Index].Group);
        // both the announcement and the spawn copy of the wave changed, and the other waves did not
        for (int k = 1; k < rv.Waves.Count; k++) Assert.Equal(rv.Waves[k].Group, rv2.Waves[k].Group);
        Assert.Equal(rv.Waves[0].Round!.Value, rv2.Waves[0].Round!.Value);

        // a group the encounter does not use, an unknown kind, or a stale index is refused and writes nothing
        var before = File.ReadAllBytes(path);
        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetSpawnGroup("wave", wave.Logic, wave.Index, "/Game/Nope.Nope") }));
        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetSpawnGroup("wave", wave.Logic, 99, other) }));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void A_reinforcement_trigger_can_be_switched_off_and_on_again()
    {
        var (svc, _, _) = NewRig("Combat_Rescue_after.sav");
        var trig = svc.Current!.Combat!.Reinforcements.Triggers.First(t => t.HasFlag && t.Active);
        Act(svc, new SaveAction.SetTriggerActive(trig.Logic, trig.Name, false));
        var t2 = svc.Current!.Combat!.Reinforcements.Triggers.First(t => t.Logic == trig.Logic);
        Assert.False(t2.Active);
        foreach (var o in svc.Current.Combat.Reinforcements.Triggers.Where(t => t.Logic != trig.Logic))
            Assert.Equal(svc.Current.Combat.Reinforcements.Triggers.First(t => t.Logic == o.Logic).Active, o.Active);

        Assert.Throws<EditException>(() => svc.Apply(Array.Empty<FieldChange>(), force: true,
            actions: new SaveAction[] { new SaveAction.SetTriggerActive(trig.Logic, "BP_Something_Else_C_0", true) }));
        Act(svc, new SaveAction.SetTriggerActive(trig.Logic, trig.Name, true));
        Assert.True(svc.Current!.Combat!.Reinforcements.Triggers.First(t => t.Logic == trig.Logic).Active);
    }

    [SkippableFact]
    public void Objective_counters_such_as_occupy_points_are_listed_and_editable()
    {
        var (svc, _, _) = NewRig("Combat_Rescue_late.sav");                 // counting has started here
        var occ = svc.Current!.Combat!.Objectives.First(o => o.Name.Contains("OccupyRegion"));
        var so = occ.Counters.First(c => c.Label == "Points so far");
        var need = occ.Counters.First(c => c.Label == "Points needed");
        var copy = occ.Counters.First(c => c.Label == "Points needed (progress copy)");
        Assert.Equal(5, so.Ref.Value);
        Assert.Equal(3, need.Ref.Value);
        Assert.Equal(3, copy.Ref.Value);
        svc.Apply(new[] { new FieldChange(so.Ref.Id, 2), new FieldChange(need.Ref.Id, 6), new FieldChange(copy.Ref.Id, 6) }, force: true);
        var occ2 = svc.Current!.Combat!.Objectives.First(o => o.Name.Contains("OccupyRegion"));
        Assert.Equal(2, occ2.Counters.First(c => c.Label == "Points so far").Ref.Value);
        Assert.Equal(6, occ2.Counters.First(c => c.Label == "Points needed").Ref.Value);
        Assert.Equal(6, occ2.Counters.First(c => c.Label == "Points needed (progress copy)").Ref.Value);
    }

    [SkippableFact]
    public void Pending_store_handles_group_and_trigger_changes()
    {
        var (svc, _, _) = NewRig("Combat_Rescue_after.sav");
        var cb = svc.Current!.Combat!; var rv = cb.Reinforcements;
        var p = new PendingChanges();
        var w = rv.Waves[0]; var other = rv.Groups.First(x => x != w.Group);
        p.SetGroup("wave", w.Logic, w.Index, other, w.Group);
        var trig = rv.Triggers.First(t => t.HasFlag);
        p.SetTrigger(trig.Logic, !trig.Active, trig.Active);
        Assert.Equal(2, p.Count);
        Assert.Equal(2, p.BuildCombatActions(cb).Count);
        p.SetGroup("wave", w.Logic, w.Index, w.Group, w.Group);
        p.SetTrigger(trig.Logic, trig.Active, trig.Active);
        Assert.Equal(0, p.Count);
    }

    [SkippableFact]
    public void Transform_parses_as_a_property_list_in_a_combat_save()
    {
        var src = Repo.CombatSave();
        Skip.If(src == null, "no combat sample save");
        var g = new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
        var tr = g.Child(CombatOps.Units(g)[0], "Transform")!;
        Assert.Equal(new[] { "Rotation", "Translation", "Scale3D" }, tr.Children!.Select(c => c.Name));
    }
}
