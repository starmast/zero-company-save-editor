using Newtonsoft.Json;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.Sdk;
using ZeroCompany.Sdk.Cli;

namespace ZeroCompany.Cli;

/// <summary>`zc assets ...`: browse the game's own packages (read-only, never downloads, never writes to the game).</summary>
static class AssetCommands
{
    public const string Usage = """
        zc assets - browse the game's own assets (read-only). Uses the game folder, mappings file and Oodle library the editor
        already knows (run `zc data extract` or the editor's extraction once first). Nothing is written to the game or downloaded.

          zc assets find <text> [--limit N]            asset package paths containing the text, e.g. TurnStructure, SpawnSet
          zc assets dump <asset> [--grep TEXT] [--merged]
                                                       an asset's objects as JSON (--grep: only the properties mentioning TEXT)
          zc assets scan <path-part> <text> [--limit N] search inside every asset whose path contains <path-part> for TEXT
          zc assets missions [path-part] [--json]      every mission's in-game name, place, difficulty and faction
          zc assets operations [path-part] [--json]    the Holotable operations: name, cycles on offer, options
          zc assets rewards [path-part] [--json]       inventory-item rewards of missions, grouped by the player choice that unlocks them

        <asset> is a package path (/Game/Game/Core/...), a file name, or a part of the path that matches exactly one asset.
        Examples:
          zc assets find BP_TurnStructure
          zc assets dump BP_TurnStructure_ThreeWayBattle
          zc assets scan /Blueprints/Objects/TurnStructures Enemy2
        The output is the game's own content: use it to read, and never publish or commit dumps.
        """;

    public static int Run(CliArgs a)
    {
        if (a.Count == 0 || a.First is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
        int limit = a.Num("--limit", 60);
        var grep = a.Opt("--grep");
        bool merged = a.Flag("--merged");
        var cmd = a.Take("zc assets <command>");
        if (cmd is not ("find" or "dump" or "scan" or "rewards" or "missions" or "operations")) throw new ZcException($"unknown command 'zc assets {cmd}' (zc assets --help)");
        bool optionalArg = cmd is "rewards" or "missions" or "operations";
        if ((a.Count == 0 && !optionalArg) || (cmd == "scan" && a.Count < 2))
            throw new ZcException($"usage: zc assets {(cmd == "scan" ? "scan <path-part> <text>" : cmd + " <text>")}");

        var log = new Progress<string>(m => Console.Error.WriteLine(m));
        var assets = GameAssets.Open(a.Workspace, log);
        var reader = assets.Reader;
        var packages = assets.Packages;
        bool asJson = a.Json;
        var part = a.Count > 0 ? a.Rest[0] : "";

        switch (cmd)
        {
            case "operations":
            {
                var all = OperationReader.Read(reader, log, default);
                if (asJson) { Console.WriteLine(all.ToString(Formatting.Indented)); return 0; }
                foreach (var o in all.Where(o => ((string)o["recipe"]!).Contains(part, StringComparison.OrdinalIgnoreCase)))
                    Console.WriteLine($"{o["recipe"],-34} \"{o["name"]}\"  {o["kind"]}  on offer {o["expiration"]}  options: {string.Join(" / ", o["choices"]!.Select(c => (string?)c))}");
                return 0;
            }
            case "missions":
            {
                var all = MissionReader.Read(reader, log, default);
                if (asJson) { Console.WriteLine(all.ToString(Formatting.Indented)); return 0; }
                foreach (var m in all.Where(m => ((string)m["asset"]!).Contains(part, StringComparison.OrdinalIgnoreCase)))
                    Console.WriteLine($"{m["asset"],-34} \"{m["name"]}\"  {m["location"]} ({m["region"]})  {m["difficulty"]} vs {m["faction"]}  squad {m["minSquad"]}-{m["maxSquad"]}  lasts {m["lifespan"]}");
                return 0;
            }
            case "rewards":
            {
                var all = RewardReader.Read(reader, log, default);
                if (asJson) { Console.WriteLine(all.ToString(Formatting.Indented)); return 0; }
                foreach (var r in all.Where(r => ((string)r["recipe"]!).Contains(part, StringComparison.OrdinalIgnoreCase)))
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
                var text = string.Join(' ', a.TakeAll());
                var hits = packages.Where(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var p in hits.Take(limit)) Console.WriteLine(p);
                Console.WriteLine(hits.Count > limit ? $"... {hits.Count - limit} more ({hits.Count} total; use --limit)" : $"{hits.Count} asset(s) of {packages.Count}");
                return 0;
            }
            case "dump":
            {
                var pkg = assets.Pick(string.Join(' ', a.TakeAll()));
                var root = assets.Load(pkg, merged);
                if (grep == null) { Console.WriteLine(root.ToString(Formatting.Indented)); return 0; }
                var hits = GameAssets.Grep(root, grep);
                Console.WriteLine($"{pkg}: {hits.Count} match(es) for '{grep}'");
                foreach (var (path, value) in hits.Take(limit)) Console.WriteLine($"  {path} = {value}");
                return 0;
            }
            default:   // scan
            {
                var all = a.TakeAll();
                var partText = all[0]; var text = string.Join(' ', all.Skip(1));
                var candidates = packages.Where(p => p.Contains(partText, StringComparison.OrdinalIgnoreCase)).ToList();
                Console.Error.WriteLine($"searching {candidates.Count} asset(s) under '{partText}'...");
                int shown = 0, found = 0, failed = 0;
                foreach (var p in candidates)
                {
                    if (shown >= limit) { Console.WriteLine($"... stopped at {limit} matches (use --limit)"); break; }
                    try
                    {
                        var hits = GameAssets.Grep(assets.Load(p, merged), text);
                        if (hits.Count == 0) continue;
                        found++;
                        Console.WriteLine(p);
                        foreach (var (path, value) in hits.Take(5)) { Console.WriteLine($"    {path} = {value}"); shown++; }
                        if (hits.Count > 5) Console.WriteLine($"    ... {hits.Count - 5} more in this asset");
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { failed++; }
                }
                Console.WriteLine($"{found} asset(s) mention '{text}'{(failed > 0 ? $" ({failed} could not be read)" : "")}");
                return 0;
            }
        }
    }
}
