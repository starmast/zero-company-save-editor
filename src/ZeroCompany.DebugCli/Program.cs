using ZeroCompany.DebugCli;
using ZeroCompany.Sdk.Cli;

const string Usage = """
    zcdebug - talk to the running game through the ZcDebug mod (needs UE4SS). Reads by default; changes need --write.

      zcdebug status                          is the game running, is the mod answering
      zcdebug install [--game DIR] [--out DIR]  copy the ZcDebug mod into UE4SS's Mods folder, or write it to DIR to copy by hand
      zcdebug uninstall [--game DIR]          remove it again
      zcdebug fix [--game DIR] [--elevate]    check the game folders the mod and UE4SS write to (Mods, LogicMods), create what it can, and say
                                              what needs Administrator; --elevate runs just those commands through the Windows UAC prompt.
                                              Also adds a game build that a game update made UE4SS's signature pack reject to the pack's
                                              scripts (backed up first; --no-signatures skips it, --undo-signatures restores the backup)
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
      zcdebug load <save name> --write      load a save by file name, no .sav (the game must already list it; see docs); waits until the level
                                            is up (--no-wait returns at once, --load-wait SECONDS gives up after that long, default 180)
      zcdebug loadstatus                    whether the level of the last load is up
      zcdebug log on|off|clear|snap         record the game's own combat events (abilities used, moves, shots, hits with damage, kills, turns, rounds)
                                            to combat.log without polling; snap writes the units' numbers once
      zcdebug log show [n]                  the last n (default 60) lines of that log
      zcdebug funcs <object|Class> [filter]   the functions a live object's class has, with parameter types
      zcdebug call <object|Class> <Function> [args] --write   call one (numbers, true/false, @/object/path, n:Name, text)
      zcdebug trace <Class:Function> [on|off|list] [max] --write   log every call of a function, with its parameters, to combat.log
      zcdebug reload [--from FILE]            run the mod's script again without restarting it (copy FILE over it first) --write
      zcdebug shot [--out FILE]               have the game save a screenshot with the HUD and print its path
      zcdebug api <text> [--limit N]          search the dumped class, struct and function declarations (UE4SS "Dump CXX Headers")
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
        case "fix": return Fix.Run(game, a.Flag("--elevate"), Console.Out, checkSignatures: !a.Flag("--no-signatures"), undoSignatures: a.Flag("--undo-signatures"));
        case "status": return Session.Status(new Bridge());
        case "saves":
        {
            var filter = string.Join(' ', a.TakeAll());
            return savesDir != null
                ? Saves.List(savesDir, filter, all, Saves.GameStart(), Console.Out)
                : Saves.List(a.Workspace, filter, all, Saves.GameStart(), Console.Out);
        }
        case "repl": return Session.Repl(new Bridge(), write, timeout);
        case "reload": return Investigate.Reload(new Bridge(), game, a.Opt("--from"), write, timeout, Console.Out);
        case "shot": return Investigate.Shot(new Bridge(), a.Opt("--out"), timeout, Console.Out);
        case "api":
        {
            var hdr = a.Opt("--headers") ?? Investigate.HeaderDumpDir(game)
                ?? throw new CliException("no CXXHeaderDump folder: in the UE4SS window open Dumpers and press \"Dump CXX Headers\" once (or pass --headers DIR)");
            return Investigate.Api(hdr, string.Join(' ', a.TakeAll()), int.TryParse(a.Opt("--limit"), out var lim) ? lim : 60, Console.Out);
        }
        case "log" when a.First == "show":
            return CombatLog.Show(new Bridge().Dir, string.Join(' ', a.TakeAll()), Console.Out, Console.Error);
        default:
        {
            // `load` returns once the level is up (give up after --load-wait seconds, default 180); --no-wait returns at once
            var noWait = a.Flag("--no-wait");
            var loadWait = TimeSpan.FromSeconds(double.TryParse(a.Opt("--load-wait"), out var lw) ? lw : 180);
            return Session.Run(new Bridge(), cmd, string.Join(' ', a.TakeAll()), write, timeout, waitForLoad: noWait ? null : loadWait);
        }
    }
});
