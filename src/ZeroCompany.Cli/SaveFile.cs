using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Cli;

/// <summary>A save opened read-only: the container, its main GVAS and the save-info comment. Nothing here ever writes.</summary>
sealed class SaveFile
{
    public string Path { get; }
    public SaveContainer Container { get; }
    public GvasFile Gvas { get; }
    public string Comment { get; }

    SaveFile(string path, SaveContainer c)
    {
        Path = path; Container = c; Gvas = new GvasFile(c.Gvas);
        Comment = c.SaveInfo()?["comment"]?.ToString() ?? "";
    }

    /// <summary>
    /// A path, a file name in the game's save folder, a name fragment ("Brentaal"), a save comment ("COMBAT BASE 4.0"),
    /// or "latest" (the newest save). Ambiguous fragments pick the newest match.
    /// </summary>
    public static SaveFile Open(string what)
    {
        if (File.Exists(what)) return Load(what);
        var all = Candidates().ToList();
        if (what.Equals("latest", StringComparison.OrdinalIgnoreCase) && all.Count > 0) return Load(all[0]);
        foreach (var f in all)
            if (System.IO.Path.GetFileName(f).Contains(what, StringComparison.OrdinalIgnoreCase)) return Load(f);
        foreach (var f in all)                                   // by comment: reads each save's small info entry
        {
            try { if (SaveContainer.Load(File.ReadAllBytes(f)).SaveInfo()?["comment"]?.ToString()?.Equals(what, StringComparison.OrdinalIgnoreCase) == true) return Load(f); }
            catch (Exception e) when (e is IOException or InvalidDataException or GvasException) { }
        }
        throw new CliException($"no save matches '{what}' (give a path, a file name fragment, a save comment, or 'latest')");
    }

    /// <summary>Saves in the game's save folders, newest first.</summary>
    public static IEnumerable<string> Candidates() =>
        new AppEnvironment().DiscoverSaveDirs().Values
            .SelectMany(d => Directory.GetFiles(d, "*.sav"))
            .Where(f => !System.IO.Path.GetFileName(f).StartsWith("SharedGameSettings", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc);

    static SaveFile Load(string path)
    {
        try { return new SaveFile(path, SaveContainer.Load(File.ReadAllBytes(path))); }
        catch (Exception e) when (e is GvasException or InvalidDataException) { throw new CliException($"{System.IO.Path.GetFileName(path)}: {e.Message}"); }
    }

    public static string Short(string s) => Regex.Replace(s, @"/Game/Game/Maps/[^:]*:PersistentLevel\.", "");

    /// <summary>A node's value as short text; containers return "".</summary>
    public string Value(GvasNode x, int max = 160)
    {
        try
        {
            if (x.Children != null) return "";
            if (x.TName == "ObjectProperty") return Short(Gvas.GetStr(x));
            if (x.Size > max) return $"<{x.Size} bytes>";
            var v = Gvas.Get(x);
            return v is byte[] b ? Convert.ToHexString(b) : v?.ToString() ?? "";
        }
        catch (Exception e) when (e is GvasException or ArgumentException or InvalidCastException) { return "?"; }
    }
}

sealed class CliException : Exception
{
    public CliException(string message) : base(message) { }
}
