using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Sdk;

/// <summary>What went wrong, for <see cref="DebugReport"/>: what was being done and the exception (null when there was none).</summary>
public sealed record DebugIncident(string What, Exception? Error, DateTime When);

/// <summary>
/// A plain-text report to paste into a bug report: the tool and OS, the last error with its stack trace, what was pending, a summary
/// of the open save made of counts and the game build (no names, GUIDs or bytes), the save checks and the setup checks
/// (<see cref="ZcDoctor"/>). The home folder is shown as <c>~</c>. A bundle is the same report zipped with the save file and the
/// crash log, for bugs that need the real file.
/// </summary>
public static class DebugReport
{
    public const string ReportEntry = "report.txt";
    const int CrashLogTail = 64 * 1024;

    /// <summary>"name version" of the running program, e.g. "ZeroCompanyEditor 0.7.0+439b5e3".</summary>
    public static string ToolName(Assembly? asm = null)
    {
        asm ??= Assembly.GetEntryAssembly() ?? typeof(DebugReport).Assembly;
        var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "?";
        if (v.IndexOf('+') is int plus and >= 0 && v.Length > plus + 8) v = v[..(plus + 8)];         // keep a short commit id
        return $"{asm.GetName().Name} {v}";
    }

    public static string Build(ZcWorkspace ws, string tool, string? savePath, IEnumerable<PendingLine>? pending = null, DebugIncident? incident = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Zero Company save editor - debug report");
        sb.AppendLine($"made:    {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"tool:    {tool}");
        sb.AppendLine($"system:  {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");

        if (incident != null)
        {
            sb.AppendLine().AppendLine($"== Error ({incident.When.ToString("HH:mm:ss", CultureInfo.InvariantCulture)})");
            sb.AppendLine(incident.What);
            if (incident.Error != null) sb.AppendLine(incident.Error.ToString());
        }

        var lines = pending?.ToList() ?? new List<PendingLine>();
        sb.AppendLine().AppendLine($"== Pending changes ({lines.Count})");
        foreach (var l in lines) sb.AppendLine("- " + Pending(l));

        sb.AppendLine().AppendLine("== Save");
        if (savePath == null) sb.AppendLine("none open");
        else
        {
            foreach (var l in SaveSummary(savePath)) sb.AppendLine(l);
            sb.AppendLine().AppendLine("== Save checks");
            try { foreach (var c in SaveCheck.Run(ws, savePath)) sb.AppendLine(Line(c)); }
            catch (Exception e) { sb.AppendLine($"could not run: {e.GetType().Name}: {e.Message}"); }
        }

        sb.AppendLine().AppendLine("== Setup");
        try { foreach (var c in ZcDoctor.Run(ws)) sb.AppendLine(Line(c)); }
        catch (Exception e) { sb.AppendLine($"could not run: {e.GetType().Name}: {e.Message}"); }

        return Redact(sb.ToString());
    }

    /// <summary>
    /// Zip <paramref name="report"/>, the save file (when given) and the end of crash.log into <paramref name="zipPath"/>. Returns what
    /// went in, in words ("the report, the save and the crash log").
    /// </summary>
    public static string WriteBundle(ZcWorkspace ws, string zipPath, string report, string? savePath)
    {
        var parts = new List<string> { "the report" };
        var tmp = zipPath + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                Text(zip, ReportEntry, report);
                if (savePath != null && File.Exists(savePath))
                {
                    zip.CreateEntryFromFile(savePath, Path.GetFileName(savePath), CompressionLevel.Optimal);
                    parts.Add("the save");
                }
                var log = Path.Combine(ws.Env.DataDir, "crash.log");
                if (File.Exists(log)) { Text(zip, "crash.log", Redact(Tail(log))); parts.Add("the crash log"); }
            }
            File.Move(tmp, zipPath, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
    }

    static void Text(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    static string Tail(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length <= CrashLogTail ? Encoding.UTF8.GetString(bytes)
            : "(earlier entries left out)\n" + Encoding.UTF8.GetString(bytes, bytes.Length - CrashLogTail, CrashLogTail);
    }

    static string Pending(PendingLine l) =>
        l.Text != null ? $"{l.Where}: {l.Text}"
        : $"{l.Where}: {l.Label}" + (l.From is double f ? $" {f.ToString(CultureInfo.InvariantCulture)} ->" : "") + (l.To is double t ? $" {t.ToString(CultureInfo.InvariantCulture)}" : "");

    static string Line(Check c) =>
        $"[{c.Status.ToString().ToLowerInvariant()}] {c.Name}: {c.Detail}" + (c.Fix != null ? $" (fix: {c.Fix})" : "");

    /// <summary>The save as counts: the file, the game build and mode from the save's info, and what the inventory holds.</summary>
    public static List<string> SaveSummary(string path)
    {
        var l = new List<string>();
        try
        {
            var fi = new FileInfo(path);
            l.Add($"file:      {fi.Name}, {fi.Length:N0} bytes, changed {fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
            var c = SaveContainer.Load(File.ReadAllBytes(path));
            if (c.SaveInfo() is { } info)
            {
                string S(string k) => info[k] is JsonValue v ? v.ToString() : "?";
                l.Add($"game:      changelist {S("changelist")} (base {S("baseChangelist")}, {S("branchName")}, {S("platform")})");
                l.Add($"campaign:  {S("gameMode")} on {S("worldName")}, turn {S("strategyTurn")}, difficulty {S("currentDifficultyLevel")}, permadeath {S("bPermaDeath")}, {S("saveGameType")}"
                      + (S("gameMode") == "Strategy" ? "" : $", combat turn {S("combatTurn")}"));
            }
            else l.Add("game:      no save info (not a game save?)");
            var g = new GvasFile(c.Gvas);
            l.Add($"structure: {g.Walk().Count():N0} properties, {g.OpaqueCount} left unparsed");
            l.Add("inventory: " + Inventory(g));
        }
        catch (Exception e) { l.Add($"cannot be read: {e.GetType().Name}: {e.Message}"); }
        return l;
    }

    /// <summary>Entries per kind, and how many of them are equipped or have a count other than 1 (the game stores one item per entry).</summary>
    static string Inventory(GvasFile g)
    {
        if (g.StrategyDataOrNull() is not { } sd || g.Child(sd, "InventoryItemData") is not { Children: { } entries }) return "none";
        var kinds = new SortedDictionary<string, (int N, int Equipped, int NotOne)>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            var v = e.Children is { Count: > 1 } ch ? ch[1] : g.Child(e, "value");
            if (v == null || g.Child(v, "ItemAsset") is not { } a) continue;
            var path = g.GetStr(a);
            var kind = path.Contains("/UtilityItems/") ? "utility items" : path.Contains("/Modifications/") ? "weapon mods"
                     : path.Contains("/Resources/") ? "resources" : "other";
            var k = kinds.GetValueOrDefault(kind);
            k.N++;
            if (g.Child(v, "EquippedCharacterIDs") is { Size: > 8 }) k.Equipped++;
            if (kind != "resources" && g.Child(v, "ItemCount") is { } cnt && g.GetInt(cnt) != 1) k.NotOne++;
            kinds[kind] = k;
        }
        return kinds.Count == 0 ? "empty" : string.Join(", ", kinds.Select(kv =>
            $"{kv.Key} {kv.Value.N}" + (kv.Key == "resources" ? "" : $" ({kv.Value.Equipped} equipped" + (kv.Value.NotOne > 0 ? $", {kv.Value.NotOne} with a count other than 1" : "") + ")")));
    }

    /// <summary>Show the home folder as <c>~</c>, so the report does not carry the account name.</summary>
    public static string Redact(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length < 4) return text;
        text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        return text.Replace(home.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
    }
}
