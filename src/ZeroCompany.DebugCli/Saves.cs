using ZeroCompany.Core.Platform;
using ZeroCompany.Sdk;

namespace ZeroCompany.DebugCli;

/// <summary>
/// <c>zcdebug saves</c>: the save folder as the game sees it. Shows each save's file name in the form <c>zcdebug load</c> wants (no .sav),
/// the name the game's load menu shows (the comment in its header), and whether the game probably lists it. The game builds its list of
/// saves once, at start-up, and refuses to load a name that is not on it, so a file created after the game started is "new".
/// Finding the saves is the SDK's <see cref="SaveCatalog"/>; this adds only the listed/NEW marker.
/// </summary>
public static class Saves
{
    public sealed record Entry(string Name, string Comment, string Mode, DateTime Modified, DateTime Created);

    /// <summary>When the game process started (the earliest of its processes), or null when it is not running or cannot be asked.</summary>
    public static DateTime? GameStart() => GameProcess.StartedAt;

    static Entry ToEntry(CatalogEntry e) => new(e.NameWithoutExtension, e.Comment, e.Mode, e.Modified, e.Created);

    /// <summary>The saves in <paramref name="dir"/>, newest first. Files that are not saves (settings, partial writes) are skipped.</summary>
    public static List<Entry> Read(string dir) => SaveCatalog.ListDir(dir).Select(ToEntry).ToList();

    /// <summary>The saves in all of the user's save folders (the game's own, Wine prefixes, folders added in the editor).</summary>
    public static List<Entry> Read(ZcWorkspace ws) => SaveCatalog.List(ws).Select(ToEntry).ToList();

    /// <summary>Print the list. <paramref name="gameStart"/> null means the game is not running, so nothing is marked.</summary>
    public static int List(string dir, string filter, bool all, DateTime? gameStart, TextWriter output) =>
        List(Read(dir), dir, filter, all, gameStart, output);

    /// <summary>The same for every save folder the user has.</summary>
    public static int List(ZcWorkspace ws, string filter, bool all, DateTime? gameStart, TextWriter output) =>
        List(Read(ws), string.Join(", ", ws.SaveDirs.Values), filter, all, gameStart, output);

    /// <summary>The same, for entries already read (so the marker logic can be tested without setting file times).</summary>
    public static int List(IReadOnlyList<Entry> read, string dir, string filter, bool all, DateTime? gameStart, TextWriter output)
    {
        var entries = read.ToList();
        if (filter.Length > 0)
            entries = entries.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                         || e.Comment.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0) { output.WriteLine(filter.Length > 0 ? $"no saves match '{filter}' in {dir}" : $"no saves in {dir}"); return 1; }

        const int limit = 20;
        var shown = all ? entries : entries.Take(limit).ToList();
        foreach (var e in shown)
        {
            // a file created before the game started was on its list; an overwritten one keeps its creation time, so it still counts
            var flag = gameStart == null ? "" : e.Created < gameStart ? "listed" : "NEW";
            output.WriteLine($"{e.Modified:yyyy-MM-dd HH:mm:ss}  {flag,-6}  {e.Comment,-44}  {e.Name}");
        }
        if (shown.Count < entries.Count) output.WriteLine($"... {entries.Count - shown.Count} more (--all)");
        if (gameStart != null && shown.Any(e => e.Created >= gameStart))
            output.WriteLine("NEW = created after the game started: `zcdebug load` refuses it and the load menu does not show it until the game restarts.");
        return 0;
    }
}
