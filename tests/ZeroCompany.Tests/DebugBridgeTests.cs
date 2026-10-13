using ZeroCompany.DebugCli;

namespace ZeroCompany.Tests;

/// <summary>zcdebug's channel to the in-game mod, tested against a fake mod that speaks the same line protocol as main.lua.</summary>
public class DebugBridgeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "zc-debug-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A stand-in for main.lua: a heartbeat, then answer each new request with the handler.</summary>
    sealed class FakeMod : IDisposable
    {
        readonly CancellationTokenSource _stop = new();
        readonly Task _task;
        public List<string> Seen { get; } = new();

        public FakeMod(Bridge b, Func<string, string, (bool, string)> handler)
        {
            File.WriteAllText(b.HeartbeatPath, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\tzcdebug-mod fake\n");
            _task = Task.Run(() =>
            {
                string? last = null;
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        if (File.Exists(b.RequestPath) && Protocol.TryParseRequest(File.ReadAllText(b.RequestPath), out var id, out var cmd, out var args) && id != last)
                        {
                            last = id;
                            lock (Seen) Seen.Add(cmd + " " + args);
                            var (ok, text) = handler(cmd, args);
                            File.WriteAllText(b.ReplyPath, Protocol.FormatReply(id, ok, text));
                        }
                    }
                    catch (IOException) { }
                    Thread.Sleep(10);
                }
            });
        }

        public void Dispose() { _stop.Cancel(); _task.Wait(1000); }
    }

    [Fact]
    public void Escaping_round_trips_tabs_newlines_and_backslashes()
    {
        var s = "a\tb\nc\r\\d\\n end";
        Assert.Equal(s, Protocol.Unescape(Protocol.Escape(s)));
        Assert.DoesNotContain('\t', Protocol.Escape(s));
        Assert.DoesNotContain('\n', Protocol.Escape(s));
        Assert.True(Protocol.TryParseRequest(Protocol.FormatRequest("ab12", "eval", s), out var id, out var cmd, out var args));
        Assert.Equal(("ab12", "eval", s), (id, cmd, args));
        Assert.True(Protocol.TryParseReply(Protocol.FormatReply("ab12", false, s), out var rid, out var reply));
        Assert.Equal("ab12", rid);
        Assert.False(reply.Ok);
        Assert.Equal(s, reply.Text);
    }

    [Fact]
    public void A_line_without_its_final_newline_is_not_an_answer_yet()
    {
        Assert.False(Protocol.TryParseReply("ab12\tok\tpartial", out _, out _));
        Assert.False(Protocol.TryParseRequest("ab12\tping", out _, out _, out _));
        Assert.False(Protocol.TryParseReply("ab12\tmaybe\tx\n", out _, out _));
    }

    [Fact]
    public void A_command_gets_the_mods_answer_and_arguments_arrive_intact()
    {
        var b = new Bridge(_dir);
        using var mod = new FakeMod(b, (cmd, args) => cmd == "get" ? (true, "value of " + args) : (false, "nope"));
        var r = b.Send("get", "Pawn Health\twith tab", TimeSpan.FromSeconds(3));
        Assert.True(r.Ok);
        Assert.Equal("value of Pawn Health\twith tab", r.Text);
        var bad = b.Send("zzz", "", TimeSpan.FromSeconds(3));
        Assert.False(bad.Ok);
        Assert.Equal("nope", bad.Text);
        Assert.False(File.Exists(b.ReplyPath));                                       // answers are cleaned up
    }

    [Fact]
    public void An_old_answer_is_never_taken_for_a_new_command()
    {
        var b = new Bridge(_dir);
        File.WriteAllText(b.ReplyPath, Protocol.FormatReply("oldid000", true, "stale"));
        Assert.Throws<TimeoutException>(() => b.Send("ping", "", TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void Heartbeat_tells_whether_the_game_is_there()
    {
        var b = new Bridge(_dir);
        Assert.False(b.Connected(TimeSpan.FromSeconds(5)));
        File.WriteAllText(b.HeartbeatPath, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60}\tmod\n");
        Assert.False(b.Connected(TimeSpan.FromSeconds(5)));
        File.WriteAllText(b.HeartbeatPath, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\tzcdebug-mod 0.1\n");
        Assert.True(b.Connected(TimeSpan.FromSeconds(5)));
        Assert.Equal("zcdebug-mod 0.1", b.HeartbeatInfo());
    }

    [Fact]
    public void Commands_that_change_the_game_need_write_and_are_never_sent_without_it()
    {
        var b = new Bridge(_dir);
        using var mod = new FakeMod(b, (_, _) => (true, "done"));
        foreach (var cmd in new[] { "set", "exec", "eval" })
            Assert.Contains("--write", Assert.Throws<CliException>(() => Session.Run(b, cmd, "x y z", write: false, TimeSpan.FromSeconds(1))).Message);
        Assert.Empty(mod.Seen);
        var sw = new StringWriter();
        Assert.Equal(0, Session.Run(b, "set", "Pawn Health 5", write: true, TimeSpan.FromSeconds(3), sw));
        Assert.Equal("done" + Environment.NewLine, sw.ToString());
        Assert.Equal(new[] { "set Pawn Health 5" }, mod.Seen);
        Assert.Equal(0, Session.Run(b, "find", "Pawn", write: false, TimeSpan.FromSeconds(3), new StringWriter()));    // reads need no flag
    }

    [Fact]
    public void No_heartbeat_means_a_clear_error_and_nothing_is_written()
    {
        var b = new Bridge(_dir);
        Assert.Contains("not answering", Assert.Throws<CliException>(() => Session.Run(b, "ping", "", write: false, TimeSpan.FromSeconds(1))).Message);
        Assert.False(File.Exists(b.RequestPath));
        Assert.Contains("unknown command", Assert.Throws<CliException>(() => Session.Run(b, "fly", "x", write: true, TimeSpan.FromSeconds(1))).Message);
    }

    [Fact]
    public void A_mod_error_comes_back_as_a_failed_run()
    {
        var b = new Bridge(_dir);
        using var mod = new FakeMod(b, (_, _) => (false, "no live instance of class Nope"));
        var err = new StringWriter();
        Assert.Equal(1, Session.Run(b, "get", "Nope Health", write: false, TimeSpan.FromSeconds(3), new StringWriter(), err));
        Assert.Contains("no live instance", err.ToString());
    }

    [Fact]
    public void The_mod_files_ship_inside_the_tool_and_install_into_ue4ss_but_never_without_it()
    {
        Assert.Contains("mod/ZcDebug/Scripts/main.lua", typeof(Install).Assembly.GetManifestResourceNames());
        var game = Path.Combine(_dir, "Game");
        var bin = Path.Combine(game, "SWZeroCompany", "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "SWZeroCompany.exe"), "");
        Assert.Contains("never downloads", Assert.Throws<CliException>(() => Install.Setup(game, uninstall: false)).Message);     // no UE4SS yet
        Directory.CreateDirectory(Path.Combine(bin, "ue4ss", "Mods"));
        File.WriteAllText(Path.Combine(bin, "ue4ss", "UE4SS.dll"), "");
        Install.Setup(game, uninstall: false);
        var lua = Path.Combine(bin, "ue4ss", "Mods", "ZcDebug", "Scripts", "main.lua");
        Assert.Contains("LoopAsync", File.ReadAllText(lua));
        Assert.True(File.Exists(Path.Combine(bin, "ue4ss", "Mods", "ZcDebug", "enabled.txt")));
        Assert.Contains("removed", Install.Setup(game, uninstall: true));
        Assert.False(File.Exists(lua));
        Assert.True(File.Exists(Path.Combine(bin, "SWZeroCompany.exe")));                                                      // the game itself is never touched
    }
}
