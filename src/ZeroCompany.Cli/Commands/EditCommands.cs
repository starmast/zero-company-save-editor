using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;
using ZeroCompany.Sdk.Cli;

namespace ZeroCompany.Cli;

/// <summary>
/// `zc edit ...`: change a save from the command line, through the same checked write the editor uses (backup, game-running refusal,
/// changed-on-disk check, verified rebuild). Nothing is written without --write.
/// </summary>
static class EditCommands
{
    public const string Usage = """
        zc edit - change a save from the command line. Nothing is written without --write; without it you get the list of changes.

          zc edit fields <save> [filter]            every editable value: path, value, range
          zc edit get <save> <path>...              current values
          zc edit set <save> <path>=<value>...      change values (linked fields follow, as in the editor)
          zc edit do <save> <action> [args...]      one structural edit, e.g.  heal Neiya   add-item Medkit 3   unit-dead 4 false
          zc edit actions <save>                    the actions that save accepts, with their arguments
          zc edit apply <script.json | -> [<save>]  a batch ({"set": {...}, "actions": [...]}) written in one go
          zc edit diff <a> <b>                      what changed between two saves, as field paths
          zc edit backups <save>                    the editor's backups of a save
          zc edit restore <save> <backup>           put a backup back (the current file is backed up first)

        Options for set / do / apply / restore:
          --write        really write (without it: preview only, exit 0)
          --copy         write a copy next to the save instead of changing it; --as NAME gives the copy its in-game name
          --force        write even though the game appears to be running
        --json prints machine-readable results. Exit codes: 0 done or previewed, 1 error (nothing written), 2 refused (game running, file changed).

        <path> is a dotted name from `zc edit fields`, e.g. resource.Intel, operator.neiya.focus, unit.3.attr.Health (a raw id such as o2897 works too).
        <save> is a file path, a name fragment, a save name, or "latest".
        """;

    sealed record Options(bool Write, bool Copy, string? CopyName, bool Force);

