using ZeroCompany.Cli;
using ZeroCompany.Sdk;
using ZeroCompany.Sdk.Cli;

const string Usage = """
    zc - tools for Star Wars Zero Company saves and game data

      zc doctor                what is set up and what is missing, with the fix for each
      zc save   <command>      look inside saves (read-only): list, profile, diff, find, dump, check
      zc edit   <command>      change a save from the command line (nothing is written without --write)
      zc assets <command>      browse the game's own packages (read-only)
      zc data   <command>      the game database: extract it, list the names zc edit accepts

    Run `zc <group> --help` for the commands in a group.
    Everywhere: --json for machine-readable output, --data-dir DIR to use another settings/backups/game-data folder
    (or set ZC_DATA_DIR). Exit codes: 0 done, 1 error (nothing written), 2 refused (the game is running, or a file changed).
    """;

return CliApp.Run("zc", Usage, args, a =>
{
    var group = a.Take("zc <command>");
    return group switch
    {
        "doctor" => DataCommands.Doctor(a),
        "save" => SaveCommands.Run(a),
        "edit" => EditCommands.Run(a),
        "assets" => AssetCommands.Run(a),
        "data" => DataCommands.Run(a),
        _ => throw new ZcException($"unknown command '{group}' (zc --help)"),
    };
});
