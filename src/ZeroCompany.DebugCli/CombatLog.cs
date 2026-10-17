namespace ZeroCompany.DebugCli;

/// <summary>
/// <c>zcdebug log show [n]</c>: the end of combat.log, which the mod appends to while <c>zcdebug log on</c> is active. Reading it needs no
/// answer from the game, so it also works after the game has closed.
/// </summary>
public static class CombatLog
{
    public static string PathIn(string dir) => Path.Combine(dir, "combat.log");

    /// <summary>Print the last <paramref name="count"/> lines (default 60). Returns 1 when there is no log yet.</summary>
    public static int Show(string dir, string args, TextWriter output, TextWriter error)
    {
        var path = PathIn(dir);
        if (!File.Exists(path)) { error.WriteLine("no combat log yet: run `zcdebug log on`, play, then `zcdebug log show`"); return 1; }
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = parts.Length > 1 && int.TryParse(parts[1], out var n) && n > 0 ? n : 60;
        string[] lines;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))    // the mod may be appending
        using (var sr = new StreamReader(fs)) lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines.Skip(Math.Max(0, lines.Length - count))) output.WriteLine(l.TrimEnd('\r'));
        return 0;
    }
}
