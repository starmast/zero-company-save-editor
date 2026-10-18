using ZeroCompany.Sdk;
using ZeroCompany.Sdk.Cli;

namespace ZeroCompany.Cli;

/// <summary>`zc data ...` and `zc doctor`: the game database and the health of the setup.</summary>
static class DataCommands
{
    public const string Usage = """
        zc data - the game database (names, costs and descriptions the editor shows) and the names `zc edit` accepts

          zc data extract [--game DIR] [--usmap FILE]   read the game's packages (read-only) and build the database, like the editor's Game data screen
          zc data items|upgrades|effects|coil|spawn-sets [filter]
                                                        names the game defines, as zc edit takes them (e.g. zc data items medkit)

        The game folder and mappings file default to what the editor saved; the first extraction may fetch the Oodle library
        (checked against a pinned hash) like the editor does. --json is available on the lists.
        """;

    public static int Run(CliArgs a)
    {
        if (a.Count == 0 || a.First is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
        var game = a.Opt("--game"); var usmap = a.Opt("--usmap");
        var cmd = a.Take("zc data <command>");
        if (cmd == "extract")
        {
            var log = new Progress<string>(m => Console.Error.WriteLine(m));
            var db = GameDataBuilder.Extract(a.Workspace, log, game, usmap);
            var line = $"{db.Upgrades.Count} upgrades, {db.Items.Count} items, {db.Effects.Count} effects, {db.CoilEffects.Count} Coil upgrades";
            if (a.Json) CliApp.WriteJson(new { extracted = true, path = a.Workspace.Env.GameDbPath, upgrades = db.Upgrades.Count, items = db.Items.Count, effects = db.Effects.Count, coil = db.CoilEffects.Count });
            else Console.WriteLine($"wrote {a.Workspace.Env.GameDbPath}: {line}");
            return 0;
        }
        var names = GameDataLookup.List(a.Workspace.LoadDatabase(), cmd, a.TakeOptional() ?? "");
        if (a.Json) { CliApp.WriteJson(names); return 0; }
        foreach (var n in names) Console.WriteLine($"{n.Id,-48} {n.Label}{(n.Detail.Length > 0 ? "  - " + Clip(n.Detail) : "")}");
        Console.WriteLine($"{names.Count} {cmd}");
        return 0;
    }

    static string Clip(string s) => s.Length > 90 ? s[..90] + "..." : s;

    public static int Doctor(CliArgs a)
    {
        bool report = a.Flag("--report");
        var save = a.Opt("--save"); var bundle = a.Opt("--bundle");
        if (report || bundle != null)
        {
            var path = save == null ? null : SaveCatalog.Resolve(a.Workspace, save).Path;
            var text = DebugReport.Build(a.Workspace, DebugReport.ToolName(), path);
            if (bundle == null) { Console.Write(text); return 0; }
            var holds = DebugReport.WriteBundle(a.Workspace, bundle, text, path);
            Console.WriteLine($"wrote {Path.GetFullPath(bundle)} ({holds})");
            return 0;
        }
        var checks = ZcDoctor.Run(a.Workspace);
        if (a.Json) CliApp.WriteJson(checks);
        else foreach (var c in checks) Report.Print(c);
        return checks.HasFailure() ? 1 : 0;
    }
}
