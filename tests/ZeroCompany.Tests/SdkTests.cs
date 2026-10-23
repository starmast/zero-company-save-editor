using System.IO.Compression;
using System.Text;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;
using ZeroCompany.Sdk;
using ZeroCompany.Sdk.Cli;

namespace ZeroCompany.Tests;

/// <summary>The parts of the SDK that need no real save or game: lookup rules, argument parsing, the doctor and the edit script.</summary>
public class SdkTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-sdk-" + Guid.NewGuid().ToString("N"));
    public SdkTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    ZcWorkspace Ws(params string[] saveDirs)
    {
        var ws = ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false);
        foreach (var d in saveDirs) ws.Settings.SaveDirs.Add(d);
        return ws;
    }

    /// <summary>A save-shaped zip whose header says <paramref name="comment"/>.</summary>
    string MakeSave(string dir, string name, string comment, DateTime modified, bool header = true)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".sav");
        using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            z.CreateEntry("SaveGame");
            if (header)
            {
                using var s = z.CreateEntry("SaveGameInfoJSON.txt").Open();
                s.Write(Encoding.Unicode.GetBytes($"{{\"comment\": \"{comment}\", \"gameMode\": \"Tactical\"}}"));
            }
        }
        File.SetLastWriteTime(path, modified);
        return path;
    }

    // ---- workspace -------------------------------------------------------------------------------

    [Fact]
    public void The_game_folder_comes_from_the_explicit_value_then_the_saved_setting()
    {
        var ws = Ws();
        Assert.Equal("X:/explicit", ws.GameDir("X:/explicit"));
        ws.Settings.GameDir = "X:/saved";
        Assert.Equal("X:/saved", ws.GameDir());
        Assert.Equal("X:/explicit", ws.GameDir("X:/explicit"));                       // an explicit value still wins
    }

    [Fact]
    public void Extract_options_use_the_explicit_values_then_the_settings_and_the_data_folder_for_oodle()
    {
        var ws = Ws();
        ws.Settings.GameDir = "G"; ws.Settings.UsmapPath = "U";
        var o = ws.ExtractOptions();
        Assert.Equal(("G", "U", ws.Env.DataDir), (o.GameDir, o.UsmapPath, o.OodleDir));
        ws.Settings.OodleDir = "O";
        Assert.Equal("O", ws.ExtractOptions("G2", "U2").OodleDir);
        Assert.Equal(("G2", "U2"), (ws.ExtractOptions("G2", "U2").GameDir, ws.ExtractOptions("G2", "U2").UsmapPath));
    }

    [Fact]
    public void The_data_folder_is_the_one_asked_for()
    {
        var ws = Ws();
        Assert.Equal(Path.GetFullPath(Path.Combine(_tmp, "data")), Path.GetFullPath(ws.Env.DataDir));
        Assert.Equal(Path.Combine(ws.Env.DataDir, "backups"), ws.Env.BackupsDir);
    }

    [Fact]
    public void The_package_path_of_a_level_drops_umap_like_uasset()
    {
        Assert.Equal("/Game/Game/Maps/X/Y", PackageReader.PackagePath("SWZeroCompany/Content/Game/Maps/X/Y.umap"));
        Assert.Equal("/Game/Game/A/B", PackageReader.PackagePath("SWZeroCompany/Content/Game/A/B.uasset"));
    }

    // ---- save catalog ----------------------------------------------------------------------------

    [Fact]
    public void Saves_are_found_by_path_latest_exact_name_fragment_and_comment()
    {
        var dir = Path.Combine(_tmp, "saves");
        var t = new DateTime(2026, 10, 14, 12, 0, 0);
        var old = MakeSave(dir, "Alpha_Old", "First run", t.AddDays(-2));
        MakeSave(dir, "Alpha_New", "Second run", t.AddDays(-1));
        var newest = MakeSave(dir, "Beta", "COMBAT BASE 4.0", t);
        var ws = Ws(dir);

        Assert.Equal(Path.GetFullPath(old), Path.GetFullPath(SaveCatalog.Resolve(ws, old).Path));
        Assert.Equal("Beta.sav", SaveCatalog.Resolve(ws, "latest").Name);
        Assert.Equal("Alpha_Old.sav", SaveCatalog.Resolve(ws, "alpha_old").Name);              // exact name, no .sav
        Assert.Equal("Alpha_New.sav", SaveCatalog.Resolve(ws, "Alpha").Name);                  // a fragment: the newest match
        Assert.Equal("Beta.sav", SaveCatalog.Resolve(ws, "combat base 4.0").Name);             // the comment, any case
        Assert.Equal("COMBAT BASE 4.0", SaveCatalog.Resolve(ws, newest).Comment);
        var ex = Assert.Throws<ZcException>(() => SaveCatalog.Resolve(ws, "Gamma"));
        Assert.Contains("no save matches", ex.Message);
    }

    [Fact]
    public void The_catalog_lists_newest_first_and_skips_settings_and_zips_without_a_header()
    {
        var dir = Path.Combine(_tmp, "saves");
        var t = DateTime.Now;
        MakeSave(dir, "A", "a", t.AddHours(-2));
        MakeSave(dir, "B", "b", t.AddHours(-1));
        MakeSave(dir, "NoHeader", "x", t, header: false);
        File.WriteAllText(Path.Combine(dir, "SharedGameSettings.sav"), "not a zip");
        var list = SaveCatalog.List(Ws(dir));
        Assert.Equal(new[] { "B.sav", "A.sav" }, list.Select(e => e.Name).ToArray());
        Assert.Equal("Tactical", list[0].Mode);
    }

    [Fact]
    public void Checking_a_settings_file_passes_without_trying_to_open_it_as_a_save()
    {
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SharedGameSettings.sav");
        File.WriteAllBytes(path, Synth.Gvas(Synth.Cat(Synth.IntProp("Volume", 7))));
        var checks = SaveCheck.Run(Ws(dir), path);
        Assert.False(checks.HasFailure());
        Assert.Contains(checks, c => c.Name == "screens" && c.Detail.Contains("not a game save"));
    }

    [Fact]
    public void A_save_outside_the_known_folders_gets_its_own_folder_id_and_stays_openable()
    {
        var elsewhere = MakeSave(Path.Combine(_tmp, "elsewhere"), "Loose", "Loose one", DateTime.Now);
        var ws = Ws(Path.Combine(_tmp, "saves-not-created"));
        var e = SaveCatalog.Resolve(ws, elsewhere);
        Assert.StartsWith("path-", e.DirId);
        Assert.Equal(Path.Combine(_tmp, "elsewhere"), SaveCatalog.DirsFor(ws, e)[e.DirId]);
        Assert.Equal(e.DirId, SaveCatalog.FromPath(elsewhere, ws).DirId);                     // the same folder, the same id
    }

    // ---- save document ---------------------------------------------------------------------------

    [Fact]
    public void A_document_finds_nodes_by_path_and_text()
    {
        var path = Path.Combine(_tmp, "raw.sav");
        File.WriteAllBytes(path, Synth.Sample());
        var doc = SaveDocument.OpenPath(path);
        Assert.Null(doc.NodeAt(""));
        Assert.Equal("Turn", doc.NodeAt("Turn")!.Name);
        var bad = Assert.Throws<ZcException>(() => doc.NodeAt("Turn/Nope"));
        Assert.Contains("no property 'Nope'", bad.Message);
        var (hits, truncated) = doc.Find("Alpha", 10);
        Assert.False(truncated);
        Assert.Contains(hits, h => h.Path == "Name" && h.Value == "Alpha");
        Assert.True(doc.Find("e", 1).Truncated);
        Assert.NotEmpty(doc.Leaves());
    }

    [Fact]
    public void A_file_that_is_not_a_save_is_a_user_error()
    {
        var path = Path.Combine(_tmp, "junk.sav");
        File.WriteAllText(path, "this is not a save");
        Assert.Throws<ZcException>(() => SaveDocument.OpenPath(path));
    }

    // ---- command line ----------------------------------------------------------------------------

    [Fact]
    public void Arguments_are_taken_out_of_the_list_as_they_are_read()
    {
        var a = new CliArgs(new[] { "edit", "--json", "Brentaal", "--limit", "5", "--write", "x=1", "--data-dir", "D" });
        Assert.True(a.Json);
        Assert.Equal("D", a.DataDir);
        Assert.True(a.Flag("--write"));
        Assert.False(a.Flag("--write"));                                                        // already taken
        Assert.Equal(5, a.Num("--limit", 60));
        Assert.Equal(60, a.Num("--limit", 60));
        Assert.Equal(new[] { "edit", "Brentaal", "x=1" }, a.Rest.ToArray());
        Assert.Equal("edit", a.Take("usage"));
        Assert.Equal(new[] { "Brentaal", "x=1" }, a.TakeAll().ToArray());
        Assert.Throws<ZcException>(() => a.Take("zc something"));
    }

    [Fact]
    public void An_option_without_a_value_or_with_a_bad_number_is_an_error()
    {
        Assert.Throws<ZcException>(() => new CliArgs(new[] { "x", "--data-dir" }));
        Assert.Throws<ZcException>(() => new CliArgs(new[] { "x", "--limit", "many" }).Num("--limit", 1));
        Assert.True(new CliArgs(new[] { "-v" }).Flag("-v", "--verbose"));
        Assert.True(new CliArgs(new[] { "--verbose" }).Flag("-v", "--verbose"));
    }

    [Fact]
    public void The_tool_wrapper_turns_failures_into_exit_codes()
    {
        var err = Console.Error; var sw = new StringWriter(); Console.SetError(sw);
        try
        {
            Assert.Equal(1, CliApp.Run("zct", "usage", new[] { "go" }, _ => throw new ZcException("nope")));
            Assert.Contains("zct: nope", sw.ToString());
            Assert.Equal(2, CliApp.Run("zct", "usage", new[] { "go" }, _ => throw new ZcRefusal("game running")));
            Assert.Equal(0, CliApp.Run("zct", "usage", new[] { "go" }, _ => 0));
            Assert.Equal(0, CliApp.Run("zct", "usage", Array.Empty<string>(), _ => throw new InvalidOperationException("never run")));   // no arguments: usage
            Assert.Throws<InvalidOperationException>(() => CliApp.Run("zct", "usage", new[] { "go" }, _ => throw new InvalidOperationException("a bug")));   // bugs are not hidden
        }
        finally { Console.SetError(err); }
    }

    // ---- doctor ----------------------------------------------------------------------------------

    [Fact]
    public void The_doctor_warns_about_what_is_not_set_up_and_fails_on_what_is_set_but_unusable()
    {
        var ws = Ws();
        var plain = ZcDoctor.Run(ws);
        Assert.Equal(CheckStatus.Warn, plain.Single(c => c.Name == "game data").Status);
        Assert.NotNull(plain.Single(c => c.Name == "game data").Fix);
        Assert.NotEqual(CheckStatus.Fail, plain.Single(c => c.Name == "mappings (.usmap)").Status);   // none set is a warning

        var junk = Path.Combine(_tmp, "not-mappings.usmap");
        File.WriteAllText(junk, "this is not a mappings file");
        ws.Settings.UsmapPath = junk;
        var broken = ZcDoctor.Run(ws);
        Assert.Equal(CheckStatus.Fail, broken.Single(c => c.Name == "mappings (.usmap)").Status);
        Assert.True(broken.HasFailure());
    }

    // ---- game data lookup ------------------------------------------------------------------------

    [Fact]
    public void Lookup_lists_the_names_edits_accept_and_needs_game_data_except_for_spawn_sets()
    {
        var db = new GameDatabase();
        db.Items["UtilityItem_Medkit_T1"] = new ItemInfo { Name = "UtilityItem_Medkit_T1", Display = "Medpac", Kind = "Utility", Tier = 1, Rarity = "Common" };
        db.Items["Resource_Credits"] = new ItemInfo { Name = "Resource_Credits", Display = "Credits", Kind = "Resource" };
        var items = GameDataLookup.List(db, "items");
        Assert.Equal("UtilityItem_Medkit_T1", Assert.Single(items).Id);                          // resources are not addable items
        Assert.Single(GameDataLookup.List(db, "items", "medpac"));
        Assert.Empty(GameDataLookup.List(db, "items", "nothing like it"));

        var tiers = GameDataLookup.List(GameDatabase.Empty, "spawn-sets");                       // known without any game data
        Assert.Equal("Coil threat 1", tiers[0].Id);
        Assert.Contains(tiers, t => t.Id == "Imperial threat 9");
        Assert.Throws<ZcException>(() => GameDataLookup.List(GameDatabase.Empty, "items"));
        Assert.Throws<ZcException>(() => GameDataLookup.List(db, "nonsense"));
    }

    // ---- edit script -----------------------------------------------------------------------------

    [Fact]
    public void A_script_reads_values_and_actions_from_json()
    {
        var s = EditScript.Parse("""
            { "save": "latest", // comment
              "set": { "resource.Intelligence": 300, "level": "4" },
              "actions": [ { "do": "heal", "args": ["Neiya"] }, { "do": "unit-dead", "args": [4, false] } ] }
            """);
        Assert.Equal("latest", s.Save);
        Assert.Equal(new[] { ("resource.Intelligence", 300.0), ("level", 4.0) }, s.Set.ToArray());
        Assert.Equal("heal", s.Actions[0].Do);
        Assert.Equal(new[] { "4", "false" }, s.Actions[1].Args.ToArray());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "sett": {} }""")]
    [InlineData("""{ "set": [1] }""")]
    [InlineData("""{ "set": { "level": "many" } }""")]
    [InlineData("""{ "actions": [ { "args": [] } ] }""")]
    [InlineData("""{ "actions": [ { "do": "heal", "args": [ {} ] } ] }""")]
    public void A_bad_script_is_a_user_error(string json) => Assert.Throws<ZcException>(() => EditScript.Parse(json));
}
