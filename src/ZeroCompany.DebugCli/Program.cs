using ZeroCompany.DebugCli;
using ZeroCompany.Sdk.Cli;

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
      zcdebug saves [filter] [--all] [--saves DIR]  the save folders: file names (as load wants them), in-game names, and which the game lists
      zcdebug load <save name> --write      load a save by file name, no .sav (the game must already list it; see docs)
      zcdebug exec <console command> --write  run an Unreal console command
      zcdebug eval <lua> --write              run Lua inside the game (UE4SS API: FindAllOf, StaticFindObject ...)
      zcdebug repl [--write]                  the same commands interactively

    <object|Class> is a full object path (has "/" or ".") or a short class name (its first live instance).
    Options: --timeout SECONDS (default 5)
    """;

return CliApp.Run("zcdebug", Usage, args, a =>
{
    bool write = a.Flag("--write");
    var game = a.Opt("--game");
    var timeout = TimeSpan.FromSeconds(double.TryParse(a.Opt("--timeout"), out var t) ? t : 5);
    bool all = a.Flag("--all");
    var savesDir = a.Opt("--saves");
    var cmd = a.Take("zcdebug <command>");

    switch (cmd)
    {
        case "install": Console.WriteLine(Install.Setup(game, uninstall: false, a.Opt("--out"))); return 0;
        case "uninstall": Console.WriteLine(Install.Setup(game, uninstall: true)); return 0;
        case "status": return Session.Status(new Bridge());
        case "saves":
        {
            var filter = string.Join(' ', a.TakeAll());
            return savesDir != null
                ? Saves.List(savesDir, filter, all, Saves.GameStart(), Console.Out)
                : Saves.List(a.Workspace, filter, all, Saves.GameStart(), Console.Out);
        }
        case "repl": return Session.Repl(new Bridge(), write, timeout);
        default: return Session.Run(new Bridge(), cmd, string.Join(' ', a.TakeAll()), write, timeout);
    }
});
