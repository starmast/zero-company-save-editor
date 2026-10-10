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
