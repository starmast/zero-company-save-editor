using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.App.ViewModels;

/// <summary>
/// Game data: reads the user's own install (read-only) to learn item names, upgrade costs, focus costs and Coil text. The
/// result is a small file kept next to the editor's settings; the editor works without it, just with less friendly names.
/// </summary>
public sealed partial class GameDataViewModel : ScreenViewModel
{
    readonly MainViewModel _main;
    CancellationTokenSource? _cts;
    readonly StringBuilder _log = new();

    public GameDataViewModel(MainViewModel main)
    {
        _main = main;
        var s = main.Session.Settings;
        _gameDir = s.GameDir.Length > 0 ? s.GameDir : AppEnvironment.GuessGameDirs().FirstOrDefault() ?? GameExtractor.DefaultGameDir;
        _usmapPath = s.UsmapPath.Length > 0 ? s.UsmapPath : GuessUsmap();
        RefreshStatus();
    }

    [ObservableProperty] string _gameDir;
    [ObservableProperty] string _usmapPath;
    [ObservableProperty] string _status = "";
    [ObservableProperty] string _logText = "";
    [ObservableProperty] bool _isRunning;
    [ObservableProperty] bool _hasData;

    public bool CanExtract => !IsRunning;
    public bool NotRunning => !IsRunning;
    public string PathsHint => OperatingSystem.IsMacOS()
        ? "The extractor reads Windows/Linux game files and its decompressor is not available on macOS: extract on another machine and use Import."
        : "";

    partial void OnIsRunningChanged(bool value) { OnPropertyChanged(nameof(CanExtract)); OnPropertyChanged(nameof(NotRunning)); }

    string GuessUsmap()
    {
        foreach (var dir in new[] { _main.Session.Env.DataDir, _main.Session.Env.GameDataDir, AppContext.BaseDirectory })
            try
            {
                var f = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.usmap").FirstOrDefault() : null;
                if (f != null) return f;
            }
            catch (IOException) { }
        return "";
    }

    void RefreshStatus()
    {
        var db = _main.Session.Db;
        HasData = !db.IsEmpty;
        Status = db.IsEmpty
            ? "No game data yet."
            : $"{db.Upgrades.Count} upgrades · {db.Items.Count} items · {db.Effects.Count} effects · {db.FocusThresholds.Count} abilities · "
              + $"{db.CoilEffects.Count} Coil upgrades. Extracted {db.ExtractedUtc.ToLocalTime():yyyy-MM-dd HH:mm}.";
    }

    void Append(string line)
    {
        _log.AppendLine(line);
        LogText = _log.ToString();
    }

    [RelayCommand]
    async Task BrowseGame()
    {
        var p = await _main.Ui.PickFolderAsync("Choose the Star Wars Zero Company install folder (or its Paks folder)");
        if (!string.IsNullOrEmpty(p)) GameDir = p;
    }

    [RelayCommand]
    async Task BrowseUsmap()
    {
        var p = await _main.Ui.PickFileAsync("Choose the community mappings file (.usmap) for your game version", "Mappings", "*.usmap");
        if (!string.IsNullOrEmpty(p)) UsmapPath = p;
    }

    [RelayCommand]
    async Task Extract()
    {
        if (IsRunning) return;
        if (GameExtractor.FindPaksDir(GameDir) == null) { _main.SetBanner("Could not find the game's Paks folder there. Pick the folder that contains SWZeroCompany.", "err"); return; }
        if (!File.Exists(UsmapPath)) { _main.SetBanner("Pick the .usmap mappings file first (the game's data assets cannot be read without it).", "err"); return; }
        var s = _main.Session;
        s.Settings.GameDir = GameDir; s.Settings.UsmapPath = UsmapPath;
        s.SaveSettings();
        _log.Clear(); LogText = "";
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(Append);                 // created on the UI thread: reports marshal back to it
        try
        {
            var db = await GameExtractor.ExtractAsync(new ExtractOptions
            {
                GameDir = GameDir, UsmapPath = UsmapPath, OodleDir = s.Settings.OodleDir.Length > 0 ? s.Settings.OodleDir : s.Env.DataDir,
            }, progress, _cts.Token);
            s.UseDatabase(db);
            RefreshStatus();
            _main.SetBanner("Game data extracted.", "ok");
            _main.OnDatabaseChanged();
        }
        catch (OperationCanceledException) { Append("Cancelled."); }
        catch (ExtractionException e) { Append("Failed: " + e.Message); _main.SetBanner(e.Message, "err"); }
        catch (Exception e) { Append("Failed: " + e.Message); _main.SetBanner("Extraction failed: " + e.Message, "err"); }
        finally { IsRunning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] void Cancel() => _cts?.Cancel();

    [RelayCommand]
    async Task Import()
    {
        var p = await _main.Ui.PickFileAsync("Choose a game database file exported from another machine", "Game database", "*.json");
        if (string.IsNullOrEmpty(p)) return;
        var db = GameDatabase.TryLoad(p);
        if (db == null || db.IsEmpty) { _main.SetBanner("That file is not a usable game database (or is from a different editor version).", "err"); return; }
        _main.Session.UseDatabase(db);
        RefreshStatus();
        _main.SetBanner("Game data imported.", "ok");
        _main.OnDatabaseChanged();
    }

    [RelayCommand]
    void Export()
    {
        var src = _main.Session.Env.GameDbPath;
        if (File.Exists(src)) _ = _main.Ui.CopyToClipboardAsync(src);
        _main.SetBanner(File.Exists(src) ? $"The database file is at {src} (path copied). Copy it to another machine and use Import there." : "Nothing to export yet.", "info");
    }

    public override void Dispose()
    {
        _cts?.Cancel();
        base.Dispose();
    }
}
