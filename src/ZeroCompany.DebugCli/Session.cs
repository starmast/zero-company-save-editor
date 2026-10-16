using ZeroCompany.Core.Platform;

namespace ZeroCompany.DebugCli;

/// <summary>Running commands against the mod: the write guard, status output and the interactive loop.</summary>
public static class Session
{
    /// <summary>Commands that read only. Everything else changes the game and needs --write.</summary>
    public static readonly IReadOnlySet<string> ReadOnly = new HashSet<string> { "ping", "find", "props", "get", "dump", "effects", "loadstatus", "log", "funcs" };
    static readonly IReadOnlySet<string> All = new HashSet<string> { "ping", "find", "props", "get", "dump", "effects", "loadstatus", "log", "funcs", "set", "attr", "fx", "load", "exec", "eval", "call", "trace", "reload" };

    /// <summary>Send one command (with the same checks as <see cref="Run"/>) and return the mod's answer without printing it.</summary>
    public static Reply RunQuiet(Bridge b, string command, string args, bool write, TimeSpan timeout)
    {
        if (!All.Contains(command)) throw new CliException($"unknown command '{command}' (zcdebug --help)");
        if (!ReadOnly.Contains(command) && !write)
            throw new CliException($"'{command}' changes the running game; add --write if you mean it");
        if (!b.Connected(TimeSpan.FromSeconds(10)))
            throw new CliException("the mod is not answering (no recent heartbeat). Run `zcdebug status`.");
        try { return b.Send(command, args, timeout); }
        catch (TimeoutException e) { throw new CliException(e.Message + ". The game may be paused, loading or busy."); }
    }

    public static int Status(Bridge b)
    {
        var game = GameProcess.Running();
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
    public static int Run(Bridge b, string command, string args, bool write, TimeSpan timeout, TextWriter? output = null, TextWriter? error = null,
                          TimeSpan? waitForLoad = null)
    {
        output ??= Console.Out; error ??= Console.Error;
        if (!All.Contains(command)) throw new CliException($"unknown command '{command}' (zcdebug --help)");
        if (!ReadOnly.Contains(command) && !write)
            throw new CliException($"'{command}' changes the running game; add --write if you mean it");
        if (args.Trim().Length == 0 && command is not ("ping" or "loadstatus" or "log"))
            throw new CliException($"'{command}' needs an argument (zcdebug --help)");
        if (!b.Connected(TimeSpan.FromSeconds(10)))
            throw new CliException("the mod is not answering (no recent heartbeat). Run `zcdebug status`.");
        try
        {
            var r = b.Send(command, args, timeout);
            (r.Ok ? output : error).WriteLine(r.Ok ? r.Text : "error: " + r.Text);
            if (r.Ok && command == "load" && waitForLoad is { } max && r.Text.StartsWith("loading", StringComparison.Ordinal))
                return WaitForLoad(b, output, error, max);
            return r.Ok ? 0 : 1;
        }
        catch (TimeoutException e) { throw new CliException(e.Message + ". The game may be paused, loading or busy."); }
    }

    /// <summary>
    /// Poll the mod's <c>loadstatus</c> until the loaded level is up. While the game loads it does not answer, so timeouts and
    /// errors only mean "not yet". Two "ready" answers in a row are needed, so a level that is still being built is not taken
    /// for a finished one. Returns 0 when ready, 3 if the time ran out. <paramref name="pause"/> is how long to wait between polls.
    /// </summary>
    public static int WaitForLoad(Bridge b, TextWriter output, TextWriter error, TimeSpan max, TimeSpan? pause = null)
    {
        var gap = pause ?? TimeSpan.FromSeconds(2);
        var until = DateTime.UtcNow + max;
        string last = "";
        int ready = 0;
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(gap);
            string text;
            try
            {
                var r = b.Send("loadstatus", "", TimeSpan.FromSeconds(3));
                text = r.Ok ? r.Text : "error: " + r.Text;
            }
            catch (TimeoutException) { text = "loading: the game is busy"; }
            ready = text.StartsWith("ready", StringComparison.Ordinal) ? ready + 1 : 0;
            if (ready >= 2) { output.WriteLine("load finished (" + text + ")"); return 0; }
            if (text != last && ready == 0) { output.WriteLine("  " + text); last = text; }
        }
        error.WriteLine($"the load did not finish within {max.TotalSeconds:0}s (last: {last}). Look at the game before sending anything else.");
        return 3;
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
