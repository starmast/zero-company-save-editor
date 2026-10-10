using CUE4Parse.FileProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZeroCompany.GameData.Extractor;

/// <summary>
/// Loads packages through CUE4Parse and exposes them as JSON object graphs. Blueprint data lives on class
/// default objects (CDOs) plus sub-objects that may sit in the same or a parent package, so lookups
/// follow object references and Template chains.
/// </summary>
public sealed class PackageReader
{
    readonly DefaultFileProvider _p;
    readonly Dictionary<string, Dictionary<string, JObject>> _cache = new();

    public PackageReader(DefaultFileProvider provider) { _p = provider; }

    public IEnumerable<string> AssetKeys(Func<string, bool> filter) =>
        _p.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && filter(k))
                     .OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>"SWZeroCompany/Content/Game/X/Y.uasset" -> "/Game/Game/X/Y".</summary>
    public static string PackagePath(string fileKey) =>
        "/Game" + fileKey["SWZeroCompany/Content".Length..].Replace(".uasset", "");

    /// <summary>Objects of a package by name ("/Game/Game/GameData/X/Y"); empty when it does not exist.</summary>
    public Dictionary<string, JObject> Objects(string packagePath)
    {
        if (_cache.TryGetValue(packagePath, out var d)) return d;
        d = new Dictionary<string, JObject>();
        var key = "SWZeroCompany/Content" + packagePath["/Game".Length..] + ".uasset";
        if (_p.Files.ContainsKey(key))
            foreach (var o in _p.LoadPackageObjects(key))
            {
                var j = JObject.Parse(JsonConvert.SerializeObject(o));
                d[(string)j["Name"]!] = j;
            }
        return _cache[packagePath] = d;
    }

    /// <summary>ObjectName looks like <c>Class'Outer:Name'</c> or <c>Class'Name'</c>; returns Name.</summary>
    public static string InnerName(string objectName)
    {
        const char Q = '\'';
        var q = objectName.IndexOf(Q);
        var inner = q >= 0 ? objectName[(q + 1)..].TrimEnd(Q) : objectName;
        var c = inner.LastIndexOf(':');
        return c >= 0 ? inner[(c + 1)..] : inner;
    }

    public static JToken? S(JToken? t, string key) => t is JObject o ? o[key] : null;

    public JObject? Resolve(JToken? refTok)
    {
        if (refTok == null || refTok.Type != JTokenType.Object) return null;
        var path = (string?)S(refTok, "ObjectPath");
        var name = (string?)S(refTok, "ObjectName");
        if (path == null || name == null) return null;
        var pkg = path[..path.LastIndexOf('.')];
        return Objects(pkg).TryGetValue(InnerName(name), out var o) ? o : null;
    }

    /// <summary>Own properties first, then inherit missing ones along the Template chain.</summary>
    public JObject Merged(JObject obj)
    {
        var props = obj["Properties"] is JObject pr ? (JObject)pr.DeepClone() : new JObject();
        var t = Resolve(obj["Template"]);
        for (int i = 0; t != null && i < 8; i++)
        {
            if (t["Properties"] is JObject tp)
                foreach (var kv in tp) if (!props.ContainsKey(kv.Key)) props[kv.Key] = kv.Value!.DeepClone();
            t = Resolve(t["Template"]);
        }
        return props;
    }

    /// <summary>The quoted asset name inside an ObjectName reference.</summary>
    public static string Quoted(JToken? refTok)
    {
        var n = (string?)S(refTok, "ObjectName") ?? "";
        var a = n.IndexOf('\''); var b = n.LastIndexOf('\'');
        return a >= 0 && b > a ? n.Substring(a + 1, b - a - 1).Split(':').Last() : n;
    }

    static readonly HashSet<string> SkippedTypes = new() { "BlueprintGeneratedClass", "SceneComponent", "SimpleConstructionScript", "SCS_Node" };

    /// <summary>
    /// All packages whose path contains one of <paramref name="subs"/>:
    /// <c>{ assetName: { path, objects:[{Type, Name, Template, Properties}] } }</c>.
    /// </summary>
    public JObject DumpFamily(string[] subs, IProgress<string> log, CancellationToken ct)
    {
        var files = AssetKeys(k => subs.Any(s => k.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
        var root = new JObject();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var pkgPath = PackagePath(f);
                var objs = new JArray();
                foreach (var (name, j) in Objects(pkgPath))
                {
                    var t = (string?)j["Type"] ?? "";
                    if (SkippedTypes.Contains(t)) continue;
                    var o = new JObject { ["Type"] = t, ["Name"] = name };
                    if (j["Template"] is JObject tm && tm["ObjectName"] != null) o["Template"] = tm["ObjectName"];
                    if (j["Properties"] != null) o["Properties"] = j["Properties"];
                    objs.Add(o);
                }
                foreach (var d in objs.Descendants().OfType<JProperty>().Where(x => x.Name == "ObjectPath").ToList()) d.Remove();
                root[Path.GetFileNameWithoutExtension(f)] = new JObject { ["path"] = pkgPath, ["objects"] = objs };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Report($"  skipped {f}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return root;
    }
}

/// <summary>Reads the facility/crew/weapon upgrade recipes (Blueprint class default objects).</summary>
public sealed class RecipeReader
{
    readonly PackageReader _pkg;
    public RecipeReader(PackageReader pkg) { _pkg = pkg; }

    public JArray Read(IProgress<string> log, CancellationToken ct)
    {
        var files = _pkg.AssetKeys(k => k.Contains("/Recipes/Upgrade_Facility/")).ToList();
        var list = new JArray();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var pkgPath = PackageReader.PackagePath(f);
                var objs = _pkg.Objects(pkgPath);
                var name = Path.GetFileNameWithoutExtension(f);
                if (!objs.TryGetValue("Default__" + name + "_C", out var cdo)) continue;
                var props = _pkg.Merged(cdo);
                var rec = new JObject
                {
                    ["name"] = name,
                    ["path"] = pkgPath,
                    ["unused"] = pkgPath.Contains("/Unused/"),
                    ["title"] = PackageReader.S(props["Name"], "SourceString"),
                    ["description"] = PackageReader.S(props["Description"], "SourceString"),
                    ["duration"] = props["Duration"],
                    ["recipeTags"] = props["RecipeTags"] is JArray rt ? new JArray(rt.Select(x => PackageReader.S(x, "TagName") ?? x)) : new JArray(),
                    ["completedFactTags"] = props["CompletedFactTags"] is JArray ct2 ? new JArray(ct2.Select(x => PackageReader.S(x, "TagName") ?? x)) : new JArray(),
                };
                var tiers = new JArray();
                foreach (var tier in (props["RewardTiers"] as JArray) ?? new JArray())
                    tiers.Add(new JObject
                    {
                        ["requirements"] = new JArray(((tier["TierRequirements"] as JArray) ?? new JArray()).Select(r => Describe(_pkg.Resolve(r)))),
                    });
                rec["tiers"] = tiers;
                list.Add(rec);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Report($"  skipped {f}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        log.Report($"  {list.Count} recipes (of {files.Count} files)");
        return list;
    }

    /// <summary>What a requirement asks for: an inventory item amount, a roster level or required tags.</summary>
    JObject Describe(JObject? o)
    {
        var r = new JObject();
        if (o == null) return r;
        var props = _pkg.Merged(o);
        if (props["InventoryItem"] != null) r["item"] = PackageReader.Quoted(props["InventoryItem"]);
        if (props["Amount"] != null) r["amount"] = props["Amount"];
        if (props["TargetRosterLevel"] != null) r["rosterLevel"] = props["TargetRosterLevel"];
        var tags = props.SelectTokens("..RequireTags[*]").Select(t => (string)t!).ToArray();
        if (tags.Length > 0) r["requireTags"] = new JArray(tags);
        return r;
    }
}
