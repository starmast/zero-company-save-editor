using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Model;
using ZeroCompany.Sdk;
using ZeroCompany.Sdk.Cli;

namespace ZeroCompany.Cli;

/// <summary>`zc save ...`: read-only looks at saves. Nothing here writes.</summary>
static class SaveCommands
{
    public const string Usage = """
        zc save - look inside saves (read-only; it never writes to a save)

          zc save list                           saves in the game's folders, newest first, with their names
          zc save profile <save> [-v]            what the save is and what is in it (teams, units, objectives, waves, scenery)
          zc save diff <a> <b> [--all] [--top N] what changed between two saves (units first, then changed values by path)
          zc save find <save> <text> [--limit N] properties whose name or value contains the text, with their paths
          zc save dump <save> [path] [--depth N] [--take N]
                                                 property tree below a path, e.g. TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos/[6]
          zc save weapons <save>                 each operator's weapon parts, and which weapons have lost their mesh data
          zc save check <save>...                round-trip and consistency checks (exit 1 on any problem)

        <save> is a file path, a file-name fragment ("Brentaal"), a save name ("COMBAT BASE 4.0"), or "latest". --json is available on list and check.
        """;

    public static int Run(CliArgs a)
    {
        if (a.Count == 0 || a.First is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
        var cmd = a.Take("zc save <command>");
        var ws = a.Workspace;
        switch (cmd)
        {
            case "list": return List(a);
            case "profile":
            {
                bool v = a.Flag("-v", "--verbose");
                NoJson(a, cmd);
                SaveProfile.Run(SaveDocument.Open(ws, a.Take("zc save profile <save>")), v);
                return 0;
            }
            case "diff":
            {
                bool all = a.Flag("--all"); int top = a.Num("--top", 40);
                NoJson(a, cmd);
                SaveInspect.Diff(SaveDocument.Open(ws, a.Take("zc save diff <a> <b>")), SaveDocument.Open(ws, a.Take("zc save diff <a> <b>")), all, top);
                return 0;
            }
            case "find":
            {
                int limit = a.Num("--limit", 60);
                NoJson(a, cmd);
                SaveInspect.Find(SaveDocument.Open(ws, a.Take("zc save find <save> <text>")), a.Take("zc save find <save> <text>"), limit);
                return 0;
            }
            case "dump":
            {
                int depth = a.Num("--depth", 2), take = a.Num("--take", 40);
                NoJson(a, cmd);
                var doc = SaveDocument.Open(ws, a.Take("zc save dump <save> [path]"));
                SaveInspect.Dump(doc, a.TakeOptional() ?? "", depth, take);
                return 0;
            }
            case "weapons":
            {
                NoJson(a, cmd);
                Weapons(SaveDocument.Open(ws, a.Take("zc save weapons <save>")));
                return 0;
            }
            case "check": return Check(a);
            default: throw new ZcException($"unknown command 'zc save {cmd}' (zc save --help)");
        }
    }

    static void NoJson(CliArgs a, string cmd) { if (a.Json) throw new ZcException($"--json is not available for 'zc save {cmd}'"); }

    static void Weapons(SaveDocument doc)
    {
        var g = doc.Gvas;
        var model = SaveModel.ForProfile(g);
        var ops = WeaponOps.Operators(g);
        Console.WriteLine($"{ops.Count} operators with a weapon");
        foreach (var guid in ops)
        {
            var w = WeaponOps.Read(g, guid)!;
            var name = MedbayOps.Characters(g).TryGetValue(guid, out var wr) ? Naming.RealName(g, wr) ?? model.NameOf(guid) : guid[..8];
            Console.WriteLine($"  {guid[..8]}  {name,-24} {(w.Class.Length == 0 ? "?" : w.Class),-16} {(w.Broken ? "DAMAGED" : "ok"),-8} {w.Type}");
            if (w.Broken)
            {
                foreach (var p in w.Parts) Console.WriteLine($"      {(p.Fragments == 0 ? "no data" : "ok     ")}  {p.Slot.Replace("br.Customization.Slot.Weapon.", ""),-10} {p.Name}");
                var donors = WeaponOps.Donors(g, guid);
                Console.WriteLine(donors.Count == 0 ? "      no operator to copy a vanilla weapon from" : "      can copy from: " + string.Join(", ", donors.Select(d => d[..8])));
            }
        }
    }

    static int List(CliArgs a)
    {
        var all = SaveCatalog.List(a.Workspace);
        if (a.Json) { CliApp.WriteJson(all.Select(e => new { e.DirId, e.Path, e.Name, e.Comment, e.Mode, e.Modified })); return 0; }
        foreach (var e in all) Console.WriteLine($"{e.Modified:MM-dd HH:mm:ss}  {e.Name,-62} {e.Comment}");
        return 0;
    }

    static int Check(CliArgs a)
    {
        var specs = a.TakeAll();
        if (specs.Count == 0) throw new ZcException("usage: zc save check <save>...");
        bool failed = false;
        var results = new List<object>();
        foreach (var spec in specs)
        {
            var checks = SaveCheck.Run(a.Workspace, spec);
            failed |= checks.HasFailure();
            if (a.Json) { results.Add(new { save = spec, checks }); continue; }
            Console.WriteLine(SaveCatalog.Resolve(a.Workspace, spec).Name);
            foreach (var c in checks) Report.Print(c);
        }
        if (a.Json) CliApp.WriteJson(results);
        return failed ? 1 : 0;
    }
}

/// <summary>One-line rendering of a <see cref="Check"/>.</summary>
static class Report
{
    public static void Print(Check c)
    {
        var tag = c.Status switch { CheckStatus.Ok => "ok  ", CheckStatus.Warn => "warn", _ => "FAIL" };
        Console.WriteLine($"  [{tag}] {c.Name,-18} {c.Detail}");
        if (c.Fix != null && c.Status != CheckStatus.Ok) Console.WriteLine($"         {"",-18} fix: {c.Fix}");
    }
}
