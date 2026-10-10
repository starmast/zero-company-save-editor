using ZeroCompany.App.ViewModels;
using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.App.Tests;

public class GameDataScreenTests
{
    [SkippableFact]
    public void Extract_button_flow_builds_the_database_and_reloads_the_open_save()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        var game = Environment.GetEnvironmentVariable("ZC_GAME_DIR") ?? GameExtractor.DefaultGameDir;
        var dirs = new[] { "gamedata", Path.Combine("tools", "extract") }.Select(d => Path.Combine(Repo.Root, d)).Where(Directory.Exists).ToList();
        var usmap = Environment.GetEnvironmentVariable("ZC_USMAP") ?? dirs.SelectMany(d => Directory.GetFiles(d, "*.usmap")).FirstOrDefault();
        var oodle = Environment.GetEnvironmentVariable("ZC_OODLE_DIR") ?? dirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "oodle-data-shared.dll")));
        Skip.If(GameExtractor.FindPaksDir(game) == null, "game not installed");
        Skip.If(usmap == null || oodle == null, "no local .usmap / Oodle library");

        using var rig = new TestRig(withGameData: false);              // starts with no game data at all
        rig.Session.Settings.OodleDir = oodle!;                        // use the local library: never download in a test
        rig.Start();
        Assert.False(rig.Session.HasGameData);
        Assert.Equal("Resource_Credits", rig.Main.Session.Current!.Model.Resources.Keys.First());

        Headless.Run(() => rig.Main.Navigate("gamedata"));
        var vm = (GameDataViewModel)rig.Main.Screen!;
        Assert.False(vm.HasData);
        rig.Shot("30-gamedata-empty");
        Headless.Run(() => { vm.GameDir = game; vm.UsmapPath = usmap!; });
        Headless.RunAsync(async () => await vm.ExtractCommand.ExecuteAsync(null)).GetAwaiter().GetResult();

        vm = (GameDataViewModel)rig.Main.Screen!;                      // the screen was rebuilt with the new data
        Assert.True(vm.HasData);
        Assert.Contains("75 upgrades", vm.Status);
        Assert.Contains("Done:", vm.LogText);                          // ...and the log is still there
        Assert.True(vm.HasLog);
        Assert.False(vm.IsRunning);
        Assert.True(File.Exists(rig.Session.Env.GameDbPath));
        Assert.True(rig.Session.HasGameData);
        Assert.Equal("ok", rig.Main.BannerKind);
        // the open save was re-read with the new data, so friendly names show up
        var credits = rig.Main.Session.Current!.View.Header.Resources.First(r => r.Key == "Credits");
        Assert.False(string.IsNullOrEmpty(credits.Description));
        Assert.Equal(game, rig.Session.Settings.GameDir);               // remembered for next time
        rig.Shot("31-gamedata-done");
    }

    [SkippableFact]
    public void Extract_refuses_a_bad_folder_or_missing_mappings_with_a_clear_message()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig(withGameData: false);
        rig.Start();
        Headless.Run(() => rig.Main.Navigate("gamedata"));
        var vm = (GameDataViewModel)rig.Main.Screen!;
        Headless.Run(() => { vm.GameDir = Path.Combine(rig.Tmp, "no-game-here"); vm.UsmapPath = ""; });
        Headless.RunAsync(async () => await vm.ExtractCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
        Assert.Equal("err", rig.Main.BannerKind);
        Assert.Contains("Paks", rig.Main.BannerText);
        Assert.False(vm.HasData);
        Assert.False(vm.IsRunning);
    }

    [SkippableFact]
    public void Importing_a_database_file_makes_it_the_active_game_data()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        Skip.If(!Directory.Exists(Path.Combine(Repo.Root, "gamedata")), "no local game data to export");
        using var donor = new TestRig();                                 // has the database built from the local dumps
        using var rig = new TestRig(withGameData: false);
        var imp = donor.Session.Env.GameDbPath;
        Skip.IfNot(File.Exists(imp), "no database to import");
        rig.Start();
        rig.Ui.PickedFile = imp;
        Headless.Run(() => rig.Main.Navigate("gamedata"));
        var vm = (GameDataViewModel)rig.Main.Screen!;
        Headless.RunAsync(async () => await vm.ImportCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
        Assert.True(vm.HasData);
        Assert.True(rig.Session.HasGameData);
        Assert.Equal("ok", rig.Main.BannerKind);
    }

    [SkippableFact]
    public void The_mappings_field_explains_the_chosen_file_and_extraction_refuses_a_bad_one_before_starting()
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig(withGameData: false);
        rig.Start();
        // a folder that looks like a game install (extraction only needs a .utoc to accept it)
        var paks = Path.Combine(rig.Tmp, "game", "SWZeroCompany", "Content", "Paks");
        Directory.CreateDirectory(paks);
        File.WriteAllBytes(Path.Combine(paks, "x.utoc"), new byte[8]);
        Headless.Run(() => rig.Main.Navigate("gamedata"));
        var vm = (GameDataViewModel)rig.Main.Screen!;

        Headless.Run(() => { vm.GameDir = Path.Combine(rig.Tmp, "game"); vm.UsmapPath = ""; });
        Assert.True(vm.UsmapWarn);
        Assert.Contains("Nexus Mods", vm.UsmapNote);                       // says what it is and where to get it
        Headless.RunAsync(async () => await vm.ExtractCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
        Assert.Equal("err", rig.Main.BannerKind);
        Assert.Contains("mappings", rig.Main.BannerText);
        Assert.False(vm.IsRunning);
        Assert.Equal("", vm.LogText);                                       // refused up front: nothing was started

        var notAMap = Path.Combine(rig.Tmp, "mappings.zip");
        File.WriteAllBytes(notAMap, new byte[] { 0x50, 0x4B, 3, 4 });
        Headless.Run(() => vm.UsmapPath = notAMap);
        Assert.True(vm.UsmapBad);
        Assert.Contains("unzip", vm.UsmapNote);
        rig.Shot("32-gamedata-badmap");
        Headless.RunAsync(async () => await vm.ExtractCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
        Assert.Contains("unzip", rig.Main.BannerText);

        var good = Path.Combine(rig.Tmp, "SWZeroCompany-5.6.1-196320+++ProjectBruno+Stable-abc.usmap");
        File.WriteAllBytes(good, new byte[] { 0xC4, 0x30, 4, 0, 0, 0, 0 });
        Headless.Run(() => vm.UsmapPath = good);
        Assert.True(vm.UsmapOk);
        Assert.Contains("5.6.1", vm.UsmapNote);
    }
}
