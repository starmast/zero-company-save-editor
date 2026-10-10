using System.Text.Json;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Tests;

/// <summary>
/// End-to-end: mounts the real game install and extracts. Skipped unless the game, a .usmap and a local
/// Oodle library are all present (set ZC_GAME_DIR / ZC_USMAP / ZC_OODLE_DIR to override the defaults).
/// </summary>
public class ExtractorIntegrationTests
{
    /// <summary>Folders where a developer may have left the (git-ignored) mappings file and Oodle library.</summary>
    static IEnumerable<string> LocalToolDirs() =>
        new[] { "gamedata", Path.Combine("tools", "extract") }.Select(d => Path.Combine(Repo.Root, d)).Where(Directory.Exists);

    static string? Usmap() =>
        Environment.GetEnvironmentVariable("ZC_USMAP")
        ?? LocalToolDirs().SelectMany(d => Directory.GetFiles(d, "*.usmap")).FirstOrDefault();

    [SkippableFact]
    public void Extractor_output_matches_the_legacy_dumps()
    {
        var game = Environment.GetEnvironmentVariable("ZC_GAME_DIR") ?? GameExtractor.DefaultGameDir;
        var usmap = Usmap();
        var oodleDir = Environment.GetEnvironmentVariable("ZC_OODLE_DIR")
                       ?? LocalToolDirs().FirstOrDefault(d => File.Exists(Path.Combine(d, "oodle-data-shared.dll"))) ?? "";
        Skip.If(GameExtractor.FindPaksDir(game) == null, "game not installed");
        Skip.If(usmap == null, "no .usmap");
        Skip.IfNot(File.Exists(Path.Combine(oodleDir, "oodle-data-shared.dll")), "no local Oodle library");
        var legacy = RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        Skip.If(legacy == null, "no legacy dumps to compare against");

        var log = new List<string>();
        var db = GameExtractor.Extract(new ExtractOptions { GameDir = game, UsmapPath = usmap!, OodleDir = oodleDir },
            new SyncProgress(log.Add));
        var expected = Distiller.Distill(legacy!);

        Assert.Equal(75, db.Upgrades.Count);
        Assert.Equal(Normalize(expected), Normalize(db));
    }

    sealed class SyncProgress : IProgress<string>
    {
        readonly Action<string> _a;
        public SyncProgress(Action<string> a) { _a = a; }
        public void Report(string value) => _a(value);
    }

    static string Normalize(GameDatabase db)
    {
        db.ExtractedUtc = default; db.GameDir = "";
        return JsonSerializer.Serialize(db);
    }
}
