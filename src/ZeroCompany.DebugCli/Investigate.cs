using System.Text.RegularExpressions;

namespace ZeroCompany.DebugCli;

/// <summary>
/// Commands that let one person (or an agent) investigate the running game without touching its window: <c>reload</c> swaps the mod's
/// script without a restart, <c>shot</c> takes a screenshot with the HUD, <c>api</c> searches the class and function names UE4SS dumped.
/// </summary>
public static class Investigate
{
    // ---- reload -------------------------------------------------------------------------------------------------------

    /// <summary>The mod's own script as installed in the game's Mods folder.</summary>
    public static string InstalledScript(string? game)
    {
        var dir = Install.FindGameDir(game) ?? throw new CliException("game folder unknown: pass --game \"C:\\...\\Star Wars Zero Company\"");
        var bin = Install.BinariesDir(dir) ?? throw new CliException($"no SWZeroCompany.exe under {dir}");
        var mods = Install.ModsDir(bin) ?? throw new CliException("UE4SS's Mods folder was not found");
        return Path.Combine(mods, Install.ModName, "Scripts", "main.lua");
    }

    /// <summary>
    /// Ask the running mod to run its script again. With <paramref name="from"/> that file is copied over the installed script first, so a
    /// change made in the repo is live a moment later, with no Ctrl+R. The mod checks the new script's syntax before it lets go of the
    /// old one, and waits here until it answers as the new version.
    /// </summary>
    public static int Reload(Bridge b, string? game, string? from, bool write, TimeSpan timeout, TextWriter output, TimeSpan? wait = null)
    {
        if (!write) throw new CliException("'reload' changes the running mod; add --write if you mean it");
        if (!b.Connected(TimeSpan.FromSeconds(10))) throw new CliException("the mod is not answering (no recent heartbeat). Run `zcdebug status`.");
        if (from != null)
        {
            if (!File.Exists(from)) throw new CliException($"no such file: {from}");
            var target = InstalledScript(game);
            try { File.Copy(from, target, overwrite: true); }
            catch (UnauthorizedAccessException) { throw new CliException($"cannot write {target}: run `zcdebug fix` first"); }
            output.WriteLine($"copied {from} -> {target}");
        }
        Reply r;
        try { r = b.Send("reload", "", timeout); }
        catch (TimeoutException e) { throw new CliException(e.Message + ". The game may be paused, loading or busy."); }
        if (!r.Ok) { output.WriteLine("error: " + r.Text); return 1; }
        output.WriteLine(r.Text);
        var m = Regex.Match(r.Text, @"-> (\S+)$");
        if (!m.Success) return 0;
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(400);
            try
            {
                var p = b.Send("ping", "", TimeSpan.FromSeconds(2));
                if (p.Ok && p.Text.EndsWith("zcdebug-mod " + m.Groups[1].Value, StringComparison.Ordinal)) { output.WriteLine("now running " + m.Groups[1].Value); return 0; }
            }
            catch (TimeoutException) { }
        }
        output.WriteLine($"the mod did not answer as {m.Groups[1].Value}: the new script may have failed to start (see UE4SS.log); the old one keeps running if it did");
        return 1;
    }

    // ---- screenshot ---------------------------------------------------------------------------------------------------

    /// <summary>Where the game writes screenshots; <c>ZCDEBUG_SHOTS</c> overrides it (for running under WSL).</summary>
    public static string ScreenshotDir() =>
        Environment.GetEnvironmentVariable("ZCDEBUG_SHOTS") is { Length: > 0 } over
            ? over
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWZeroCompany", "Saved", "Screenshots", "Windows");

    /// <summary>
    /// Have the game write a screenshot that includes the HUD (<c>Shot showui</c>; plain HighResShot leaves the HUD out) and return its
    /// path. With <paramref name="copyTo"/> the file is also copied there.
    /// </summary>
    public static int Shot(Bridge b, string? copyTo, TimeSpan timeout, TextWriter output, string? dir = null, TimeSpan? wait = null)
    {
        dir ??= ScreenshotDir();
        Directory.CreateDirectory(dir);
        var before = Directory.GetFiles(dir, "ScreenShot*.png").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var r = Session.RunQuiet(b, "exec", "Shot showui", write: true, timeout);
        if (!r.Ok) { output.WriteLine("error: " + r.Text); return 1; }
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(300);
            var fresh = Directory.GetFiles(dir, "ScreenShot*.png").Where(f => !before.Contains(f)).OrderBy(File.GetLastWriteTimeUtc).LastOrDefault();
            if (fresh == null) continue;
            long size = -1;                                           // wait for the game to finish writing it
            for (int i = 0; i < 20 && new FileInfo(fresh).Length != size; i++) { size = new FileInfo(fresh).Length; Thread.Sleep(250); }
            var path = fresh;
            if (copyTo != null) { File.Copy(fresh, copyTo, overwrite: true); path = copyTo; }
            output.WriteLine(path);
            return 0;
        }
        output.WriteLine("no screenshot appeared (is the game window up, and is console input allowed?)");
        return 1;
    }

    // ---- api search ---------------------------------------------------------------------------------------------------

    /// <summary>The folder UE4SS's "Dump CXX Headers" writes, or null when it has not been run.</summary>
    public static string? HeaderDumpDir(string? game)
    {
        var dir = Install.FindGameDir(game);
        var bin = dir == null ? null : Install.BinariesDir(dir);
        if (bin == null) return null;
        foreach (var c in new[] { Path.Combine(bin, "ue4ss", "CXXHeaderDump"), Path.Combine(bin, "CXXHeaderDump") })
            if (Directory.Exists(c)) return c;
        return null;
    }

    static readonly Regex Declares = new(@"^\s*(class|struct|enum( class)?)\s+\w", RegexOptions.Compiled);

    /// <summary>
    /// Search the dumped headers for classes, structs, enums and function declarations whose line contains <paramref name="text"/>
    /// (case-insensitive). One line per match: <c>file:line  declaration</c>. The match limit keeps a short word from flooding the output.
    /// </summary>
    public static int Api(string headersDir, string text, int limit, TextWriter output)
    {
        if (text.Trim().Length == 0) throw new CliException("usage: zcdebug api <text> [--limit N]");
        int found = 0;
        foreach (var file in Directory.EnumerateFiles(headersDir, "*.hpp", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            int n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (line.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var t = line.Trim();
                bool declaration = Declares.IsMatch(line) || (t.EndsWith(");") && t.Contains('('));
                if (!declaration) continue;
                output.WriteLine($"{Path.GetRelativePath(headersDir, file)}:{n}  {(t.Length > 200 ? t[..200] + "..." : t)}");
                if (++found >= limit) { output.WriteLine($"... stopped at {limit} matches (use a longer text or --limit)"); return 0; }
            }
        }
        if (found == 0) output.WriteLine("no class, struct or function declaration matches");
        return found == 0 ? 1 : 0;
    }
}
