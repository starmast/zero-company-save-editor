using ZeroCompany.App.ViewModels;

namespace ZeroCompany.App.Tests;

public class CombatScreenTests
{
    static TestRig OpenCombat()
    {
        Skip.If(Repo.CombatSave() == null, "no combat sample save");
        var rig = new TestRig(combat: true);
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("combat"));
        return rig;
    }

    [SkippableFact]
    public void Combat_screen_lists_units_by_team_and_edits_queue_changes()
    {
        using var rig = OpenCombat();
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.True(vm.HasCombat);
        Assert.Equal(new[] { "allies", "enemies", "civilians" }, vm.Tabs.Select(t => t.Key).Take(3));
        Assert.StartsWith("Allies 4", vm.Tabs[0].Label);
        Assert.Equal(4, vm.Units.Count);                       // first tab is Allies
        Assert.True(vm.ShowAllyActions);
        rig.Shot("20-combat");

        Headless.Run(() => vm.Tabs.First(t => t.Key == "enemies").Command.Execute(null));
        Assert.Equal("enemies", vm.ActiveTab);
        Assert.True(vm.Tabs.First(t => t.Key == "enemies").IsActive);
        Assert.False(vm.ShowAllyActions); Assert.True(vm.ShowEnemyActions);
        Assert.All(vm.Units, u => Assert.Equal("Enemy", u.Team));
        var enemy = vm.Units[0];
        var pending = rig.Main.Session.Pending;
        Headless.Run(() => enemy.Health!.SetTo(1));
        Assert.True(enemy.Health!.IsChanged);
        Assert.Equal(1, pending.Count);

        Headless.Run(() => enemy.Dead.Command.Execute(null));
        Assert.True(enemy.Dead.IsOn);
        Assert.Equal(2, pending.Count);

        var eff = enemy.Effects[0];
        Headless.Run(() => eff.Remove.Command.Execute(null));
        Assert.True(eff.Remove.IsOn);
        Assert.Equal(3, pending.Count);

        Headless.Run(() => { enemy.SelectedChoice = enemy.Choices[0]; enemy.AddEffect.Execute(null); });
        Assert.Single(enemy.QueuedCopies);
        Assert.Equal(4, pending.Count);
        Assert.Equal(4, rig.Main.PendingBar.Lines.Count);
        rig.Shot("21-combat-pending");

        Headless.Run(() => enemy.QueuedCopies[0].Undo.Execute(null));
        Assert.Empty(enemy.QueuedCopies);
        Assert.Equal(3, pending.Count);
    }

    [SkippableFact]
    public void Objectives_tab_lists_objectives_and_queues_a_status_change()
    {
        using var rig = OpenCombat();
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.Contains(vm.Tabs, t => t.Key == "objectives");
        Headless.Run(() => vm.Tabs.First(t => t.Key == "objectives").Command.Execute(null));
        Assert.True(vm.ShowObjectives); Assert.False(vm.ShowUnits);
        Assert.Contains(vm.Objectives, o => o.Title.StartsWith("Critical Interact"));
        rig.Shot("22-combat-objectives");

        var row = vm.Objectives.First(o => o.CanEdit && o.StatusText == "Active");
        Headless.Run(() => row.Toggle.Command.Execute(null));
        Assert.Equal("Succeeded", row.StatusText);
        Assert.True(row.IsDone);
        Assert.Equal(1, rig.Main.Session.Pending.Count);
        Assert.Contains(rig.Main.PendingBar.Lines, l => l.Label.Contains("Succeeded"));
        rig.Shot("23-combat-objective-queued");
        Headless.Run(() => row.Toggle.Command.Execute(null));
        Assert.Equal("Active", row.StatusText);
        Assert.Equal(0, rig.Main.Session.Pending.Count);

        // the chosen tab is remembered when the screen is rebuilt
        Headless.Run(() => rig.Main.Navigate("combat"));
        Assert.Equal("objectives", Assert.IsType<CombatViewModel>(rig.Main.Screen).ActiveTab);
    }

    [SkippableFact]
    public void Team_dropdown_queues_a_team_change_and_applies_it()
    {
        using var rig = OpenCombat();
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Headless.Run(() => vm.Tabs.First(t => t.Key == "enemies").Command.Execute(null));
        var card = vm.Units[0];
        Assert.Equal("Enemy", card.SelectedTeam);
        Assert.False(card.TeamChanged);
        Headless.Run(() => card.SelectedTeam = "Ally");
        Assert.True(card.TeamChanged);
        Assert.Equal("Ally", card.SelectedTeam);
        Assert.Contains(rig.Main.PendingBar.Lines, l => l.Label.Contains("Team:"));
        rig.Shot("24-combat-team");
        Headless.Run(() => card.SelectedTeam = "Enemy");                       // back to the saved team: no change
        Assert.False(card.TeamChanged);
        Assert.Equal(0, rig.Main.Session.Pending.Count);

        Headless.Run(() => card.SelectedTeam = "Ally");
        rig.Main.Session.Apply(asCopy: false, force: true);
        Headless.Run(() => rig.Main.ReloadScreen());
        var vm2 = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.StartsWith("Allies 5", vm2.Tabs.First(t => t.Key == "allies").Label);
        Assert.StartsWith("Enemies 6", vm2.Tabs.First(t => t.Key == "enemies").Label);
    }

    [SkippableFact]
    public void Rescue_mission_shows_the_countdown_and_can_recruit_the_civilian()
    {
        Skip.If(Repo.CombatSaveNamed("Combat_Rescue_before.sav") == null, "no rescue sample");
        using var rig = new TestRig(combat: true, combatFile: "Combat_Rescue_before.sav");
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("combat"));
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);

        Headless.Run(() => vm.Tabs.First(t => t.Key == "objectives").Command.Execute(null));
        Assert.True(vm.ShowTimers);
        var timer = Assert.Single(vm.Timers);
        Assert.Equal(1, timer.Remaining!.Value);
        Assert.Contains("Critical Interact 1", timer.Detail);
        rig.Shot("25-combat-timer");
        Headless.Run(() => timer.Remaining.SetTo(3));
        Assert.Equal(1, rig.Main.Session.Pending.Count);

        Headless.Run(() => vm.Tabs.First(t => t.Key == "civilians").Command.Execute(null));
        var baker = Assert.Single(vm.Units);
        Assert.Equal("Civilian", baker.Team);
        Headless.Run(() => baker.SelectedTeam = "Ally");
        Assert.Equal(2, rig.Main.Session.Pending.Count);

        rig.Main.Session.Apply(asCopy: false, force: true);
        Headless.Run(() => rig.Main.ReloadScreen());
        var vm2 = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.StartsWith("Allies 5", vm2.Tabs.First(t => t.Key == "allies").Label);
        Assert.DoesNotContain(vm2.Tabs, t => t.Key == "civilians");
        Headless.Run(() => vm2.Tabs.First(t => t.Key == "objectives").Command.Execute(null));
        Assert.Equal(3, Assert.Single(vm2.Timers).Remaining!.Value);
    }

    [SkippableFact]
    public void Switching_a_player_class_unit_to_the_ai_shows_a_warning_but_enemies_do_not()
    {
        using var rig = OpenCombat();
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        var hero = vm.Units[0];                                               // first tab is Allies
        Assert.False(hero.TeamWarning);
        Headless.Run(() => hero.SelectedTeam = "Friendly AI");
        Assert.True(hero.TeamWarning);
        Headless.Run(() => hero.SelectedTeam = "Enemy");
        Assert.True(hero.TeamWarning);
        Headless.Run(() => hero.SelectedTeam = "Ally");
        Assert.False(hero.TeamWarning);

        Headless.Run(() => vm.Tabs.First(t => t.Key == "enemies").Command.Execute(null));
        var enemy = vm.Units[0];
        Headless.Run(() => enemy.SelectedTeam = "Friendly AI");
        Assert.True(enemy.TeamChanged);
        Assert.False(enemy.TeamWarning);
    }

    [SkippableFact]
    public void Reinforcements_tab_edits_waves_definitions_and_triggers_and_applies_them()
    {
        Skip.If(Repo.CombatSaveNamed("Combat_Rescue_after.sav") == null, "no rescue sample");
        using var rig = new TestRig(combat: true, combatFile: "Combat_Rescue_after.sav");
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("combat"));
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.Contains(vm.Tabs, t => t.Key == "reinforcements");
        Headless.Run(() => vm.Tabs.First(t => t.Key == "reinforcements").Command.Execute(null));
        Assert.True(vm.ShowReinforcements); Assert.False(vm.ShowUnits); Assert.False(vm.ShowObjectives);
        Assert.Equal(3, vm.Waves.Count); Assert.Equal(7, vm.Configs.Count); Assert.True(vm.HasTriggers);
        rig.Shot("26-combat-reinforcements");

        var pending = rig.Main.Session.Pending;
        var wave = vm.Waves[0];
        Headless.Run(() => wave.Round!.SetTo(8));
        Assert.Equal(1, pending.Count);
        Headless.Run(() => wave.Cancel.Command.Execute(null));                  // cancel replaces the typed round
        Assert.True(wave.Cancel.IsOn); Assert.Equal(WaveRowViewModel.CancelRound, wave.Round!.Value);
        Headless.Run(() => wave.Cancel.Command.Execute(null));                  // undo goes back to the saved round
        Assert.False(wave.Round.IsChanged); Assert.Equal(0, pending.Count);

        var other = wave.Group.Choices.First(c => c != wave.Group.Selected);
        Headless.Run(() => wave.Group.Selected = other);
        Assert.True(wave.Group.IsChanged); Assert.Equal(1, pending.Count);
        var cfg = vm.Configs[0];
        Headless.Run(() => cfg.Delay!.SetTo(4));
        var trig = vm.Triggers.First(t => t.HasFlag && t.StatusText == "On");
        Headless.Run(() => trig.Toggle.Command.Execute(null));
        Assert.Equal("Off", trig.StatusText);
        Assert.Equal(3, pending.Count);
        Assert.Contains(rig.Main.PendingBar.Lines, l => l.Label.Contains("Reinforcement"));
        rig.Shot("27-combat-reinforcements-queued");

        rig.Main.Session.Apply(asCopy: false, force: true);
        Headless.Run(() => rig.Main.ReloadScreen());
        var vm2 = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.Equal("reinforcements", vm2.ActiveTab);
        Assert.Equal(other.Value, vm2.Waves[0].Group.Selected!.Value);
        Assert.Equal(4, vm2.Configs[0].Delay!.Value);
        Assert.Equal("Off", vm2.Triggers.First(t => t.Title == trig.Title).StatusText);
    }

    [SkippableFact]
    public void Occupy_region_points_show_as_editable_counters_on_the_objectives_tab()
    {
        Skip.If(Repo.CombatSaveNamed("Combat_Rescue_late.sav") == null, "no late rescue sample");
        using var rig = new TestRig(combat: true, combatFile: "Combat_Rescue_late.sav");
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("combat"));
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Headless.Run(() => vm.Tabs.First(t => t.Key == "objectives").Command.Execute(null));
        var occ = vm.Objectives.First(o => o.Title.StartsWith("Occupy Region"));
        Assert.True(occ.HasCounters);
        var so = occ.Counters.First(c => c.Label == "Points so far");
        Assert.Equal(5, so.Field.Value);
        Headless.Run(() => so.Field.SetTo(1));
        Assert.True(so.Field.IsChanged);
        Assert.Equal(1, rig.Main.Session.Pending.Count);
        rig.Shot("28-combat-objective-counters");
    }

    [SkippableFact]
    public void Quick_action_sets_every_enemy_to_one_health_and_applies_to_the_save()
    {
        using var rig = OpenCombat();
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Headless.Run(() => vm.EnemiesTo1Hp.Execute(null));
        Assert.Equal(7, rig.Main.Session.Pending.Count);

        var res = rig.Main.Session.Apply(asCopy: false, force: true);
        Assert.Equal(7, res.Count);
        Headless.Run(() => rig.Main.ReloadScreen());
        var vm2 = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Headless.Run(() => vm2.Tabs.First(t => t.Key == "enemies").Command.Execute(null));
        Assert.All(vm2.Units, u => Assert.Equal(1, u.Health!.Value));
        Headless.Run(() => vm2.Tabs.First(t => t.Key == "allies").Command.Execute(null));
        Assert.All(vm2.Units, u => Assert.True(u.Health!.Value > 1));
        Assert.Equal(0, rig.Main.Session.Pending.Count);
    }

    [SkippableFact]
    public void Hub_save_shows_the_not_a_combat_save_message()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("combat"));
        var vm = Assert.IsType<CombatViewModel>(rig.Main.Screen);
        Assert.True(vm.NoCombat);
        Assert.Empty(vm.Tabs);
    }
}
