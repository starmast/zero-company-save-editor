using System.Diagnostics;

namespace ZeroCompany.DebugCli;

/// <summary>What <see cref="Fix"/> needs from the file system and from Windows, so it can be tested without either.</summary>
public interface IFixHost
{
    bool DirExists(string path);
    /// <summary>Create the folder as the current user; false when Windows refuses (access denied).</summary>
    bool TryCreateDir(string path);
    /// <summary>Whether the current user can create and delete a file in the folder.</summary>
    bool CanWriteIn(string dir);
    /// <summary>Run one command in an elevated (UAC) cmd.exe and wait for it; false when it was declined or failed to start.</summary>
    bool RunElevated(string commandLine);
    string UserName { get; }
}

/// <summary>
/// <c>zcdebug fix</c>: the game-folder write problems that show up when the game lives under Program Files. It checks the three
/// folders the mod and UE4SS need, creates what it can as the current user, and for what Windows refuses it prints the exact
/// command to run as Administrator, or with <c>--elevate</c> runs just those commands through the normal Windows UAC prompt
/// (one prompt per command). It creates folders and grants the current user write access to UE4SS's own folder; it never
/// touches the game's files.
/// </summary>
public static class Fix
{
    /// <summary>Something that is wrong, with the one command that would fix it from an elevated prompt.</summary>
    public sealed record Problem(string Title, string Path, string AdminCommand);

    /// <summary>The folder BPModLoaderMod (shipped with UE4SS) tries to create at every start; without it the UE4SS log shows "Access is denied".</summary>
    public static string LogicModsDir(string binaries) =>
        Path.Combine(Directory.GetParent(Directory.GetParent(binaries)!.FullName)!.FullName, "Content", "Paks", "LogicMods");

    /// <summary>A path is safe to put inside a quoted cmd argument.</summary>
    static bool Safe(string p) => p.Length > 0 && p.IndexOfAny(new[] { '"', '%', '&', '|', '<', '>', '^', '\r', '\n' }) < 0;

    /// <summary>Look at the folders; whatever can be fixed without elevation is fixed on the way. Returns what is still wrong.</summary>
    public static List<Problem> Find(string binaries, IFixHost host, TextWriter output)
    {
        var problems = new List<Problem>();
        var ue4ss = Path.Combine(binaries, "ue4ss");
        var mods = host.DirExists(Path.Combine(ue4ss, "Mods")) ? Path.Combine(ue4ss, "Mods") : Path.Combine(binaries, "Mods");     // newer UE4SS keeps Mods in a ue4ss subfolder
        var modsRoot = Path.GetDirectoryName(mods)!;           // the folder that owns Mods: where UE4SS also writes its logs and dumps

        // 1. UE4SS's own folder: ZcDebug is installed into Mods, UE4SS writes its log and dumps beside it
        if (!host.DirExists(mods)) output.WriteLine($"  Mods folder     missing ({mods}); start the game once with UE4SS installed so it creates it");
        else if (host.CanWriteIn(mods) && host.CanWriteIn(modsRoot)) output.WriteLine($"  Mods folder     writable ({mods})");
        else
        {
            output.WriteLine($"  Mods folder     NOT writable ({mods})");
            if (Safe(modsRoot) && Safe(host.UserName))
                problems.Add(new Problem("let your user write to UE4SS's folder (zcdebug install and UE4SS's log need it)", modsRoot,
                                         $"icacls \"{modsRoot}\" /grant \"{host.UserName}:(OI)(CI)M\" /T /C /Q"));
        }

        // 2. the LogicMods folder BPModLoaderMod wants
        var logic = LogicModsDir(binaries);
        if (host.DirExists(logic)) output.WriteLine($"  LogicMods       present ({logic})");
        else if (host.TryCreateDir(logic)) output.WriteLine($"  LogicMods       created ({logic})");
        else
        {
            output.WriteLine($"  LogicMods       cannot be created by your user ({logic})");
            if (Safe(logic))
                problems.Add(new Problem("create the LogicMods folder (stops the BPModLoaderMod \"Access is denied\" error in UE4SS.log)", logic, $"mkdir \"{logic}\""));
        }
        return problems;
    }

    public static int Run(string? game, bool elevate, TextWriter output, IFixHost? host = null, bool checkSignatures = true, bool undoSignatures = false)
    {
        if (host == null && !OperatingSystem.IsWindows()) { output.WriteLine("Nothing to fix: this is only needed for a Windows install under a protected folder."); return 0; }
        host ??= new WindowsHost();
        var dir = Install.FindGameDir(game) ?? throw new CliException("game folder unknown: pass --game \"C:\\...\\Star Wars Zero Company\"");
        var bin = Install.BinariesDir(dir) ?? throw new CliException($"no SWZeroCompany.exe under {dir}");
        output.WriteLine($"game: {dir}");
        if (undoSignatures) return SignaturePack.Undo(bin, output) ? 0 : 1;
        var problems = Find(bin, host, output);
        // after a game update UE4SS's signature pack stops recognising the new exe: list the new build in its scripts (no elevation needed when the folder is writable)
        var signaturesOk = true;
        if (checkSignatures)
        {
            try { signaturesOk = SignaturePack.Run(bin, output); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                output.WriteLine("  Signatures      cannot write the signature scripts (" + e.Message + "); fix the UE4SS folder permission above first");
                signaturesOk = false;
            }
        }
        if (problems.Count == 0) { output.WriteLine(signaturesOk ? "Nothing left to fix." : "Something above still needs attention."); return signaturesOk ? 0 : 1; }

        output.WriteLine();
        foreach (var p in problems) output.WriteLine($"To {p.Title}, run in a terminal started as Administrator:\n    {p.AdminCommand}");
        if (!elevate)
        {
            output.WriteLine("\nOr run `zcdebug fix --elevate`: Windows will ask you to confirm each command in its own prompt.");
            return 1;
        }
        foreach (var p in problems)
        {
            output.WriteLine($"asking Windows to run: {p.AdminCommand}");
            if (!host.RunElevated(p.AdminCommand)) output.WriteLine("  not run (the prompt was declined or could not start)");
        }
        output.WriteLine("\nchecking again:");
        var left = Find(bin, host, output);
        if (left.Count == 0) { output.WriteLine("Fixed."); return 0; }
        output.WriteLine("Still not fixed: run the commands above from an Administrator terminal.");
        return 1;
    }

    /// <summary>The real thing: the file system as the current user, and UAC through <c>runas</c>.</summary>
    sealed class WindowsHost : IFixHost
    {
        public string UserName => $"{Environment.UserDomainName}\\{Environment.UserName}";
        public bool DirExists(string path) => Directory.Exists(path);

        public bool TryCreateDir(string path)
        {
            try { Directory.CreateDirectory(path); return Directory.Exists(path); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
        }

        public bool CanWriteIn(string dir)
        {
            var probe = Path.Combine(dir, $".zcdebug-write-test-{Environment.ProcessId}");
            try { File.WriteAllText(probe, ""); File.Delete(probe); return true; }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
        }

        public bool RunElevated(string commandLine)
        {
            try
            {
                // cmd.exe itself is elevated, with the one command as its argument: no script file is written anywhere
                var psi = new ProcessStartInfo("cmd.exe", "/c " + commandLine) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using var p = Process.Start(psi);
                if (p == null) return false;
                return p.WaitForExit(120_000);
            }
            catch (System.ComponentModel.Win32Exception) { return false; }      // the user declined the UAC prompt
        }
    }
}
