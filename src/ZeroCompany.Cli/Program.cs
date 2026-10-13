using ZeroCompany.Cli;
using ZeroCompany.Core.Gvas;

const string Usage = """
    zcsave - read-only profiling of Star Wars Zero Company saves. It never writes to a save.

      zcsave list                          saves in the game's folder, newest first, with their comments
      zcsave profile <save> [-v]           what the save is and what is in it (teams, units, objectives, waves, scenery)
      zcsave diff <a> <b> [--all] [--top N]  what changed between two saves (units first, then changed values by path)
      zcsave find <save> <text> [--limit N]  properties whose name or value contains the text, with their paths
      zcsave dump <save> [path] [--depth N] [--take N]  property tree below a path, e.g.
                                           TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos/[6]

    <save> is a file path, a file-name fragment ("Brentaal"), a save comment ("COMBAT BASE 4.0"), or "latest".
    """;

try
{
    var a = args.ToList();
    if (a.Count == 0 || a[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }

    bool Flag(string f) { var i = a.FindIndex(x => x == f); if (i < 0) return false; a.RemoveAt(i); return true; }
    int Num(string f, int def)
    {
        var i = a.FindIndex(x => x == f);
        if (i < 0) return def;
        if (i + 1 >= a.Count || !int.TryParse(a[i + 1], out var v)) throw new CliException($"{f} needs a number");
        a.RemoveRange(i, 2); return v;
    }

    var cmd = a[0]; a.RemoveAt(0);
    switch (cmd)
    {
        case "list":
            foreach (var f in SaveFile.Candidates())
            {
                string comment;
                try { comment = SaveFile.Open(f).Comment; } catch (Exception e) when (e is CliException or IOException) { comment = "?"; }
                Console.WriteLine($"{File.GetLastWriteTime(f):MM-dd HH:mm:ss}  {Path.GetFileName(f),-62} {comment}");
            }
            return 0;
        case "profile":
        {
            bool v = Flag("-v") | Flag("--verbose");
            Profile.Run(SaveFile.Open(Arg(a, 0, "profile <save>")), v);
            return 0;
        }
        case "diff":
        {
            bool all = Flag("--all"); int top = Num("--top", 40);
            Inspect.Diff(SaveFile.Open(Arg(a, 0, "diff <a> <b>")), SaveFile.Open(Arg(a, 1, "diff <a> <b>")), all, top);
            return 0;
        }
        case "find":
        {
            int limit = Num("--limit", 60);
            Inspect.Find(SaveFile.Open(Arg(a, 0, "find <save> <text>")), Arg(a, 1, "find <save> <text>"), limit);
            return 0;
        }
        case "dump":
        {
            int depth = Num("--depth", 2), take = Num("--take", 40);
            Inspect.Dump(SaveFile.Open(Arg(a, 0, "dump <save> [path]")), a.Count > 1 ? a[1] : "", depth, take);
            return 0;
        }
        default:
            throw new CliException($"unknown command '{cmd}' (zcsave --help)");
    }
}
catch (Exception e) when (e is CliException or GvasException or IOException or InvalidDataException)
{
    Console.Error.WriteLine("zcsave: " + e.Message);
    return 1;
}

static string Arg(List<string> a, int i, string usage) => i < a.Count ? a[i] : throw new CliException("usage: zcsave " + usage);
