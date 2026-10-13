using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace ZeroCompany.DebugCli;

/// <summary>Finds the game, checks for UE4SS, and copies the ZcDebug mod into its Mods folder. Never downloads or patches anything.</summary>
public static class Install
{
    public const string ModName = "ZcDebug";
    static readonly string[] ResourceFiles = { "mod/ZcDebug/Scripts/main.lua", "mod/ZcDebug/enabled.txt" };

    /// <summary>Whether the game process is running (its launcher and shipping exe both start with this name).</summary>
    public static IReadOnlyList<(string Name, int Id)> RunningGame() =>
        Process.GetProcesses().Where(p => SafeName(p).StartsWith("SWZeroCompany", StringComparison.OrdinalIgnoreCase))
            .Select(p => (SafeName(p), p.Id)).ToList();

    static string SafeName(Process p) { try { return p.ProcessName; } catch (InvalidOperationException) { return ""; } }

    /// <summary>The game folder: --game, else the one the editor remembers in its settings, else null.</summary>
    public static string? FindGameDir(string? given)
    {
        if (!string.IsNullOrEmpty(given)) return given;
        try
        {
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroCompanyEditor", "settings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(settings));
            if (doc.RootElement.TryGetProperty("GameDir", out var g) && g.GetString() is { Length: > 0 } dir) return dir;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>The Win64 binaries folder of the game (where UE4SS lives), or null.</summary>
    public static string? BinariesDir(string gameDir)
    {
        foreach (var c in new[] { Path.Combine(gameDir, "SWZeroCompany", "Binaries", "Win64"), Path.Combine(gameDir, "Binaries", "Win64"), gameDir })
            if (File.Exists(Path.Combine(c, "SWZeroCompany.exe")) || Directory.Exists(Path.Combine(c, "ue4ss"))) return c;
        return null;
    }

    /// <summary>UE4SS's Mods folder: newer releases keep it in a ue4ss subfolder, older ones beside the exe.</summary>
    public static string? ModsDir(string binaries)
    {
        foreach (var c in new[] { Path.Combine(binaries, "ue4ss", "Mods"), Path.Combine(binaries, "Mods") })
            if (Directory.Exists(c)) return c;
        return null;
    }

    public static bool HasUe4ss(string binaries) =>
        File.Exists(Path.Combine(binaries, "ue4ss", "UE4SS.dll")) || File.Exists(Path.Combine(binaries, "UE4SS.dll"));

    /// <summary>Write the mod files into <paramref name="modDir"/> (the ZcDebug folder itself).</summary>
    static void Extract(string modDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in ResourceFiles)
        {
            var dest = Path.Combine(modDir, res["mod/ZcDebug/".Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var s = asm.GetManifestResourceStream(res) ?? throw new CliException($"missing embedded {res}");
            using var f = File.Create(dest);
            s.CopyTo(f);
        }
    }

    /// <summary>
    /// Install or remove the mod. With <paramref name="outDir"/> the mod folder is written there instead and nothing in the game
    /// folder is touched (for copying it in by hand when the game lives under a protected folder like Program Files).
    /// </summary>
    public static string Setup(string? game, bool uninstall, string? outDir = null)
    {
        if (outDir != null && !uninstall)
        {
            var target = Path.Combine(outDir, ModName);
            Extract(target);
            return $"wrote the {ModName} mod folder to {target}\nCopy that folder into UE4SS's Mods folder yourself (under Program Files that needs Administrator rights).";
        }
        try { return SetupInGame(game, uninstall); }
        catch (UnauthorizedAccessException)
        {
            throw new CliException(
                "Windows does not let this tool write to the game folder (it is under a protected location).\n" +
                "Either run the same command from a terminal started as Administrator, or run `zcdebug install --out <folder>`\n" +
                "and copy the ZcDebug folder it writes into UE4SS's Mods folder yourself. Nothing was changed.");
        }
    }

    static string SetupInGame(string? game, bool uninstall)
    {
        var dir = FindGameDir(game) ?? throw new CliException("game folder unknown: pass --game \"C:\\...\\Star Wars Zero Company\" (or extract game data in the editor once)");
        var bin = BinariesDir(dir) ?? throw new CliException($"no SWZeroCompany.exe under {dir}");
        var mods = ModsDir(bin);
        if (uninstall)
        {
            var target = mods == null ? null : Path.Combine(mods, ModName);
            if (target == null || !Directory.Exists(target)) return "ZcDebug is not installed.";
            Directory.Delete(target, recursive: true);
            return $"removed {target}";
        }
        if (!HasUe4ss(bin) || mods == null)
            throw new CliException(
                "UE4SS (the Unreal mod loader) is not installed in " + bin + ".\n" +
                "zcdebug never downloads or patches anything. Install a UE4SS build that supports Unreal Engine 5.6 yourself\n" +
                "(github.com/UE4SS-RE/RE-UE4SS), start the game once so it creates its Mods folder, then run `zcdebug install` again.");
        var modDir = Path.Combine(mods, ModName);
        Extract(modDir);
        return $"installed the {ModName} mod to {modDir}\nStart (or restart) the game, then run `zcdebug status`.";
    }
}

public sealed class CliException : Exception
{
    public CliException(string message) : base(message) { }
}
