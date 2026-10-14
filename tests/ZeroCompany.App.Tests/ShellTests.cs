using Avalonia.Controls;
using Avalonia.VisualTree;
using ZeroCompany.App.ViewModels;

namespace ZeroCompany.App.Tests;

public class ShellTests
{

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
