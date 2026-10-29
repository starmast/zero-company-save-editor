using CUE4Parse.Compression;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.Sdk;

/// <summary>
/// Reports what is set up and what is missing, with the fix for each. Reads only. A missing optional piece is a warning; a piece that
/// is set but unusable (a mappings file that is not one, a game folder without packages) is a failure.
/// </summary>
public static class ZcDoctor
{
    static string Download => $"download the community mappings file for your game version ({UsmapInfo.DownloadUrl}), then run: zc data extract --usmap FILE";

    public static IReadOnlyList<Check> Run(ZcWorkspace ws)
    {
        var l = new List<Check>();
        l.Add(Check.Ok("data folder", ws.Env.DataDir));
        SaveFolders(ws, l);
        Game(ws, l);
        Usmap(ws, l);
        Oodle(ws, l);
        Database(ws, l);
        Backups(ws, l);
        var running = GameProcess.Running();
        l.Add(running.Count == 0 ? Check.Ok("game process", "not running (saves can be edited in place)")
            : Check.Warn("game process", $"running ({string.Join(", ", running.Select(r => $"{r.Name} pid {r.Id}"))}); in-place writes are refused", "close the game, or write a copy with --copy"));
        return l;
    }

    static void SaveFolders(ZcWorkspace ws, List<Check> l)
    {
        var dirs = ws.SaveDirs;
        if (dirs.Count == 0)
        {
            l.Add(Check.Warn("save folders", "none found", "start the game once so it creates its save folder, or add a folder in the editor's Saves screen"));
            return;
        }
        foreach (var (id, path) in dirs) l.Add(Check.Ok("save folder " + id, $"{path} ({SaveCatalog.List(new Dictionary<string, string> { [id] = path }).Count} saves)"));
    }

    static void Game(ZcWorkspace ws, List<Check> l)
    {
        var dir = ws.GameDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            l.Add(Check.Warn("game folder", string.IsNullOrEmpty(dir) ? "not found" : dir + " does not exist",
                "needed only for game data and zc assets: pass --game DIR to zc data extract, or set it in the editor's Game data screen"));
            return;
        }
        var paks = GameExtractor.FindPaksDir(dir);
        l.Add(paks == null ? Check.Fail("game folder", $"{dir} has no Paks folder with packages", "pick the folder that contains SWZeroCompany")
                           : Check.Ok("game folder", $"{dir} (packages in {paks})"));
    }

    static void Usmap(ZcWorkspace ws, List<Check> l)
    {
        var path = ws.UsmapPath();
        var info = UsmapInfo.Inspect(path);
        l.Add(info.Status switch
        {
            UsmapStatus.Ok => Check.Ok("mappings (.usmap)", $"{path} ({info.Message})"),
            UsmapStatus.OkWithWarning => Check.Warn("mappings (.usmap)", info.Message),
            UsmapStatus.Missing => Check.Warn("mappings (.usmap)", "none set", Download),
            _ => Check.Fail("mappings (.usmap)", info.Message, Download),
        });
    }

    static void Oodle(ZcWorkspace ws, List<Check> l)
    {
        var dir = ws.OodleDir;
        var found = new[] { OodleHelper.OodleFileName, "oodle-data-shared.dll", "liboodle-data-shared.so" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        l.Add(found != null ? Check.Ok("Oodle library", found)
            : Check.Warn("Oodle library", $"not in {dir}", "run zc data extract (or the editor's extraction) once; it fetches and checks the library"));
    }

    static void Database(ZcWorkspace ws, List<Check> l)
    {
        var path = ws.Env.GameDbPath;
        if (!File.Exists(path)) { l.Add(Check.Warn("game data", "not extracted yet", "run zc data extract (names and costs in the editor come from it)")); return; }
        var db = ws.LoadDatabase();
        if (db.IsEmpty) { l.Add(Check.Fail("game data", path + " cannot be read", "run zc data extract again")); return; }
        var summary = $"{db.Upgrades.Count} upgrades, {db.Items.Count} items, {db.Effects.Count} effects, extracted {db.ExtractedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        var paks = GameExtractor.FindPaksDir(ws.GameDir());
        var newest = paks == null ? (DateTime?)null : Directory.EnumerateFiles(paks, "*.utoc").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        l.Add(db.IsStale
            ? Check.Warn("game data", summary + "; extracted by an older version of zc", "run zc data extract to get the names, values and limits this version reads")
            : newest != null && newest > db.ExtractedUtc
            ? Check.Warn("game data", summary + "; the game files are newer", "run zc data extract to refresh it")
            : Check.Ok("game data", summary));
    }

    static void Backups(ZcWorkspace ws, List<Check> l)
    {
        var dir = ws.Env.BackupsDir;
        if (!Directory.Exists(dir)) { l.Add(Check.Ok("backups", "none yet")); return; }
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
        l.Add(Check.Ok("backups", $"{files.Count} files, {files.Sum(f => f.Length) / (1024 * 1024):N0} MB in {dir}"));
    }
}
