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
        var dumps = Path.Combine(Repo.Root, "gamedata");
        var legacy = RawGameData.LoadLegacyDirectory(dumps);
        Skip.If(legacy == null, "no legacy dumps to compare against");
        // the dumps describe the game as it was when they were made; after a game update (1.2 changed upgrade costs) they cannot match
        var gameFiles = Directory.EnumerateFiles(GameExtractor.FindPaksDir(game)!, "*.utoc").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        var dumped = Directory.EnumerateFiles(dumps, "*.json").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        Skip.If(gameFiles > dumped, $"the game was updated ({gameFiles:yyyy-MM-dd}) after the dumps were made ({dumped:yyyy-MM-dd}); make new dumps to compare");

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
