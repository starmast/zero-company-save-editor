using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Local-only game data extractor (reads the install, writes ../../gamedata/*.json).
//   dotnet run -- probe   [--usmap Mappings.usmap]    mount archives, count files, test one recipe
//   dotnet run -- strings                              English text from Game.locres -> gamedata/strings_en.json
// The game folder is only ever read.
var cmd = args.Length > 0 ? args[0] : "probe";
string Opt(string name, string? dflt = null)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : dflt ?? "";
}
var gameDir = Opt("--game", @"C:\Program Files\EA Games\Star Wars Zero Company");
var usmap = Opt("--usmap");
var outDir = Path.GetFullPath(Opt("--out", Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "gamedata")));
Directory.CreateDirectory(outDir);

var provider = Mount(gameDir, usmap);

switch (cmd)
{
    case "probe":
    {
        var names = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
        if (names.Count == 0) names.Add("Upgrade_Crew_FocusPoint_Focus_1");
        foreach (var n in names) Probe(provider, n);
        break;
    }
    case "strings": ExportStrings(provider, outDir); break;
    case "upgrades": ExportUpgrades(provider, outDir); break;
    case "dump": DumpFamily(provider, outDir, args); break;
    case "file":
    {   // file <path substring>...  -> prints the raw text of packaged files (ini/json/txt), read-only
        foreach (var needle in args.Skip(1).TakeWhile(a => !a.StartsWith("--")))
            foreach (var k in provider.Files.Keys.Where(k => k.EndsWith(needle, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("##### " + k);
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(provider.SaveAsset(k)));
            }
        break;
    }
    default: Console.WriteLine($"unknown command {cmd}"); break;
}

static DefaultFileProvider Mount(string gameDir, string usmap)
{
    var paks = Path.Combine(gameDir, "SWZeroCompany", "Content", "Paks");
    var oodle = Path.Combine(AppContext.BaseDirectory, "oo2core_9_win64.dll");
    if (!File.Exists(oodle))
    {
        Console.WriteLine("Fetching Oodle via CUE4Parse...");
        OodleHelper.DownloadOodleDllAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    OodleHelper.Initialize(oodle);
    var p = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly,
        new VersionContainer(EGame.GAME_UE5_6), StringComparer.OrdinalIgnoreCase);
    p.Initialize();
    p.SubmitKey(new FGuid(), new FAesKey(new byte[32]));    // containers are not encrypted
    p.PostMount();
    if (usmap.Length > 0) p.MappingsContainer = new FileUsmapTypeMappingsProvider(usmap);
    Console.WriteLine($"Mounted {p.MountedVfs.Count} containers, {p.Files.Count} files"
                      + (usmap.Length > 0 ? $", mappings: {usmap}" : ", no mappings"));
    return p;
}

static void Probe(DefaultFileProvider p, string needle)
{
    var probe = p.Files.Keys.FirstOrDefault(k => k.EndsWith("/" + needle + ".uasset", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"probe: {probe}");
    if (probe == null) return;
    try
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in p.LoadPackageObjects(probe))
        {
            var cls = o.Class?.Name.Text ?? "";
            if (cls is "BlueprintGeneratedClass" or "SceneComponent" or "SimpleConstructionScript" or "SCS_Node") continue;
            sb.AppendLine(JsonConvert.SerializeObject(o, Formatting.Indented));
        }
        var f = Path.Combine(Path.GetTempPath(), "probe_" + needle + ".json");
        File.WriteAllText(f, sb.ToString());
        Console.WriteLine($"wrote {f} ({sb.Length} chars)");
    }
    catch (Exception ex) { Console.WriteLine($"read failed: {ex.GetType().Name}: {ex.Message}"); }
}

// dump <family> <substring>...   e.g.  dump items /GameData/ItemData/
// Writes gamedata/raw_<family>.json: { assetName: { path, objects:[{Type,Name,Template,Properties}] } }
static void DumpFamily(DefaultFileProvider p, string outDir, string[] a)
{
    Pkg.P = p;
    var rest = a.Skip(1).TakeWhile(x => !x.StartsWith("--")).ToList();
    if (rest.Count < 2) { Console.WriteLine("usage: dump <family> <path substring>..."); return; }
    var family = rest[0]; var subs = rest.Skip(1).ToList();
    var files = p.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                                        && subs.Any(s => k.Contains(s, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(k => k, StringComparer.Ordinal).ToList();
    var root = new JObject();
    int bad = 0;
    foreach (var f in files)
    {
        try
        {
            var pkgPath = "/Game" + f.Substring("SWZeroCompany/Content".Length).Replace(".uasset", "");
            var objs = new JArray();
            foreach (var (name, j) in Pkg.Objects(pkgPath))
            {
                var t = (string?)j["Type"] ?? "";
                if (t is "BlueprintGeneratedClass" or "SceneComponent" or "SimpleConstructionScript" or "SCS_Node") continue;
                var o = new JObject { ["Type"] = t, ["Name"] = name };
                if (j["Template"] is JObject tm && tm["ObjectName"] != null) o["Template"] = tm["ObjectName"];
                if (j["Properties"] != null) o["Properties"] = j["Properties"];
                objs.Add(o);
            }
            foreach (var d in objs.Descendants().OfType<JProperty>().Where(x => x.Name == "ObjectPath").ToList()) d.Remove();
            root[Path.GetFileNameWithoutExtension(f)] = new JObject { ["path"] = pkgPath, ["objects"] = objs };
        }
        catch (Exception ex) { bad++; Console.WriteLine($"  skip {f}: {ex.GetType().Name}: {ex.Message}"); }
    }
    var file = Path.Combine(outDir, $"raw_{family}.json");
    File.WriteAllText(file, root.ToString(Formatting.None));
    Console.WriteLine($"Wrote {file}: {root.Count} assets ({bad} skipped), {new FileInfo(file).Length / 1024} KB");
}

static void ExportUpgrades(DefaultFileProvider p, string outDir)
{
    Pkg.P = p;
    var files = p.Files.Keys.Where(k => k.Contains("/Recipes/Upgrade_Facility/") && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
        .OrderBy(k => k, StringComparer.Ordinal).ToList();
    var list = new JArray();
    foreach (var f in files)
    {
        try
        {
            var pkgPath = "/Game" + f.Substring("SWZeroCompany/Content".Length).Replace(".uasset", "");
            var objs = Pkg.Objects(pkgPath);
            var name = Path.GetFileNameWithoutExtension(f);
            if (!objs.TryGetValue("Default__" + name + "_C", out var cdo)) continue;
            var props = Pkg.Merged(cdo);
            var rec = new JObject
            {
                ["name"] = name,
                ["path"] = pkgPath,
                ["unused"] = pkgPath.Contains("/Unused/"),
                ["title"] = Pkg.S(props["Name"], "SourceString"),
                ["description"] = Pkg.S(props["Description"], "SourceString"),
                ["duration"] = props["Duration"],
                ["recipeTags"] = props["RecipeTags"] is JArray rt ? new JArray(rt.Select(x => Pkg.S(x, "TagName") ?? x)) : new JArray(),
                ["completedFactTags"] = props["CompletedFactTags"] is JArray ct ? new JArray(ct.Select(x => Pkg.S(x, "TagName") ?? x)) : new JArray(),
            };
            var tiers = new JArray();
            foreach (var tier in (props["RewardTiers"] as JArray) ?? new JArray())
            {
                var t = new JObject
                {
                    ["requirements"] = new JArray(((tier["TierRequirements"] as JArray) ?? new JArray()).Select(r => Upg.Describe(Pkg.Resolve(r)))),
                    ["customRewards"] = new JArray(((tier["CustomRewards"] as JArray) ?? new JArray()).Select(r => Upg.Describe(Pkg.Resolve(r)))),
                    ["rewards"] = tier["Rewards"] ?? new JArray(),
                };
                tiers.Add(t);
            }
            rec["tiers"] = tiers;
            list.Add(rec);
        }
        catch (Exception ex) { Console.WriteLine($"  skip {f}: {ex.GetType().Name}: {ex.Message}"); }
    }
    var file = Path.Combine(outDir, "upgrades.json");
    File.WriteAllText(file, list.ToString(Formatting.Indented));
    Console.WriteLine($"Wrote {file}: {list.Count} recipes (of {files.Count} files)");
}

static void ExportStrings(DefaultFileProvider p, string outDir)
{
    const string path = "SWZeroCompany/Content/Localization/Game/en/Game.locres";
    if (!p.TryCreateReader(path, out var ar)) { Console.WriteLine("Game.locres not found"); return; }
    var res = new FTextLocalizationResource(ar);
    // namespace -> key -> text
    var dict = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
    foreach (var (ns, entries) in res.Entries)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, entry) in entries) d[key.Str] = entry.LocalizedString;
        dict[ns.Str] = d;
    }
    var file = Path.Combine(outDir, "strings_en.json");
    File.WriteAllText(file, JsonConvert.SerializeObject(dict, Formatting.Indented));
    Console.WriteLine($"Wrote {file}: {dict.Count} namespaces, {dict.Values.Sum(v => v.Count)} strings");
}


// ---------------------------------------------------------------- upgrades
// Recipes are Blueprint classes: the interesting data is on the class default object (CDO), plus
// requirement/reward sub-objects that may live in this package or the parent recipe's package.
static class Pkg
{
    static readonly Dictionary<string, Dictionary<string, JObject>> cache = new();
    public static DefaultFileProvider P = null!;

    public static Dictionary<string, JObject> Objects(string packagePath)   // "/Game/Game/GameData/X/Y"
    {
        if (cache.TryGetValue(packagePath, out var d)) return d;
        d = new Dictionary<string, JObject>();
        var key = "SWZeroCompany/Content" + packagePath.Substring("/Game".Length) + ".uasset";
        if (P.Files.ContainsKey(key))
            foreach (var o in P.LoadPackageObjects(key))
            {
                var j = JObject.Parse(JsonConvert.SerializeObject(o));
                d[(string)j["Name"]!] = j;
            }
        return cache[packagePath] = d;
    }

    // ObjectName looks like  Class'Outer:Name'  or  Class'Name'
    public static string InnerName(string objectName)
    {
        const char Q = (char)39;                       // single quote
        var q = objectName.IndexOf(Q);
        var inner = q >= 0 ? objectName.Substring(q + 1).TrimEnd(Q) : objectName;
        var c = inner.LastIndexOf(':');
        return c >= 0 ? inner.Substring(c + 1) : inner;
    }

    public static JToken? S(JToken? t, string key) => t is JObject o ? o[key] : null;

    public static JObject? Resolve(JToken? refTok)
    {
        if (refTok == null || refTok.Type != JTokenType.Object) return null;
        var path = (string?)S(refTok, "ObjectPath"); var name = (string?)S(refTok, "ObjectName");
        if (path == null || name == null) return null;
        var pkg = path.Substring(0, path.LastIndexOf('.'));
        return Objects(pkg).TryGetValue(InnerName(name), out var o) ? o : null;
    }

    // Own properties first, then inherit missing ones along the Template chain.
    public static JObject Merged(JObject obj)
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

    public static string Quoted(JToken? refTok)
    {
        var n = (string?)S(refTok, "ObjectName") ?? "";
        const char Q = (char)39;
        var a = n.IndexOf(Q); var b = n.LastIndexOf(Q);
        return a >= 0 && b > a ? n.Substring(a + 1, b - a - 1).Split(':').Last() : n;
    }
}

static class Upg
{
    public static JObject Describe(JObject? o)
    {
        var r = new JObject();
        if (o == null) return r;
        var type = (string?)o["Type"] ?? "";
        var props = Pkg.Merged(o);
        r["type"] = type;
        if (props["InventoryItem"] != null) { r["item"] = Pkg.Quoted(props["InventoryItem"]); }
        if (props["Amount"] != null) r["amount"] = props["Amount"];
        if (props["TargetRosterLevel"] != null) r["rosterLevel"] = props["TargetRosterLevel"];
        var tags = props.SelectTokens("..RequireTags[*]").Select(t => (string)t!).ToArray();
        if (tags.Length > 0) r["requireTags"] = new JArray(tags);
        // keep anything else small and informative
        var rest = new JObject();
        foreach (var kv in props)
            if (kv.Key is not ("InventoryItem" or "Amount" or "TargetRosterLevel" or "Gameplay Tag Required")) rest[kv.Key] = kv.Value;
        if (rest.Count > 0) r["other"] = rest;
        return r;
    }
}

