using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core.Platform;

/// <summary>
/// This user's setup in one place: where data lives, the saved settings, the save folders, the game install and the Oodle folder.
/// The editor, <c>zc</c> and <c>zcdebug</c> all ask this instead of working these out themselves, so they always agree.
/// </summary>
public sealed class ZcWorkspace
{
    /// <summary>Environment variable that points the tools at another data folder (settings, game data, backups).</summary>
    public const string DataDirVariable = "ZC_DATA_DIR";

    public AppEnvironment Env { get; }
    public AppSettings Settings { get; }

    public ZcWorkspace(AppEnvironment env, AppSettings settings) { Env = env; Settings = settings; }

    /// <summary>The data folder: <paramref name="dataDir"/>, else <c>ZC_DATA_DIR</c>, else the per-user default.</summary>
    public static ZcWorkspace Open(string? dataDir = null, bool useSystemSaveDirs = true)
    {
        dataDir ??= Environment.GetEnvironmentVariable(DataDirVariable);
        var env = new AppEnvironment(string.IsNullOrWhiteSpace(dataDir) ? null : dataDir) { UseSystemSaveDirs = useSystemSaveDirs };
        return new ZcWorkspace(env, AppSettings.Load(env));
    }

    /// <summary>The save folders (id -> path): the game's own, any Wine prefixes, and the ones the user added.</summary>
    public IReadOnlyDictionary<string, string> SaveDirs => Env.DiscoverSaveDirs(Settings.SaveDirs);

    /// <summary>The game install: <paramref name="explicitDir"/>, else the saved setting, else the first guess, else the OS default.</summary>
    public string GameDir(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;
        if (Settings.GameDir.Length > 0) return Settings.GameDir;
        return AppEnvironment.GuessGameDirs().FirstOrDefault() ?? GameExtractor.DefaultGameDir;
    }

    /// <summary>Where the Oodle library is kept: the saved setting, else the data folder.</summary>
    public string OodleDir => Settings.OodleDir.Length > 0 ? Settings.OodleDir : Env.DataDir;

    /// <summary>The mappings file: the explicit path, else the saved setting, else the first .usmap found in the data folders, else "".</summary>
    public string UsmapPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;
        return Settings.UsmapPath.Length > 0 ? Settings.UsmapPath : GuessUsmap();
    }

    public string GuessUsmap()
    {
        foreach (var dir in new[] { Env.DataDir, Env.GameDataDir, AppContext.BaseDirectory })
            try
            {
                var f = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.usmap").FirstOrDefault() : null;
                if (f != null) return f;
            }
            catch (IOException) { }
        return "";
    }

    /// <summary>What the extractor and the asset reader need, from the explicit values or else the saved settings.</summary>
    public ExtractOptions ExtractOptions(string? gameDir = null, string? usmap = null) =>
        new() { GameDir = GameDir(gameDir), UsmapPath = UsmapPath(usmap), OodleDir = OodleDir };

    /// <summary>The extracted game database, or an empty one.</summary>
    public GameDatabase LoadDatabase() => GameDatabase.TryLoad(Env.GameDbPath) ?? GameDatabase.Empty;

    /// <summary>The Win64 binaries folder of the game (where UE4SS lives), or null.</summary>
    public static string? BinariesDir(string gameDir)
    {
        foreach (var c in new[] { Path.Combine(gameDir, "SWZeroCompany", "Binaries", "Win64"), Path.Combine(gameDir, "Binaries", "Win64"), gameDir })
            if (File.Exists(Path.Combine(c, "SWZeroCompany.exe")) || Directory.Exists(Path.Combine(c, "ue4ss"))) return c;
        return null;
    }
}
