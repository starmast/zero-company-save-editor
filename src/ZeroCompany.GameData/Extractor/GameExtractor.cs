using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.GameData.Extractor;

public sealed class ExtractOptions
{
    /// <summary>The game install (the folder that contains SWZeroCompany), or the SWZeroCompany or Paks folder itself.</summary>
    public string GameDir { get; set; } = "";
    /// <summary>Community mappings file (.usmap) for the game's UE 5.6 build; required for the data assets.</summary>
    public string UsmapPath { get; set; } = "";
    /// <summary>Where to find/place the Oodle decompressor (defaults to the application folder).</summary>
    public string? OodleDir { get; set; }
}

public class ExtractionException : Exception
{
    public ExtractionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Reads the user's own game install (read-only) with CUE4Parse and produces a <see cref="GameDatabase"/>.
/// The reader stage yields raw property bags; <see cref="Distiller"/> turns them into typed models.
/// </summary>
public static class GameExtractor
{
    const string ContentRoot = "SWZeroCompany/Content";

    /// <summary>Default install location on Windows, or "" elsewhere.</summary>
    public static string DefaultGameDir =>
        OperatingSystem.IsWindows() ? @"C:\Program Files\EA Games\Star Wars Zero Company" : "";

    /// <summary>Resolve whatever folder the user picked to the Paks folder, or null if it does not look right.</summary>
    public static string? FindPaksDir(string picked)
    {
        if (string.IsNullOrWhiteSpace(picked)) return null;
        foreach (var c in new[]
        {
            picked,
            Path.Combine(picked, "Paks"),
            Path.Combine(picked, "Content", "Paks"),
            Path.Combine(picked, "SWZeroCompany", "Content", "Paks"),
        })
            if (Directory.Exists(c) && Directory.EnumerateFiles(c, "*.utoc").Any()) return c;
        return null;
    }

    public static Task<GameDatabase> ExtractAsync(ExtractOptions opt, IProgress<string>? progress = null,
                                                  CancellationToken ct = default) =>
        Task.Run(() => Extract(opt, progress ?? new Progress<string>(), ct), ct);

    public static GameDatabase Extract(ExtractOptions opt, IProgress<string> log, CancellationToken ct = default)
    {
        var paks = FindPaksDir(opt.GameDir)
            ?? throw new ExtractionException($"Could not find the game's Paks folder under '{opt.GameDir}'.");
        if (!File.Exists(opt.UsmapPath))
            throw new ExtractionException("A .usmap mappings file is required (the data assets use unversioned properties).");

        log.Report($"Mounting {paks}");
        var provider = Mount(paks, opt.UsmapPath, opt.OodleDir, log, ct);
        var pkg = new PackageReader(provider);
        var raw = new RawGameData();

        ct.ThrowIfCancellationRequested();
        log.Report("Reading upgrade recipes");
        raw.Upgrades = new RecipeReader(pkg).Read(log, ct);

        foreach (var (label, target, subs) in new (string, Action<JObject>, string[])[]
        {
            ("items", o => raw.Items = o, new[] { "GameData/ItemData/" }),
            ("effects", o => raw.Effects = o, new[] { "RosterUpgradeEffects/", "CrossTrainingStatRewards/", "GameData/Progression/" }),
            ("focus costs", o => raw.Focus = o, new[] { "GameData/FocusPointData/" }),
            ("Coil upgrades", o => raw.Crisis = o, new[] { "ResultEffects/CrisisEffects/" }),
        })
        {
            ct.ThrowIfCancellationRequested();
            log.Report($"Reading {label}");
            target(pkg.DumpFamily(subs, log, ct));
        }

        ct.ThrowIfCancellationRequested();
        log.Report("Reading text");
        raw.Strings = ReadStrings(provider);

        log.Report("Building database");
        var db = Distiller.Distill(raw, opt.GameDir);
        log.Report($"Done: {db.Upgrades.Count} upgrades, {db.Items.Count} items, {db.Effects.Count} effects, "
                   + $"{db.FocusThresholds.Count} abilities, {db.CoilEffects.Count} Coil upgrades");
        return db;
    }

    static DefaultFileProvider Mount(string paksDir, string usmap, string? oodleDir, IProgress<string> log, CancellationToken ct)
    {
        try
        {
            var dir = oodleDir ?? AppContext.BaseDirectory;
            var oodle = new[] { OodleHelper.OodleFileName, "oodle-data-shared.dll", "liboodle-data-shared.so" }
                .Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
            if (oodle == null)
            {
                oodle = Path.Combine(dir, OodleHelper.OodleFileName);
                log.Report("Fetching Oodle via CUE4Parse...");
                OodleHelper.DownloadOodleDllAsync(ct).GetAwaiter().GetResult();
            }
            OodleHelper.Initialize(oodle);
            var p = new DefaultFileProvider(paksDir, SearchOption.TopDirectoryOnly,
                new VersionContainer(EGame.GAME_UE5_6), StringComparer.OrdinalIgnoreCase);
            p.Initialize();
            p.SubmitKey(new FGuid(), new FAesKey(new byte[32]));    // containers are not encrypted
            p.PostMount();
            p.MappingsContainer = new FileUsmapTypeMappingsProvider(usmap);
            log.Report($"Mounted {p.MountedVfs.Count} containers, {p.Files.Count} files");
            return p;
        }
        catch (Exception e) when (e is not OperationCanceledException and not ExtractionException)
        {
            throw new ExtractionException($"Could not open the game files: {e.Message}", e);
        }
    }

    /// <summary>namespace -> key -> English text from Game.locres.</summary>
    static Dictionary<string, Dictionary<string, string>> ReadStrings(DefaultFileProvider p)
    {
        const string path = ContentRoot + "/Localization/Game/en/Game.locres";
        if (!p.TryCreateReader(path, out var ar)) throw new ExtractionException("Game.locres not found in the game files.");
        var res = new FTextLocalizationResource(ar);
        var dict = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (ns, entries) in res.Entries)
        {
            var d = new Dictionary<string, string>();
            foreach (var (key, entry) in entries) d[key.Str] = entry.LocalizedString;
            dict[ns.Str] = d;
        }
        return dict;
    }
}
