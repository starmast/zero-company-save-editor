using ZeroCompany.App.ViewModels;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.Tests;

public class ScreenTests
{
    static TestRig Open(string screen, params string[] args)
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        var rig = new TestRig();
        rig.Start();
        Headless.Run(() => rig.Main.Navigate(screen, args));
        return rig;
    }

    [SkippableFact]
    public void Armory_lists_items_by_tab_and_counts_edit_the_pending_set()
    {
        using var rig = Open("armory");
        var vm = Assert.IsType<ArmoryViewModel>(rig.Main.Screen);
        Assert.NotEmpty(vm.Tabs);
        Assert.NotEmpty(vm.Visible);
        rig.Shot("10-armory");
        var card = vm.Visible[0];
        Headless.Run(() => card.Count.Step(1));
        Assert.Equal(1, rig.Main.Session.Pending.Count);
        Headless.Run(() => vm.Filter = "zzzz-no-such-item");
        Assert.True(vm.IsEmpty);
        Headless.Run(() => vm.Filter = "");
        Assert.False(vm.IsEmpty);
    }

    [SkippableFact]
    public void Armory_add_items_tab_lists_the_games_items_and_queues_new_copies()
    {
        using var rig = Open("armory");
        var vm = Assert.IsType<ArmoryViewModel>(rig.Main.Screen);
        var tab = Assert.Single(vm.Tabs, t => t.Key == "Add");
        Headless.Run(() => tab.Command.Execute(null));
        Assert.True(vm.ShowAdd);
        Assert.False(vm.ShowOwned);
        Skip.IfNot(vm.HasGameData, "no game data in this test setup");
        Assert.NotEmpty(vm.VisibleAdd);
        var card = vm.VisibleAdd.First(c => c.NeverOwned);

        Headless.Run(() => card.Plus.Execute(null));
        Headless.Run(() => card.Plus.Execute(null));
        Assert.Equal(2, card.Queued);
        Assert.Equal(1, rig.Main.Session.Pending.Count);                 // one action: two copies of one item
        Headless.Run(() => card.Minus.Execute(null));
        Headless.Run(() => card.Minus.Execute(null));
        Headless.Run(() => card.Minus.Execute(null));                   // never below zero
        Assert.Equal(0, card.Queued);
        Assert.Equal(0, rig.Main.Session.Pending.Count);

        Headless.Run(() => vm.Filter = "zzzz-no-such-item");
        Assert.True(vm.IsEmpty);
        Headless.Run(() => vm.Filter = "");
        Assert.False(vm.IsEmpty);
    }

    [SkippableFact]
    public void Armory_add_items_filters_sort_and_clear()
    {
        using var rig = Open("armory");
        var vm = Assert.IsType<ArmoryViewModel>(rig.Main.Screen);
        Headless.Run(() => Assert.Single(vm.Tabs, t => t.Key == "Add").Command.Execute(null));
        Skip.IfNot(vm.HasGameData, "no game data in this test setup");
        int total = vm.VisibleAdd.Count;
        rig.Shot("10b-armory-add-items");
        Assert.True(vm.ShowAddTools);
        Assert.StartsWith($"Showing {total} of {total}", vm.AddSummary);

        Headless.Run(() => vm.NeverOwnedOnly = true);
        Assert.NotEmpty(vm.VisibleAdd);
        Assert.All(vm.VisibleAdd, c => Assert.True(c.NeverOwned));
        Headless.Run(() => vm.NeverOwnedOnly = false);

        var mods = vm.GroupChoices.FirstOrDefault(g => g.Value == "Mods");
        Skip.If(mods == null, "the game data has no weapon mods");
        Headless.Run(() => vm.SelGroup = mods!);
        Assert.NotEmpty(vm.VisibleAdd);
        Assert.All(vm.VisibleAdd, c => Assert.Equal("Modification", c.Kind));

        Headless.Run(() => vm.SelSort = vm.SortChoices.First(s => s.Value == "TierHigh"));
        var tiers = vm.VisibleAdd.Select(c => c.Item.Tier ?? 0).ToList();
        Assert.Equal(tiers.OrderByDescending(t => t).ToList(), tiers);

        var card = vm.VisibleAdd[0];
        Headless.Run(() => card.Plus.Execute(null));
        Headless.Run(() => vm.QueuedOnly = true);
        Assert.Equal(new[] { card }, vm.VisibleAdd);                         // only the queued item
        Assert.Contains("1 copy queued", vm.AddSummary);
        Headless.Run(() => card.Minus.Execute(null));
        Assert.Empty(vm.VisibleAdd);                                         // un-queuing drops it from "queued only"

        Headless.Run(() => vm.ClearFilters.Execute(null));
        Assert.Equal(total, vm.VisibleAdd.Count);
        Assert.False(vm.QueuedOnly);
        Assert.Equal("All", vm.SelGroup.Value);
        Assert.Equal("Name", vm.SelSort.Value);
    }

    [SkippableFact]
    public void Armory_add_items_can_show_the_choice_rewards_of_missions_like_the_meditations()
    {
        using var rig = Open("armory");
        var vm = Assert.IsType<ArmoryViewModel>(rig.Main.Screen);
        Headless.Run(() => Assert.Single(vm.Tabs, t => t.Key == "Add").Command.Execute(null));
        var choices = vm.GroupChoices.FirstOrDefault(g => g.Value == "Choices");
        Skip.If(choices == null, "no mission rewards in this test's game data (make gamedata/raw_rewards.json with `zcassets rewards --json`)");

        Headless.Run(() => vm.SelGroup = choices!);
        Assert.NotEmpty(vm.VisibleAdd);
        Assert.All(vm.VisibleAdd, c => Assert.True(c.HasSource));
        Headless.Run(() => vm.Filter = "meditat");
        var names = vm.VisibleAdd.Select(c => c.Name).ToList();
        Assert.Contains(vm.VisibleAdd, c => c.SourceText.Contains("Jedi Meditation I:"));
        Assert.Contains(vm.VisibleAdd, c => c.SourceText.Contains("Jedi Meditation II:"));
        Assert.True(vm.VisibleAdd.Count >= 8, "both meditations offer two choices of two items each");
        Headless.Run(() => vm.NeverOwnedOnly = true);
        Assert.All(vm.VisibleAdd, c => Assert.True(c.NeverOwned));
        rig.Shot("10c-armory-meditations");
    }

    [SkippableFact]
    public void Medbay_renders_slots_and_costs()
    {
        using var rig = Open("medbay");
        var vm = Assert.IsType<MedbayViewModel>(rig.Main.Screen);
        Assert.NotEmpty(vm.Slots);
        Assert.StartsWith("1 bed", vm.BedCost);
        rig.Shot("11-medbay");
    }

    [SkippableFact]
    public void Galaxy_regions_edit_and_coil_toggles_queue_actions()
    {
        using var rig = Open("galaxy");
        var vm = Assert.IsType<GalaxyViewModel>(rig.Main.Screen);
        Assert.NotEmpty(vm.Regions);
        var region = vm.Regions.First(r => r.Influence != null);
        Headless.Run(() => region.Influence!.Step(5));
        Assert.True(region.Influence!.IsChanged);
        if (vm.Coil.Count > 0)
        {
            var row = vm.Coil[0];
            Assert.True(row.Remove.IsOff);
            Headless.Run(() => row.Remove.Command.Execute(null));
            Assert.True(row.Remove.IsOn);
            Assert.Equal("Available", rig.Main.Session.Pending.CoilChanges[row.Id]);
            Headless.Run(() => row.Prevent.Command.Execute(null));          // switching to the other choice replaces the first
            Assert.True(row.Prevent.IsOn); Assert.True(row.Remove.IsOff);
            Headless.Run(() => vm.RemoveAll.Command.Execute(null));
            Assert.True(vm.RemoveAll.IsOn);
        }
        rig.Shot("12-galaxy");
    }

    [SkippableFact]
    public void Galaxy_operations_show_their_expiry_and_can_be_given_more_time()
    {
        using var rig = Open("galaxy");
        var vm = Assert.IsType<GalaxyViewModel>(rig.Main.Screen);
        Headless.Run(() => vm.Tabs.First(t => t.Key == "operations").Command.Execute(null));
        Assert.True(vm.ShowOperations);
        var row = vm.Operations.FirstOrDefault(o => o.CanEdit && o.Age > 0);
        Skip.If(row == null, "no operation in the sample save has used up a cycle");
        Assert.Contains("Expires in", row!.Expires);
        int was = row.Age;
        Headless.Run(() => row.Longer.Execute(null));
        Assert.Equal(was - 1, row.Age);
        Assert.True(row.IsChanged);
        Assert.Single(rig.Main.Session.Pending.OperationAgeChanges);
        Headless.Run(() => row.Reset.Execute(null));
        Assert.Equal(0, row.Age);
        rig.Shot("24-galaxy-operations");
        Headless.Run(() => row.Shorter.Execute(null));
        Assert.Equal(1, row.Age);
        Assert.DoesNotContain("unknown", row.Expires);
    }

    [SkippableFact]
    public void Galaxy_lists_the_dens_missions_and_a_new_enemy_set_is_queued_and_described()
    {
        using var rig = Open("galaxy");
        var vm = Assert.IsType<GalaxyViewModel>(rig.Main.Screen);
        Assert.Equal(new[] { "influence", "operations", "missions", "coil" }, vm.Tabs.Select(t => t.Key));
        Assert.True(vm.ShowInfluence); Assert.False(vm.ShowMissions);
        Headless.Run(() => vm.Tabs.First(t => t.Key == "missions").Command.Execute(null));
        Assert.True(vm.ShowMissions); Assert.False(vm.ShowInfluence); Assert.False(vm.ShowCoil);
        var row = vm.Missions.FirstOrDefault(m => m.CanEdit);
        Skip.If(row == null, "the sample save offers no editable mission");
        Assert.False(row!.IsChanged);
        if (rig.Main.Session.Current!.Model.Db.Missions.Count > 0)
        {
            Assert.DoesNotContain("SK ", row.Title);                                  // a real name, not the asset name
            Assert.True(row.HasSummary);
            Assert.Contains("normally vs", row.Detail);
        }
        var other = row.Choices.First(c => c.Label.EndsWith("(other faction)"));
        Headless.Run(() => row.Selected = other);
        Assert.True(row.IsChanged);
        var pending = rig.Main.Session.Pending;
        Assert.Single(pending.MissionSetChanges);
        Assert.Contains(rig.Main.Session.Current!.View.Galaxy.Missions.Where(m => m.CanEdit), m => pending.MissionSetChanges.ContainsKey(m.Id));
        Assert.Contains(pending.Describe(rig.Main.Session.Current!), l => l.Text.Contains("enemies"));
        rig.Shot("23-galaxy-missions");
        Headless.Run(() => row.Selected = row.Choices[0]);                 // back to the saved set clears the change
        Assert.Empty(pending.MissionSetChanges);
    }

    [SkippableFact]
    public void Discard_clears_everything_and_the_screen_follows()
    {
        using var rig = Open("galaxy");
        var vm = (GalaxyViewModel)rig.Main.Screen!;
        var region = vm.Regions.First(r => r.Influence != null);
        Headless.Run(() => region.Influence!.Step(5));
        Assert.Equal(1, rig.Main.PendingBar.Count);
        Headless.Run(() => rig.Main.PendingBar.DiscardCommand.Execute(null));
        Assert.Equal(0, rig.Main.PendingBar.Count);
        var fresh = (GalaxyViewModel)rig.Main.Screen!;
        Assert.False(fresh.Regions.First(r => r.Influence != null).Influence!.IsChanged);
    }

    [SkippableFact]
    public void Upgrades_timeline_selects_nodes_and_queues_starts()
    {
        using var rig = Open("upgrades");
        var vm = Assert.IsType<UpgradesViewModel>(rig.Main.Screen);
        Assert.Equal(3, vm.Tabs.Count);
        Assert.NotEmpty(vm.Rows);
        Assert.NotNull(vm.Selected);
        rig.Shot("13-upgrades");
        // find a startable node in any tab
        UpgradeNodeViewModel? open = null;
        foreach (var t in new[] { "Facilities", "Crew", "Weapons" })
        {
            Headless.Run(() => rig.Main.Navigate("upgrades", t));
            vm = (UpgradesViewModel)rig.Main.Screen!;
            open = vm.Rows.SelectMany(r => r.Cells).SelectMany(c => c.Nodes).FirstOrDefault(n => n.IsOpen);
            if (open != null) break;
        }
        Skip.If(open == null, "no startable upgrade in the sample save");
        Headless.Run(() => open!.SelectCommand.Execute(null));
        Assert.True(open!.IsSelected);
        Assert.NotNull(vm.StartToggle);
        Headless.Run(() => vm.StartToggle!.Command.Execute(null));
        Assert.True(open.IsQueued);
        Assert.Equal(1, rig.Main.PendingBar.Count);
        var actions = rig.Main.Session.Pending.BuildActions(rig.Main.Session.Current!.Upgrades);
        Assert.IsType<SaveAction.StartUpgrade>(Assert.Single(actions));
        Headless.Run(() => vm.AlsoExpedite = true);
        Assert.IsType<SaveAction.StartExpediteUpgrade>(Assert.Single(rig.Main.Session.Pending.BuildActions(rig.Main.Session.Current!.Upgrades)));
        rig.Shot("14-upgrades-queued");
        // applying really starts it
        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        var after = rig.Main.Session.Current!.Upgrades.Items.First(r => r.Name == open.Name);
        Assert.Equal("InProgress", after.Status);
    }

    [SkippableFact]
    public void Advanced_lists_fields_filters_and_loads_the_raw_tree_lazily()
    {
        using var rig = Open("advanced");
        var vm = Assert.IsType<AdvancedViewModel>(rig.Main.Screen);
        Assert.Contains(vm.Tabs, t => t.Key == "Raw");
        Assert.NotEmpty(vm.Sections);
        rig.Shot("15-advanced");
        var first = vm.Sections[0].Rows[0];
        Headless.Run(() => first.Field.Step(1));
        Assert.Equal(1, rig.Main.PendingBar.Count);
        Headless.Run(() => vm.Filter = "zzzz-nothing");
        Assert.True(vm.NothingMatches);
        Headless.Run(() => vm.Filter = "");
        // raw tree: roots, then expanding loads children
        Headless.Run(() => rig.Main.Navigate("advanced", "Raw"));
        var raw = (AdvancedViewModel)rig.Main.Screen!;
        Assert.True(raw.IsRaw);
        var root = Assert.IsType<RawNodeViewModel>(raw.RawRoots[0]);
        Assert.True(root.Expandable);
        Assert.Equal("…", root.Children[0].Name);                      // placeholder until opened
        Headless.Run(() => root.IsExpanded = true);
        Assert.NotEqual("…", root.Children[0].Name);
        rig.Shot("16-advanced-raw");
    }

    [SkippableFact]
    public void Without_game_data_upgrade_tiers_spread_across_the_timeline_and_a_hint_is_shown()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig(withGameData: false);
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("upgrades"));
        var vm = (UpgradesViewModel)rig.Main.Screen!;
        Assert.True(vm.NeedsGameData);
        var row = vm.Rows.First(r => r.Cells.SelectMany(c => c.Nodes).Count() > 1);
        var columns = row.Cells.Where(c => c.Nodes.Count > 0).Select(c => c.Den).ToList();
        Assert.True(columns.Count > 1, "tiers of one line must not all sit in the same column");
        rig.Shot("17-upgrades-nodata");
    }

    [SkippableFact]
    public void Upgrade_timeline_fits_a_wide_window_without_stretching()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig(withGameData: false);
        rig.Start(width: 1980, height: 900);
        Headless.Run(() => rig.Main.Navigate("upgrades"));
        rig.Shot("18-upgrades-wide");
    }
}
