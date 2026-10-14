using ZeroCompany.DebugCli;

const string Usage = """
    zcdebug - talk to the running game through the ZcDebug mod (needs UE4SS). Reads by default; changes need --write.

      zcdebug status                          is the game running, is the mod answering
      zcdebug install [--game DIR] [--out DIR]  copy the ZcDebug mod into UE4SS's Mods folder, or write it to DIR to copy by hand
      zcdebug uninstall [--game DIR]          remove it again
      zcdebug ping
      zcdebug find <Class> [limit]            live instances of an Unreal class, e.g. Pawn, PlayerController
      zcdebug props <object|Class>            a class's properties (name : type)
      zcdebug get <object|Class> <Path>       read a property (Path like Prop.1.Sub)
      zcdebug dump <object|Class>             every simple property with its value
      zcdebug set <object|Class> <Property> <value> --write     change one property
      zcdebug attr <unit> <Attribute> <n> --write  set a stat (Health, ActionPoints ...) in the game and on the HUD
      zcdebug effects <unit>                  the unit's active gameplay effects (Burning, Spotted ...)
      zcdebug fx <unit> add|remove <Name> [n] --write  add (n stacks) or remove a status effect
      zcdebug exec <console command> --write  run an Unreal console command
      zcdebug eval <lua> --write              run Lua inside the game (UE4SS API: FindAllOf, StaticFindObject ...)
      zcdebug repl [--write]                  the same commands interactively

    <object|Class> is a full object path (has "/" or ".") or a short class name (its first live instance).
    Options: --timeout SECONDS (default 5)
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
        if (i + 1 >= a.Count) throw new CliException($"{f} needs a value");
        var v = a[i + 1]; a.RemoveRange(i, 2); return v;
    }

    bool write = Flag("--write");
    var game = Opt("--game");
    var timeout = TimeSpan.FromSeconds(double.TryParse(Opt("--timeout"), out var t) ? t : 5);
    var cmd = a[0]; a.RemoveAt(0);
    var rest = string.Join(' ', a);

    switch (cmd)
    {
        case "install": Console.WriteLine(Install.Setup(game, uninstall: false, Opt("--out"))); return 0;
        case "uninstall": Console.WriteLine(Install.Setup(game, uninstall: true)); return 0;
        case "status": return Session.Status(new Bridge());
        case "repl": return Session.Repl(new Bridge(), write, timeout);
        default: return Session.Run(new Bridge(), cmd, rest, write, timeout);
    }
}
catch (CliException e)
{
    Console.Error.WriteLine("zcdebug: " + e.Message);
    return 1;
}
