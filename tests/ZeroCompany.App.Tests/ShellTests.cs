using Avalonia.Controls;
using Avalonia.VisualTree;
using ZeroCompany.App.ViewModels;

namespace ZeroCompany.App.Tests;

public class ShellTests
{
    [SkippableFact]
    public void An_error_banner_offers_a_debug_report_and_a_bundle_with_the_save()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start();
        bool shown = Headless.Run(() =>
        {
            try { throw new InvalidOperationException("the save has no utility item to copy"); }
            catch (Exception e) { rig.Main.ReportUnexpected("Applying failed", e); }
            rig.Pump();
            return rig.Window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Copy debug report" && b.IsEffectivelyVisible)
                && rig.Window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Save debug bundle…" && b.IsEffectivelyVisible);
        });
        Assert.True(shown);
        rig.Shot("debug-report-banner");

        Headless.RunAsync(() => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)rig.Main.CopyDebugReportCommand).ExecuteAsync(null)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        var text = rig.Ui.Clipboard!;
        Assert.Contains("Applying failed: the save has no utility item to copy", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("ZeroCompanyEditor", text);
        Assert.Contains(rig.SaveName, text);

        rig.Ui.PickedSaveFile = Path.Combine(rig.Tmp, "bundle.zip");
        Headless.RunAsync(() => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)rig.Main.SaveDebugBundleCommand).ExecuteAsync(null)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        using var zip = System.IO.Compression.ZipFile.OpenRead(rig.Ui.PickedSaveFile);
        Assert.Contains(zip.Entries, e => e.FullName == rig.SaveName);
        Assert.Contains(zip.Entries, e => e.FullName == "crash.log");
    }


    [SkippableFact]
    public void Saves_screen_lists_the_sample_save()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start(openSave: false);
        var saves = Assert.IsType<SavesViewModel>(rig.Main.Screen);
        var card = Assert.Single(saves.Groups.Single().Cards);
        Assert.Equal(rig.SaveName, card.FileName);
        Assert.True(card.HasThumbnail);
        rig.Shot("01-saves");
    }

    [SkippableFact]
    public void Opening_a_save_shows_the_command_screen_with_the_header_chips()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start();
        Assert.IsType<CommandViewModel>(rig.Main.Screen);
        Assert.True(rig.Main.HasSave);
        Assert.Contains(rig.Main.Header!.Chips, c => c.Label == "Credits");
        rig.Shot("02-command");
    }

    [SkippableFact]
    public void The_profile_opens_on_its_own_screen_with_only_Profile_and_Advanced()
    {
        Skip.If(Repo.CombatSaveNamed("Profile.sav") == null, "no profile sample");
        using var rig = new TestRig(combatFile: "Profile.sav");
        rig.Start(height: 2400);
        var vm = Assert.IsType<ProfileViewModel>(rig.Main.Screen);
        Assert.Equal(new[] { "profile", "advanced" }, rig.Main.Nav.Where(n => n.IsVisible).Select(n => n.Key));
        Headless.Run(() => rig.Main.Navigate("command"));
        Assert.IsType<ProfileViewModel>(rig.Main.Screen);                         // campaign screens are not offered for the profile
        vm = (ProfileViewModel)rig.Main.Screen!;
        var bargain = vm.Achievements.Single(a => a.Name == "There Will Be No Bargain");
        Assert.False(bargain.Reached);
        Headless.Run(() => { bargain.Count!.SetTo(3); rig.Pump(); });
        Assert.True(bargain.Reached);
        Assert.Equal(1, rig.Session.Pending.Count);

        var beeps = vm.Achievements.Single(a => a.Name == "Angry Beeps");                 // not counted in the sample profile
        Assert.True(beeps.CanStart);
        Headless.Run(() => { beeps.StartText = "x"; rig.Pump(); });
        Assert.True(beeps.IsInvalid); Assert.False(beeps.IsQueued);
        Headless.Run(() => { beeps.StartText = "1"; rig.Pump(); });
        Assert.True(beeps.IsQueued);
        Assert.Equal(1, rig.Session.Pending.ProfileCounterAdds["AngryBeeps"]);
        Assert.Contains("starts at 1", beeps.Progress);
        Assert.Equal(2, rig.Session.Pending.Count);
        rig.Shot("profile");
        Headless.Run(() => { rig.Session.Pending.Discard(); rig.Pump(); });              // Discard empties the box
        Assert.Equal("", beeps.StartText);
        Assert.False(beeps.IsQueued);
        Headless.Run(() => rig.Main.Navigate("advanced"));
        Assert.IsType<AdvancedViewModel>(rig.Main.Screen);
    }

    [SkippableFact]
    public void Campaign_options_show_for_a_1_2_campaign_only_and_a_choice_is_queued()
    {
        Skip.If(Repo.SampleSave() == null || Repo.CombatSaveNamed("Hub_1_2.sav") == null, "no sample saves");
        using (var old = new TestRig())
        {
            old.Start();
            var cmd = Assert.IsType<CommandViewModel>(old.Main.Screen);
            Assert.False(cmd.HasCampaignOptions);
        }
        using var rig = new TestRig(combatFile: "Hub_1_2.sav");
        rig.Start(height: 2300);
        var vm = Assert.IsType<CommandViewModel>(rig.Main.Screen);
        Assert.True(vm.HasCampaignOptions);
        Assert.Equal(15, vm.CampaignOptions.Count);
        Assert.Contains("Permadeath on", vm.Line2);
        var crits = vm.CampaignOptions.Single(o => o.Label == "Enemies can critically hit");
        Assert.Equal("Off", crits.Selected?.Label);
        Headless.Run(() => { crits.Selected = crits.Choices.Single(c => c.Label == "On"); rig.Pump(); });
        Assert.Equal("Facts.CampaignOptions.Tactical.EnemiesCanCrit.On", rig.Session.Pending.CampaignOptions["US_Gameplay_EnemyCrits"]);
        Headless.Run(() => { crits.Selected = crits.Choices.Single(c => c.Label == "Off"); rig.Pump(); });
        Assert.Empty(rig.Session.Pending.CampaignOptions);                      // back at what the save has: nothing to write
        rig.Shot("02-command-campaign-options");
    }

    [SkippableFact]
    public void Nothing_can_be_edited_while_a_write_is_running_and_a_copy_keeps_the_edits()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var gate = new ManualResetEventSlim();
        using var parked = new ManualResetEventSlim();
        int ui = Headless.Run(() => Environment.CurrentManagedThreadId);
        bool armed = false;
        // the writer asks whether the game is running from its worker thread: hold it there until the screen has been checked
        using var rig = new TestRig(gameRunning: () =>
        {
            if (Volatile.Read(ref armed) && Environment.CurrentManagedThreadId != ui) { parked.Set(); gate.Wait(TimeSpan.FromSeconds(20)); }
            return null;
        });
        rig.Start();
        var credits = rig.Main.Header!.Chips.First(c => c.Label == "Credits");
        Headless.Run(() => { credits.Field.Text = ((long)credits.Field.Value + 7).ToString(); });

        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: true, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        Assert.Equal(1, rig.Main.Session.Pending.Count);                     // a copy leaves the open save, and the edit, as they were

        Volatile.Write(ref armed, true);
        bool busy = false, screenEnabled = true, discardEnabled = true;
        Headless.RunAsync(async () =>
        {
            var apply = rig.Main.ApplyAsync(copy: false, force: false);
            while (!parked.IsSet) await Task.Delay(10);
            rig.Pump();
            busy = rig.Main.PendingBar.Busy;
            screenEnabled = rig.Window.GetVisualDescendants().OfType<ContentControl>().First(c => ReferenceEquals(c.Content, rig.Main.Screen)).IsEffectivelyEnabled;
            discardEnabled = rig.Window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Discard").IsEffectivelyEnabled;
            gate.Set();
            await apply;
        }).GetAwaiter().GetResult();
        Assert.True(busy);
        Assert.False(screenEnabled);
        Assert.False(discardEnabled);
        Assert.Equal("ok", rig.Main.BannerKind);
        Assert.Equal(0, rig.Main.Session.Pending.Count);
        Assert.False(rig.Main.PendingBar.Busy);
    }

    [SkippableFact]
    public void Editing_a_chip_shows_pending_changes_and_apply_writes_them()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start();
        var credits = rig.Main.Header!.Chips.First(c => c.Label == "Credits");
        var before = credits.Field.Value;
        Headless.Run(() => { credits.Field.Text = ((long)before + 500).ToString(); });
        Assert.Equal(1, rig.Main.PendingBar.Count);
        Assert.True(credits.IsChanged);
        rig.Shot("03-pending");
        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal(0, rig.Main.PendingBar.Count);
        Assert.Equal(before + 500, rig.Main.Header!.Chips.First(c => c.Label == "Credits").Field.Value);
        Assert.Equal("ok", rig.Main.BannerKind);
    }

    [SkippableFact]
    public void Game_data_screen_renders()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("gamedata"));
        Assert.IsType<GameDataViewModel>(rig.Main.Screen);
        rig.Shot("04-gamedata");
    }
}
