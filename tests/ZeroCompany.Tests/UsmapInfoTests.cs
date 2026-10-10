using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.Tests;

public class UsmapInfoTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-usmap-" + Guid.NewGuid().ToString("N"));
    public UsmapInfoTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    string Make(string name, params byte[] bytes)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllBytes(p, bytes.Length > 0 ? bytes : new byte[] { 0xC4, 0x30, 4, 0, 0, 0, 0 });
        return p;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_path_explains_what_a_usmap_is_and_where_to_get_one(string? path)
    {
        var i = UsmapInfo.Inspect(path);
        Assert.Equal(UsmapStatus.Missing, i.Status);
        Assert.False(i.IsUsable);
        Assert.Contains("Nexus Mods", i.Message);
    }

    [Fact]
    public void A_path_that_does_not_exist_is_reported()
    {
        var i = UsmapInfo.Inspect(Path.Combine(_tmp, "nope.usmap"));
        Assert.Equal(UsmapStatus.NotFound, i.Status);
        Assert.Contains("doesn't exist", i.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 3, 4, 0, 0 })]          // a zip that was never unpacked
    [InlineData(new byte[] { 0x7B, 0x22, 0x61, 0x22 })]          // text
    [InlineData(new byte[] { 0xC4 })]                            // too short
    public void Files_without_the_mappings_header_are_refused_with_advice(byte[] bytes)
    {
        var i = UsmapInfo.Inspect(Make("x.usmap", bytes));
        Assert.Equal(UsmapStatus.NotAMappingsFile, i.Status);
        Assert.False(i.IsUsable);
        Assert.Contains("unzip", i.Message);
    }

    [Fact]
    public void The_game_version_is_read_from_the_file_name()
    {
        var i = UsmapInfo.Inspect(Make("SWZeroCompany-5.6.1-196320+++ProjectBruno+Stable-a1e7f571.usmap"));
        Assert.Equal(UsmapStatus.Ok, i.Status);
        Assert.Equal("5.6.1", i.EngineVersion);
        Assert.Equal("196320", i.Changelist);
        Assert.Contains("5.6.1", i.Message);
    }

    [Fact]
    public void A_different_engine_version_warns_but_is_still_allowed()
    {
        var i = UsmapInfo.Inspect(Make("Other-5.4.2-123456+++X.usmap"));
        Assert.Equal(UsmapStatus.OkWithWarning, i.Status);
        Assert.True(i.IsUsable);
        Assert.Contains("5.4.2", i.Message);
    }

    [Fact]
    public void A_name_without_a_version_is_accepted_quietly()
    {
        var i = UsmapInfo.Inspect(Make("mappings.usmap"));
        Assert.Equal(UsmapStatus.Ok, i.Status);
        Assert.Null(i.EngineVersion);
    }

    [SkippableFact]
    public void The_real_local_mappings_file_is_recognised()
    {
        var f = new[] { "gamedata", Path.Combine("tools", "extract") }.Select(d => Path.Combine(Repo.Root, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.usmap")).FirstOrDefault();
        Skip.If(f == null, "no local .usmap");
        Assert.Equal(UsmapStatus.Ok, UsmapInfo.Inspect(f).Status);
    }

    [SkippableFact]
    public void A_file_with_the_right_header_but_wrong_content_gives_a_friendly_failure_not_a_crash()
    {
        var game = Environment.GetEnvironmentVariable("ZC_GAME_DIR") ?? GameExtractor.DefaultGameDir;
        var dirs = new[] { "gamedata", Path.Combine("tools", "extract") }.Select(d => Path.Combine(Repo.Root, d)).Where(Directory.Exists).ToList();
        var oodle = Environment.GetEnvironmentVariable("ZC_OODLE_DIR") ?? dirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "oodle-data-shared.dll")));
        Skip.If(GameExtractor.FindPaksDir(game) == null || oodle == null, "needs the game install and a local Oodle library");
        var junk = Make("junk.usmap", 0xC4, 0x30, 4, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8);
        var ex = Assert.ThrowsAny<Exception>(() => GameExtractor.Extract(
            new ExtractOptions { GameDir = game, UsmapPath = junk, OodleDir = oodle }, new Progress<string>()));
        Assert.IsType<ExtractionException>(ex);                             // never a raw parser exception
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.Contains("different version of the game", ex.Message);
    }
}
