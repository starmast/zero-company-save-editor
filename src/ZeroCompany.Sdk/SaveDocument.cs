using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Sdk;

public sealed record FindHit(string Path, string Value);

/// <summary>A save opened read-only: the container, its main GVAS tree and the header comment. Nothing here writes.</summary>
public sealed class SaveDocument
{
    public string Path { get; }
    public SaveContainer Container { get; }
    public GvasFile Gvas { get; }
    public string Comment { get; }

    SaveDocument(string path, SaveContainer c)
    {
        Path = path; Container = c; Gvas = new GvasFile(c.Gvas);
        Comment = c.Comment ?? "";
    }

    /// <summary>Open by any save spec (see <see cref="SaveCatalog.Resolve"/>).</summary>
    public static SaveDocument Open(ZcWorkspace ws, string spec) => OpenPath(SaveCatalog.Resolve(ws, spec).Path);

    public static SaveDocument OpenPath(string path)
    {
        try { return new SaveDocument(path, SaveContainer.Load(File.ReadAllBytes(path))); }
        catch (Exception e) when (e is GvasException or InvalidDataException or SaveFormatException)
        {
            throw new ZcException(System.IO.Path.GetFileName(path) + ": " + e.Message, e);
        }
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

    /// <summary>Every scalar leaf by path. Array elements are keyed by index, so inserted elements shift later ones (shown as changes).</summary>
    public Dictionary<string, string> Leaves()
    {
        var d = new Dictionary<string, string>();
        void Walk(GvasNode x, string path)
        {
            foreach (var c in x.Children ?? new List<GvasNode>())
            {
                var p = path + "/" + c.Name;
                if (c.Children == null) d[p] = Value(c, 60); else Walk(c, p);
            }
        }
        foreach (var r in Gvas.Root) Walk(r, r.Name);
        return d;
    }

    /// <summary>Properties whose name or string value contains the text, with the path of the property that holds it.</summary>
    public (List<FindHit> Hits, bool Truncated) Find(string text, int limit)
    {
        var hits = new List<FindHit>();
        var cmp = StringComparison.OrdinalIgnoreCase;
        foreach (var node in Gvas.Walk())
        {
            if (hits.Count >= limit) return (hits, true);
            var v = node.Children == null && node.Size <= 4096 ? Value(node, 4096) : "";
            if (node.Name.Contains(text, cmp) || v.Contains(text, cmp))
                hits.Add(new FindHit(string.Join("/", GvasFile.PathOf(node)), v));
        }
        return (hits, false);
    }

    /// <summary>The node at a path such as TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos/[6]; null for the empty path (the roots).</summary>
    public GvasNode? NodeAt(string path)
    {
        IEnumerable<GvasNode> nodes = Gvas.Root;
        GvasNode? at = null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            at = nodes.FirstOrDefault(c => c.Name == part);
            if (at == null)
            {
                var more = nodes.Count() > 12 ? ", ..." : "";
                throw new ZcException($"no property '{part}' (children: {string.Join(", ", nodes.Take(12).Select(c => c.Name))}{more})");
            }
            nodes = at.Children ?? new List<GvasNode>();
        }
        return at;
    }
}
