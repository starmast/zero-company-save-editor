using System.Text.Json;

namespace ZeroCompany.Core.Platform;

/// <summary>Where the app keeps its data, and where saves and the game are likely to be, on each OS.</summary>
public sealed class AppEnvironment
{
    public const string AppFolder = "ZeroCompanyEditor";

    /// <summary>Per-user data root (<c>%LOCALAPPDATA%</c>, <c>~/.local/share</c>, <c>~/Library/Application Support</c>).</summary>
    public string DataDir { get; }
    public string BackupsDir => Path.Combine(DataDir, "backups");
    public string GameDataDir => Path.Combine(DataDir, "gamedata");
    public string GameDbPath => Path.Combine(GameDataDir, "gamedata.db.json");
    public string PortraitCacheDir => Path.Combine(DataDir, "portraits");
    public string SettingsPath => Path.Combine(DataDir, "settings.json");

    /// <summary>Look in the OS's own save locations too (tests turn this off to stay isolated from the machine).</summary>
    public bool UseSystemSaveDirs { get; init; } = true;

    public AppEnvironment(string? dataDir = null)
    {
        DataDir = dataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder);
        Directory.CreateDirectory(DataDir);
    }

    // ---- save folders -----------------------------------------------------------------
    /// <summary>
    /// Folders to look for saves in (id -> path). The id is stable per folder so backups of same-named saves in different
    /// folders never share history: "game" for the install's own folder, then "proton1".., then "user1"...
    /// Only folders that exist are returned.
    /// </summary>
    public Dictionary<string, string> DiscoverSaveDirs(IEnumerable<string>? userDirs = null, string? home = null)
    {
        var found = new Dictionary<string, string>();
        void Add(string id, string path) { if (Directory.Exists(path) && !found.ContainsValue(path)) found[id] = path; }

        if (!UseSystemSaveDirs) { }
        else if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Add("game", Path.Combine(local, "SWZeroCompany", "Saved", "SaveGames"));
        }
        else
        {
            int n = 0;
            foreach (var p in ProtonSaveDirs(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                Add(n++ == 0 ? "game" : $"proton{n}", p);
        }
        int u = 1;
        foreach (var d in userDirs ?? Array.Empty<string>()) Add($"user{u++}", d);
        return found;
    }

    /// <summary>Save folders inside Steam/Heroic/Lutris Wine prefixes under <paramref name="home"/> (best effort).</summary>
    public static IEnumerable<string> ProtonSaveDirs(string home)
    {
        var prefixes = new List<string>();
        var steamRoots = new[]
        {
            Path.Combine(home, ".local", "share", "Steam"), Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, "Library", "Application Support", "Steam"),
        };
        foreach (var root in steamRoots)
        {
            var compat = Path.Combine(root, "steamapps", "compatdata");
            if (Directory.Exists(compat)) prefixes.AddRange(Directory.GetDirectories(compat).Select(d => Path.Combine(d, "pfx")));
        }
        var heroic = Path.Combine(home, "Games", "Heroic");
        if (Directory.Exists(heroic)) prefixes.AddRange(Directory.GetDirectories(heroic).Select(d => Path.Combine(d, "pfx")).Concat(Directory.GetDirectories(heroic)));
        var lutris = Path.Combine(home, "Games");
        if (Directory.Exists(lutris)) prefixes.AddRange(Directory.GetDirectories(lutris));
        foreach (var pfx in prefixes)
        {
            var users = Path.Combine(pfx, "drive_c", "users");
            if (!Directory.Exists(users)) continue;
            foreach (var user in Directory.GetDirectories(users))
                yield return Path.Combine(user, "AppData", "Local", "SWZeroCompany", "Saved", "SaveGames");
        }
    }

    /// <summary>Install folders worth offering to the extractor on this OS (existing ones only).</summary>
    public static IEnumerable<string> GuessGameDirs(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var c = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            c.Add(@"C:\Program Files\EA Games\Star Wars Zero Company");
            c.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Zero Company");
            c.Add(@"C:\Program Files (x86)\EA Games\Star Wars Zero Company");
        }
        else
            foreach (var root in new[] { Path.Combine(home, ".local", "share", "Steam"), Path.Combine(home, ".steam", "steam") })
                c.Add(Path.Combine(root, "steamapps", "common", "Star Wars Zero Company"));
        return c.Where(Directory.Exists);
    }
}

/// <summary>User choices that survive restarts.</summary>
public sealed class AppSettings
{
    public List<string> SaveDirs { get; set; } = new();
    public string GameDir { get; set; } = "";
    public string UsmapPath { get; set; } = "";
    public string OodleDir { get; set; } = "";

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load(AppEnvironment env)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(env.SettingsPath)) ?? new AppSettings(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new AppSettings(); }
    }

    public void Save(AppEnvironment env)
    {
        try { File.WriteAllText(env.SettingsPath, JsonSerializer.Serialize(this, Opts)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* settings are a convenience */ }
    }
}
