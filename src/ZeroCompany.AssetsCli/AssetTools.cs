using Newtonsoft.Json.Linq;

namespace ZeroCompany.AssetsCli;

/// <summary>The pure parts of zcassets (no game files needed): picking an asset by name and searching a dumped asset.</summary>
public static class AssetTools
{
    /// <summary>"SWZeroCompany/Content/Game/X/Y.uasset" -> "/Game/Game/X/Y".</summary>
    public static string PackagePath(string fileKey) =>
        "/Game" + fileKey["SWZeroCompany/Content".Length..].Replace(".uasset", "", StringComparison.OrdinalIgnoreCase).Replace(".umap", "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Package paths of every .uasset, sorted.</summary>
    public static List<string> PackagePaths(IEnumerable<string> fileKeys) =>
        fileKeys.Where(k => k.StartsWith("SWZeroCompany/Content/", StringComparison.OrdinalIgnoreCase) && (k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)))
                .Select(PackagePath).OrderBy(p => p, StringComparer.Ordinal).ToList();

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
        if (like.Count == 0) throw new ArgumentException($"no asset matches '{spec}' (try: zcassets find {spec})");
        throw new ArgumentException($"'{spec}' matches {like.Count} assets; be more specific:\n  " + string.Join("\n  ", like.Take(15)) + (like.Count > 15 ? "\n  ..." : ""));
    }

    /// <summary>JSON paths (and values) in <paramref name="root"/> whose property name or string value contains <paramref name="text"/>.</summary>
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
