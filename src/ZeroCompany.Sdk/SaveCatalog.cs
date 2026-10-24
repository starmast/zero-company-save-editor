using System.Security.Cryptography;
using System.Text;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Sdk;

/// <summary>One save on disk, from its small info entry only (the 7 MB payload is not read).</summary>
public sealed record CatalogEntry(string DirId, string Path, string Name, string Comment, string Mode, DateTime Modified, DateTime Created)
{
    public string NameWithoutExtension => System.IO.Path.GetFileNameWithoutExtension(Name);
}

/// <summary>Finds saves: the one listing and lookup used by the editor, <c>zc</c> and <c>zcdebug</c>.</summary>
public static class SaveCatalog
{
    /// <summary>Every save in the workspace's folders, newest first.</summary>
    public static List<CatalogEntry> List(ZcWorkspace ws) => List(ws.SaveDirs);

    /// <summary>Every save in one folder, newest first (<c>zcdebug saves --saves DIR</c>).</summary>
    public static List<CatalogEntry> ListDir(string dir) => List(new Dictionary<string, string> { ["dir"] = dir });

    public static List<CatalogEntry> List(IReadOnlyDictionary<string, string> dirs) =>
        SaveListing.List(dirs)
            .Where(e => e.Info.Kind == "save" && HasHeader(e.Info) && !e.Name.StartsWith("SharedGameSettings", StringComparison.OrdinalIgnoreCase))
            .Select(e => ToEntry(e.Dir, System.IO.Path.Combine(dirs[e.Dir], e.Name), e.Info))
            .OrderByDescending(e => e.Modified).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();

    /// <summary>A save the game wrote has its header (name, world, mode); a zip without one is not a save to list.</summary>
    static bool HasHeader(SaveContainer.Summary s) => s.Title != null || s.World != null || s.GameMode != null || s.Created != null;

    static CatalogEntry ToEntry(string dirId, string path, SaveContainer.Summary s) =>
        new(dirId, path, System.IO.Path.GetFileName(path), s.Title ?? "", s.GameMode ?? "", s.MTimeUtc.ToLocalTime(), s.FileCreated ?? File.GetCreationTime(path));

    /// <summary>
    /// A path, a file name in a save folder (with or without .sav), a name fragment ("Brentaal"), a save comment ("COMBAT BASE 4.0"),
    /// or "latest". An exact name wins over a fragment; among fragments the newest save wins.
    /// </summary>
    public static CatalogEntry Resolve(ZcWorkspace ws, string spec)
    {
        if (File.Exists(spec)) return FromPath(spec, ws);
        var all = List(ws);
        if (spec.Equals("latest", StringComparison.OrdinalIgnoreCase) && all.Count > 0) return all[0];
        var exact = all.FirstOrDefault(e => e.Name.Equals(spec, StringComparison.OrdinalIgnoreCase) || e.NameWithoutExtension.Equals(spec, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var frag = all.FirstOrDefault(e => e.Name.Contains(spec, StringComparison.OrdinalIgnoreCase));
        if (frag != null) return frag;
        var byComment = all.FirstOrDefault(e => e.Comment.Equals(spec, StringComparison.OrdinalIgnoreCase));
        if (byComment != null) return byComment;
        // the profile file is not a save, so it is not listed, but its exact file name finds it
        foreach (var dir in ws.SaveDirs.Values)
            foreach (var name in new[] { spec, spec + ".sav" })
            {
                var path = System.IO.Path.Combine(dir, name);
                if (File.Exists(path) && SaveContainer.Summarize(path).Kind == "profile") return FromPath(path, ws);
            }
        throw new ZcException($"no save matches '{spec}' (give a path, a file name fragment, a save comment, or 'latest')");
    }

    /// <summary>A save at any path. One outside the known folders gets a folder id of its own, so backups of same-named saves stay apart.</summary>
    public static CatalogEntry FromPath(string path, ZcWorkspace ws)
    {
        path = System.IO.Path.GetFullPath(path);
        var dir = System.IO.Path.GetDirectoryName(path)!;
        var known = ws.SaveDirs.FirstOrDefault(kv => string.Equals(System.IO.Path.GetFullPath(kv.Value), dir, StringComparison.OrdinalIgnoreCase));
        var id = known.Key ?? AdHocId(dir);
        return ToEntry(id, path, SaveContainer.Summarize(path));
    }

    public static string AdHocId(string dir) =>
        "path-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..8].ToLowerInvariant();

    /// <summary>The folders a service needs to open <paramref name="entry"/>: the workspace's, plus the entry's own when it is outside them.</summary>
    public static Dictionary<string, string> DirsFor(ZcWorkspace ws, CatalogEntry entry)
    {
        var dirs = new Dictionary<string, string>(ws.SaveDirs);
        if (!dirs.ContainsKey(entry.DirId)) dirs[entry.DirId] = System.IO.Path.GetDirectoryName(entry.Path)!;
        return dirs;
    }
}
