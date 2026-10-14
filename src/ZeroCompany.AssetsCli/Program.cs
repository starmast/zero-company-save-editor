using CUE4Parse.FileProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZeroCompany.AssetsCli;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Extractor;

const string Usage = """
    zcassets - browse the game's own assets (read-only). Uses the game folder, mappings file and Oodle library the editor
    already knows (run the extraction once in the editor first). Nothing is written to the game, and nothing is downloaded.

      zcassets find <text> [--limit N]            asset package paths containing the text, e.g. TurnStructure, SpawnSet
      zcassets dump <asset> [--grep TEXT] [--merged]
                                                  an asset's objects as JSON (--grep: only the properties mentioning TEXT)
      zcassets scan <path-part> <text> [--limit N] search inside every asset whose path contains <path-part> for TEXT
      zcassets missions [path-part] [--json]      every mission's in-game name, place, difficulty and faction (--json: raw, for the editor's dev dumps)
      zcassets rewards [path-part] [--json]       inventory-item rewards of missions, grouped by the player choice that unlocks them (--json: raw, for the editor's dev dumps)

    <asset> is a package path (/Game/Game/Core/...), a file name, or a part of the path that matches exactly one asset.
    Examples:
      zcassets find BP_TurnStructure
      zcassets dump BP_TurnStructure_ThreeWayBattle
      zcassets scan /Blueprints/Objects/TurnStructures Enemy2
    """;

try
{
    var a = args.ToList();
    if (a.Count == 0 || a[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
    bool Flag(string f) { var i = a.IndexOf(f); if (i < 0) return false; a.RemoveAt(i); return true; }
    string? Opt(string f)
    {
        var i = a.IndexOf(f);
        if (i < 0) return null;
        if (i + 1 >= a.Count) throw new ArgumentException($"{f} needs a value");
        var v = a[i + 1]; a.RemoveRange(i, 2); return v;
    }
    int limit = int.TryParse(Opt("--limit"), out var l) ? l : 60;
    var grep = Opt("--grep");
    bool merged = Flag("--merged");
    bool asJson = Flag("--json");
    var cmd = a[0]; a.RemoveAt(0);
    if (cmd is not ("find" or "dump" or "scan" or "rewards" or "missions")) throw new ArgumentException($"unknown command '{cmd}' (zcassets --help)");
    if ((a.Count == 0 && cmd is not ("rewards" or "missions")) || (cmd == "scan" && a.Count < 2)) throw new ArgumentException($"usage: zcassets {(cmd == "scan" ? "scan <path-part> <text>" : cmd + " <text>")}");

    var env = new AppEnvironment();
    var s = AppSettings.Load(env);
    var opt = new ExtractOptions
    {
        GameDir = string.IsNullOrEmpty(s.GameDir) ? GameExtractor.DefaultGameDir : s.GameDir,
        UsmapPath = s.UsmapPath,
        OodleDir = string.IsNullOrEmpty(s.OodleDir) ? env.DataDir : s.OodleDir,
    };
    var log = new Progress<string>(m => Console.Error.WriteLine(m));
    var provider = GameExtractor.OpenProvider(opt, log);
    var packages = AssetTools.PackagePaths(provider.Files.Keys);
    var reader = new PackageReader(provider);

    JObject Load(string package)
    {
        var objs = new JArray();
        foreach (var (name, j) in reader.Objects(package))
        {
            var o = new JObject { ["Type"] = j["Type"], ["Name"] = name };
            if (j["Template"] is JObject tm && tm["ObjectName"] != null) o["Template"] = tm["ObjectName"];
            if (j["Properties"] != null) o["Properties"] = merged && j["Properties"] is JObject ? reader.Merged(j) : j["Properties"];
            objs.Add(o);
        }
        var root = new JObject { ["path"] = package, ["objects"] = objs };
        AssetTools.StripNoise(root);
        return root;
    }

    switch (cmd)
    {
        case "missions":
        {
            var part = a.Count > 0 ? a[0] : "";
            var all = MissionReader.Read(reader, log, default);
            if (asJson) { Console.WriteLine(all.ToString(Formatting.Indented)); return 0; }
            foreach (var m in all.Where(m => ((string)m["asset"]!).Contains(part, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"{m["asset"],-34} \"{m["name"]}\"  {m["location"]} ({m["region"]})  {m["difficulty"]} vs {m["faction"]}  squad {m["minSquad"]}-{m["maxSquad"]}  lasts {m["lifespan"]}");
            return 0;
        }
        case "rewards":
        {
            var part = a.Count > 0 ? a[0] : "";
            if (asJson) { Console.WriteLine(RewardReader.Read(reader, log, default).ToString(Formatting.Indented)); return 0; }
            foreach (var r in RewardReader.Read(reader, log, default).Where(r => ((string)r["recipe"]!).Contains(part, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"{r["recipe"]}  \"{r["title"]}\"");
                foreach (var tier in r["tiers"]!)
                {
                    var choices = tier["choices"]!.Select(c => (string)c!).ToList();
                    var items = string.Join(", ", tier["items"]!.Select(i => $"{i["item"]} x{i["amount"]}"));
                    Console.WriteLine($"    {(choices.Count == 0 ? "always" : "choice: " + string.Join(" + ", choices))}  ->  {items}");
                }
            }
            return 0;
        }
        case "find":
        {
            var text = string.Join(' ', a);
            var hits = packages.Where(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var p in hits.Take(limit)) Console.WriteLine(p);
            Console.WriteLine(hits.Count > limit ? $"... {hits.Count - limit} more ({hits.Count} total; use --limit)" : $"{hits.Count} asset(s) of {packages.Count}");
            return 0;
        }
        case "dump":
        {
            var pkg = AssetTools.Pick(packages, string.Join(' ', a));
            var root = Load(pkg);
            if (grep == null) { Console.WriteLine(root.ToString(Formatting.Indented)); return 0; }
            var hits = AssetTools.Grep(root, grep);
            Console.WriteLine($"{pkg}: {hits.Count} match(es) for '{grep}'");
            foreach (var (path, value) in hits.Take(limit)) Console.WriteLine($"  {path} = {value}");
            return 0;
        }
        default:   // scan
        {
            var part = a[0]; var text = string.Join(' ', a.Skip(1));
            var candidates = packages.Where(p => p.Contains(part, StringComparison.OrdinalIgnoreCase)).ToList();
            Console.Error.WriteLine($"searching {candidates.Count} asset(s) under '{part}'...");
            int shown = 0, assets = 0, failed = 0;
            foreach (var p in candidates)
            {
                if (shown >= limit) { Console.WriteLine($"... stopped at {limit} matches (use --limit)"); break; }
                try
                {
                    var hits = AssetTools.Grep(Load(p), text);
                    if (hits.Count == 0) continue;
                    assets++;
                    Console.WriteLine(p);
                    foreach (var (path, value) in hits.Take(5)) { Console.WriteLine($"    {path} = {value}"); shown++; }
                    if (hits.Count > 5) Console.WriteLine($"    ... {hits.Count - 5} more in this asset");
                }
                catch (Exception e) when (e is not OperationCanceledException) { failed++; }
            }
            Console.WriteLine($"{assets} asset(s) mention '{text}'{(failed > 0 ? $" ({failed} could not be read)" : "")}");
            return 0;
        }
    }
}
catch (Exception e) when (e is ArgumentException or ExtractionException)
{
    Console.Error.WriteLine("zcassets: " + e.Message);
    return 1;
}
