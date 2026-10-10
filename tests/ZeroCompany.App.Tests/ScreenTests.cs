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