    public static int Run(CliArgs a)
    {
        if (a.Count == 0 || a.First is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
        var cmd = a.Take("zc edit <command>");
        var o = new Options(a.Flag("--write"), a.Flag("--copy"), a.Opt("--as"), a.Flag("--force"));
        switch (cmd)
        {
            case "fields": return Fields(a);
            case "get": return Get(a);
            case "set": return Set(a, o);
            case "do": return Do(a, o);
            case "actions": return Actions(a);
            case "apply": return ApplyScript(a, o);
            case "diff": return Diff(a);
            case "backups": return Backups(a);
            case "restore": return Restore(a, o);
            default: throw new ZcException($"unknown command 'zc edit {cmd}' (zc edit --help)");
        }
    }

    static SaveEditor Open(CliArgs a, string usage) => SaveEditor.Open(a.Workspace, a.Take(usage));

    static int Fields(CliArgs a)
    {
        var ed = Open(a, "zc edit fields <save> [filter]");
        var found = ed.Fields.Search(a.TakeOptional() ?? "").ToList();
        if (a.Json) { CliApp.WriteJson(found.Select(e => new { e.Path, e.Id, e.Label, e.Value, e.Min, e.Max, e.Kind })); return 0; }
        foreach (var e in found)
            Console.WriteLine($"{e.Path,-52} {SaveEditor.Fmt(e.Value),12}  [{Range(e)}]  {e.Label}");
        Console.WriteLine($"{found.Count} value(s) in {ed.Entry.Name}");
        return 0;
    }

    static string Range(FieldEntry e) => (e.Min <= -1e15 ? "any" : SaveEditor.Fmt(e.Min)) + ".." + (e.Max >= 1e15 ? "any" : SaveEditor.Fmt(e.Max));

    static int Get(CliArgs a)
    {
        var ed = Open(a, "zc edit get <save> <path>...");
        var paths = a.TakeAll();
        if (paths.Count == 0) throw new ZcException("usage: zc edit get <save> <path>...");
        var found = paths.Select(p => ed.Fields.Resolve(p)).ToList();
        if (a.Json) CliApp.WriteJson(found.Select(e => new { e.Path, e.Id, e.Label, e.Value, e.Min, e.Max, e.Kind }));
        else foreach (var e in found) Console.WriteLine($"{e.Path} = {SaveEditor.Fmt(e.Value)}  [{Range(e)}]  {e.Label}");
        return 0;
    }

    static int Set(CliArgs a, Options o)
    {
        var ed = Open(a, "zc edit set <save> <path>=<value>...");
        var pairs = a.TakeAll();
        if (pairs.Count == 0) throw new ZcException("usage: zc edit set <save> <path>=<value>...");
        var queued = new List<string>();
        foreach (var pair in pairs)
        {
            int eq = pair.LastIndexOf('=');
            if (eq <= 0) throw new ZcException($"'{pair}' should look like path=value");
            var e = ed.Set(pair[..eq], SaveEditor.ParseNumber(pair[(eq + 1)..]));
            queued.Add($"set {e.Path} = {pair[(eq + 1)..].Trim()}");
        }
        return Finish(a, ed, queued, o);
    }

    static int Do(CliArgs a, Options o)
    {
        var ed = Open(a, "zc edit do <save> <action> [args...]");
        var action = a.Take("zc edit do <save> <action> [args...]");
        return Finish(a, ed, new List<string> { ed.Do(action, a.TakeAll()) }, o);
    }

    static int Actions(CliArgs a)
    {
        var ed = Open(a, "zc edit actions <save>");
        var list = ed.Actions.Where(x => !x.CombatOnly || ed.IsCombat).ToList();
        if (a.Json) { CliApp.WriteJson(list.Select(x => new { x.Name, x.Usage, x.Summary, x.CombatOnly })); return 0; }
        foreach (var x in list) Console.WriteLine($"{x.Usage,-58} {x.Summary}");
        return 0;
    }

    static int ApplyScript(CliArgs a, Options o)
    {
        var src = a.Take("zc edit apply <script.json | -> [<save>]");
        string json;
        try { json = src == "-" ? Console.In.ReadToEnd() : File.ReadAllText(src); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new ZcException($"cannot read the script: {e.Message}", e); }
        var script = EditScript.Parse(json);
        var spec = a.TakeOptional() ?? script.Save ?? throw new ZcException("the script names no save: add \"save\" to it or pass one after the script");
        var ed = SaveEditor.Open(a.Workspace, spec);
        return Finish(a, ed, script.Queue(ed), o);
    }

    static int Diff(CliArgs a)
    {
        var x = a.Take("zc edit diff <a> <b>"); var y = a.Take("zc edit diff <a> <b>");
        var d = FieldDiff.Compare(a.Workspace, x, y);
        if (a.Json) { CliApp.WriteJson(d); return 0; }
        Console.WriteLine($"A  {d.A}\nB  {d.B}");
        foreach (var n in d.Notes) Console.WriteLine("  " + n);
        foreach (var c in d.Changes)
            Console.WriteLine($"  {c.Path,-52} {(c.From == null ? "(absent)" : SaveEditor.Fmt(c.From.Value)),10} -> {(c.To == null ? "(absent)" : SaveEditor.Fmt(c.To.Value)),-10} {c.Label}");
        Console.WriteLine(d.IsEmpty ? "no differences in the editable values" : $"{d.Changes.Count} changed value(s)");
        return 0;
    }

    static int Backups(CliArgs a)
    {
        var ed = Open(a, "zc edit backups <save>");
        var list = ed.Backups();
        if (a.Json) { CliApp.WriteJson(list); return 0; }
        foreach (var b in list) Console.WriteLine($"{b.MTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {b.Size / 1024,7:N0} KB  {b.File}{(b.Original ? "  (original)" : "")}");
        if (list.Count == 0) Console.WriteLine("no backups yet");
        return 0;
    }

    static int Restore(CliArgs a, Options o)
    {
        var ed = Open(a, "zc edit restore <save> <backup>");
        var file = a.Take("zc edit restore <save> <backup>");
        if (!ed.Backups().Any(b => b.File == file)) throw new ZcException($"no backup '{file}' (see: zc edit backups {ed.Entry.Name})");
        if (!o.Write)
        {
            Out(a, new { written = false, restore = file, save = ed.Entry.Name }, $"would restore {file} over {ed.Entry.Name} (preview only; add --write)");
            return 0;
        }
        var (restored, previous) = ed.Restore(file, o.Force);
        Out(a, new { written = true, restored, previousSavedAs = previous, save = ed.Entry.Name }, $"restored {restored}; the file it replaced is kept as {previous}");
        return 0;
    }

    /// <summary>Show what is pending and, with --write, write it.</summary>
    static int Finish(CliArgs a, SaveEditor ed, List<string> queued, Options o)
    {
        var changes = ed.Preview();
        if (!o.Write)
        {
            if (a.Json) CliApp.WriteJson(new { written = false, save = ed.Entry.Name, queued, changes });
            else
            {
                Console.WriteLine($"{ed.Entry.Name}: {changes.Count} change(s) pending");
                foreach (var c in changes) Console.WriteLine("  " + Line(c));
                Console.WriteLine("preview only; add --write to apply" + (o.Copy ? " (as a copy)" : ""));
            }
            return 0;
        }
        var res = ed.Apply(o.Copy, o.CopyName, o.Force);
        if (a.Json) CliApp.WriteJson(new { written = true, save = ed.Entry.Name, file = res.Written, copy = res.Copy, count = res.Count, backup = res.Backup, queued, changes });
        else
        {
            foreach (var c in changes) Console.WriteLine("  " + Line(c));
            Console.WriteLine($"wrote {res.Written}{(res.Copy ? " (a copy; the original is untouched)" : "")}{(res.Backup != null ? $"; backup {res.Backup}" : "")}");
        }
        return 0;
    }

    static string Line(PendingLine c) =>
        c.Text != null ? $"[{c.Where}] {c.Text}"
        : c.From != null && c.To != null ? $"[{c.Where}] {c.Label}: {SaveEditor.Fmt(c.From.Value)} -> {SaveEditor.Fmt(c.To.Value)}"
        : $"[{c.Where}] {c.Label}";

    static void Out(CliArgs a, object json, string text) { if (a.Json) CliApp.WriteJson(json); else Console.WriteLine(text); }
}
