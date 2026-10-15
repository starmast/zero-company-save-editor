namespace ZeroCompany.DebugCli;

/// <summary>Running commands against the mod: the write guard, status output and the interactive loop.</summary>
public static class Session
{
    /// <summary>Commands that read only. Everything else changes the game and needs --write.</summary>
    public static readonly IReadOnlySet<string> ReadOnly = new HashSet<string> { "ping", "find", "props", "get", "dump", "effects" };
    static readonly IReadOnlySet<string> All = new HashSet<string> { "ping", "find", "props", "get", "dump", "effects", "set", "attr", "fx", "load", "exec", "eval" };

    public static int Status(Bridge b)
    {
        var game = Install.RunningGame();
        Console.WriteLine(game.Count == 0 ? "game      not running" : $"game      running ({string.Join(", ", game.Select(g => $"{g.Name} pid {g.Id}"))})");
        var beat = b.LastHeartbeat();
        if (beat == null) Console.WriteLine("mod       no heartbeat yet (is UE4SS installed and the ZcDebug mod enabled? try `zcdebug install`)");
        else
        {
            var age = DateTimeOffset.UtcNow - beat.Value;
            Console.WriteLine(age.TotalSeconds <= 5 ? $"mod       answering ({b.HeartbeatInfo()})" : $"mod       last seen {age.TotalSeconds:0}s ago ({beat.Value.LocalDateTime:HH:mm:ss}); the game is probably closed");
        }
        Console.WriteLine($"channel   {b.Dir}");
        return b.Connected(TimeSpan.FromSeconds(5)) ? 0 : 2;
    }

    /// <summary>Run one command; prints the answer, returns the process exit code.</summary>
    public static int Run(Bridge b, string command, string args, bool write, TimeSpan timeout, TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out; error ??= Console.Error;
        if (!All.Contains(command)) throw new CliException($"unknown command '{command}' (zcdebug --help)");
        if (!ReadOnly.Contains(command) && !write)
            throw new CliException($"'{command}' changes the running game; add --write if you mean it");
        if (args.Trim().Length == 0 && command is not ("ping"))
            throw new CliException($"'{command}' needs an argument (zcdebug --help)");
        if (!b.Connected(TimeSpan.FromSeconds(10)))
            throw new CliException("the mod is not answering (no recent heartbeat). Run `zcdebug status`.");
        try
        {
            var r = b.Send(command, args, timeout);
            (r.Ok ? output : error).WriteLine(r.Ok ? r.Text : "error: " + r.Text);
            return r.Ok ? 0 : 1;
        }
        catch (TimeoutException e) { throw new CliException(e.Message + ". The game may be paused, loading or busy."); }
    }

    public static int Repl(Bridge b, bool write, TimeSpan timeout)
    {
        Console.WriteLine($"zcdebug repl ({(write ? "writes allowed" : "read only; restart with --write to change the game")}). Type help or quit.");
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) return 0;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "quit" or "exit" or "q") return 0;
            if (line == "help") { Console.WriteLine("ping | find <Class> [n] | props <o> | dump <o> | get <o> <P> | set <o> <P> <v> | attr <unit> <Attr> <n> | load <save> | effects <unit> | fx <unit> add|remove <Name> [n] | exec <cmd> | eval <lua> | status"); continue; }
            if (line == "status") { Status(b); continue; }
            var sp = line.IndexOf(' ');
            var cmd = sp < 0 ? line : line[..sp]; var rest = sp < 0 ? "" : line[(sp + 1)..];
            try { Run(b, cmd, rest, write, timeout); }
            catch (CliException e) { Console.Error.WriteLine(e.Message); }
        }
    }
}
