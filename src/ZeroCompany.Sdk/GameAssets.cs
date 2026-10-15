using CUE4Parse.FileProvider;
using Newtonsoft.Json.Linq;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.Sdk;

/// <summary>
/// The game's own packages, opened read-only: finding an asset by name, dumping it as JSON and searching inside it.
/// Never downloads anything and never writes to the game; needs the extraction to have been run once (for the Oodle library).
/// </summary>
public sealed class GameAssets
{
    public DefaultFileProvider Provider { get; }
    public PackageReader Reader { get; }
    /// <summary>Package paths of every .uasset and .umap, sorted.</summary>
    public IReadOnlyList<string> Packages { get; }

    GameAssets(DefaultFileProvider provider)
    {
        Provider = provider;
        Reader = new PackageReader(provider);
        Packages = PackagePaths(provider.Files.Keys);
    }

    public static GameAssets Open(ZcWorkspace ws, IProgress<string>? log = null, string? gameDir = null)
    {
        try { return new GameAssets(GameExtractor.OpenProvider(ws.ExtractOptions(gameDir), log)); }
        catch (ExtractionException e) { throw new ZcException(e.Message, e); }
    }

    /// <summary>An asset's objects as JSON (<paramref name="merged"/> adds inherited properties), with the long object-path noise removed.</summary>
    public JObject Load(string package, bool merged = false)
    {
        var objs = new JArray();
        foreach (var (name, j) in Reader.Objects(package))
        {
            var o = new JObject { ["Type"] = j["Type"], ["Name"] = name };
            if (j["Template"] is JObject tm && tm["ObjectName"] != null) o["Template"] = tm["ObjectName"];
            if (j["Properties"] != null) o["Properties"] = merged && j["Properties"] is JObject ? Reader.Merged(j) : j["Properties"];
            objs.Add(o);
        }
        var root = new JObject { ["path"] = package, ["objects"] = objs };
        StripNoise(root);
        return root;
    }

    public string Pick(string spec) => Pick(Packages, spec);

    // ---- pure helpers (no game files needed) -------------------------------------------------------

    /// <summary>Package paths of every .uasset and .umap, sorted.</summary>
    public static List<string> PackagePaths(IEnumerable<string> fileKeys) =>
        fileKeys.Where(k => k.StartsWith("SWZeroCompany/Content/", StringComparison.OrdinalIgnoreCase)
                            && (k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)))
                .Select(PackageReader.PackagePath).OrderBy(p => p, StringComparer.Ordinal).ToList();

    /// <summary>
    /// The one asset a name means: an exact package path or file name wins; otherwise a substring must match exactly one.
    /// Throws with the candidates when it is ambiguous or matches nothing.
    /// </summary>
    public static string Pick(IReadOnlyList<string> packages, string spec)
    {
        spec = spec.Trim();
        var exact = packages.Where(p => p.Equals(spec, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0];
        var byName = packages.Where(p => p.EndsWith("/" + spec, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1) return byName[0];
        var like = packages.Where(p => p.Contains(spec, StringComparison.OrdinalIgnoreCase)).ToList();
        if (like.Count == 1) return like[0];
        if (like.Count == 0) throw new ZcException($"no asset matches '{spec}' (try: zc assets find {spec})");
        throw new ZcException($"'{spec}' matches {like.Count} assets; be more specific:\n  " + string.Join("\n  ", like.Take(15)) + (like.Count > 15 ? "\n  ..." : ""));
    }

    /// <summary>JSON paths (and values) in the asset whose property name or string value contains the text.</summary>
    public static List<(string Path, string Value)> Grep(JContainer root, string text)
    {
        var hits = new List<(string, string)>();
        foreach (var t in root.DescendantsAndSelf())
        {
            if (t is JProperty p && p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) hits.Add((p.Path, Brief(p.Value)));
            else if (t is JValue v && v.Type == JTokenType.String && ((string?)v.Value ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) && v.Parent is not JProperty { Name: "ObjectPath" })
                hits.Add((v.Path, (string)v.Value!));
        }
        return hits;
    }

    static string Brief(JToken t)
    {
        if (t is JValue v) return v.ToString();
        var s = t.ToString(Newtonsoft.Json.Formatting.None);
        return s.Length > 160 ? s[..160] + "..." : s;
    }

    /// <summary>Drop the long object-path noise that every reference carries, so a dump stays readable.</summary>
    public static void StripNoise(JContainer root)
    {
        foreach (var d in root.Descendants().OfType<JProperty>().Where(x => x.Name == "ObjectPath").ToList()) d.Remove();
    }
}
