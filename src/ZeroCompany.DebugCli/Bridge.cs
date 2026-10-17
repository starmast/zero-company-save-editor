namespace ZeroCompany.DebugCli;

/// <summary>
/// The CLI side of the channel to the ZcDebug mod: a request file the mod polls, a reply file it writes, and a heartbeat file that
/// tells us the game is running with the mod loaded. Everything lives in one folder that both sides know.
/// </summary>
public sealed class Bridge
{
    public string Dir { get; }
    public string RequestPath => Path.Combine(Dir, "request.txt");
    public string ReplyPath => Path.Combine(Dir, "response.txt");
    public string HeartbeatPath => Path.Combine(Dir, "heartbeat.txt");

    /// <summary>
    /// The channel folder. <c>ZCDEBUG_DIR</c> overrides it, for running the CLI somewhere other than the game's own Windows (for example under WSL,
    /// with <c>/mnt/c/Users/NAME/AppData/Local/ZeroCompanyEditor/debug</c>).
    /// </summary>
    public static string DefaultDir =>
        Environment.GetEnvironmentVariable("ZCDEBUG_DIR") is { Length: > 0 } over
            ? over
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroCompanyEditor", "debug");

    public Bridge(string? dir = null)
    {
        Dir = dir ?? DefaultDir;
        Directory.CreateDirectory(Dir);
    }

    /// <summary>When the mod last wrote its heartbeat (its clock is Unix seconds), or null if it never has.</summary>
    public DateTimeOffset? LastHeartbeat()
    {
        try
        {
            var first = File.ReadAllText(HeartbeatPath).Split('\t', '\n')[0].Trim();
            return long.TryParse(first, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The text the mod wrote after the timestamp in its heartbeat (version, game build ...), or "".</summary>
    public string HeartbeatInfo()
    {
        try { var p = File.ReadAllText(HeartbeatPath).TrimEnd('\n', '\r').Split('\t', 2); return p.Length > 1 ? Protocol.Unescape(p[1]) : ""; }
        catch (IOException) { return ""; }
    }

    public bool Connected(TimeSpan maxAge) => LastHeartbeat() is { } t && DateTimeOffset.UtcNow - t <= maxAge;

    /// <summary>Send one command and wait for its reply. Throws <see cref="TimeoutException"/> when the mod does not answer in time.</summary>
    public Reply Send(string command, string args, TimeSpan timeout, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        TryDelete(ReplyPath);                                        // an old answer must never be taken for this one
        var tmp = RequestPath + ".tmp";
        File.WriteAllText(tmp, Protocol.FormatRequest(id, command, args));
        // the mod may have request.txt open (reading or deleting it) right now, which Windows refuses to overwrite
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, RequestPath, overwrite: true); break; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 40) { Thread.Sleep(25); }
        }

        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            if (TryReadReply(id, out var reply)) { TryDelete(ReplyPath); return reply; }
            Thread.Sleep(40);
        }
        throw new TimeoutException($"no answer to '{command}' within {timeout.TotalSeconds:0.#}s");
    }

    bool TryReadReply(string id, out Reply reply)
    {
        reply = new Reply(false, "");
        try
        {
            if (!File.Exists(ReplyPath)) return false;
            using var fs = new FileStream(ReplyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return Protocol.TryParseReply(r.ReadToEnd(), out var got, out reply) && got == id;
        }
        catch (IOException) { return false; }                        // the mod is writing it right now: try again
        catch (UnauthorizedAccessException) { return false; }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
