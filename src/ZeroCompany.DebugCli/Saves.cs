using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ZeroCompany.DebugCli;

/// <summary>
/// <c>zcdebug saves</c>: the save folder as the game sees it. Shows each save's file name in the form <c>zcdebug load</c> wants (no .sav),
/// the name the game's load menu shows (the comment in its header), and whether the game probably lists it. The game builds its list of
/// saves once, at start-up, and refuses to load a name that is not on it, so a file created after the game started is "new".
/// </summary>
public static class Saves
{
    public sealed record Entry(string Name, string Comment, string Mode, DateTime Modified, DateTime Created);

    public static string DefaultDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWZeroCompany", "Saved", "SaveGames");

    /// <summary>When the game process started (the earliest of its processes), or null when it is not running or cannot be asked.</summary>
    public static DateTime? GameStart()
    {
        DateTime? start = null;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!p.ProcessName.StartsWith("SWZeroCompany", StringComparison.OrdinalIgnoreCase)) continue;
                if (start == null || p.StartTime < start) start = p.StartTime;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
        return start;
    }

    /// <summary>The saves in <paramref name="dir"/>, newest first. Files that are not saves (settings, partial writes) are skipped.</summary>
    public static List<Entry> Read(string dir)
    {
        var list = new List<Entry>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.EnumerateFiles(dir, "*.sav"))
        {
            var info = ReadHeader(path);
            if (info == null) continue;
            var fi = new FileInfo(path);
            list.Add(new Entry(Path.GetFileNameWithoutExtension(path), info.Value.Comment, info.Value.Mode, fi.LastWriteTime, fi.CreationTime));
        }
        return list.OrderByDescending(e => e.Modified).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    static (string Comment, string Mode)? ReadHeader(string path)
    {
        try
        {
            using var z = ZipFile.OpenRead(path);
            var e = z.GetEntry("SaveGameInfoJSON.txt");
            if (e == null || e.Length > 1 << 20) return null;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var raw = ms.ToArray();
            int skip = raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE ? 2 : 0;
            using var doc = JsonDocument.Parse(Encoding.Unicode.GetString(raw, skip, raw.Length - skip).TrimEnd('\0', ' ', '\r', '\n', '\t'));
            string Get(string k) => doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return (Get("comment"), Get("gameMode"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Print the list. <paramref name="gameStart"/> null means the game is not running, so nothing is marked.</summary>
    public static int List(string dir, string filter, bool all, DateTime? gameStart, TextWriter output) =>
        List(Read(dir), dir, filter, all, gameStart, output);

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
