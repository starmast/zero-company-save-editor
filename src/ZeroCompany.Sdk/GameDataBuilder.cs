using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Sdk;

/// <summary>Extracts the game database the way the editor's Game data screen does, using the same defaults and saving it in the same place.</summary>
public static class GameDataBuilder
{
    /// <summary>
    /// Read the game's packages (read-only) and write the database to the workspace. The game folder and mappings file used are
    /// remembered in the settings, as the editor does. May fetch the Oodle library on the first run (checked against a pinned hash).
    /// </summary>
    public static GameDatabase Extract(ZcWorkspace ws, IProgress<string>? log = null, string? gameDir = null, string? usmap = null, CancellationToken ct = default)
    {
        var opt = ws.ExtractOptions(gameDir, usmap);
        if (opt.GameDir.Length > 0) opt.GameDir = Path.GetFullPath(opt.GameDir);              // remembered in the settings: keep it valid from any folder
        if (opt.UsmapPath.Length > 0) opt.UsmapPath = Path.GetFullPath(opt.UsmapPath);
        if (GameExtractor.FindPaksDir(opt.GameDir) == null)
            throw new ZcException($"could not find the game's Paks folder under '{opt.GameDir}' (pass --game with the folder that contains SWZeroCompany)");
        var map = UsmapInfo.Inspect(opt.UsmapPath);
        if (!map.IsUsable) throw new ZcException(map.Message);
        ws.Settings.GameDir = opt.GameDir;
        ws.Settings.UsmapPath = opt.UsmapPath;
        ws.Settings.Save(ws.Env);
        try
        {
            var db = GameExtractor.ExtractAsync(opt, log, ct).GetAwaiter().GetResult();
            db.Save(ws.Env.GameDbPath);
            return db;
        }
        catch (ExtractionException e) { throw new ZcException(e.Message, e); }
    }
}
