using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.App.Services;
using ZeroCompany.Core;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.App.ViewModels;

/// <summary>Small bits of screen state that should survive a trip to another screen.</summary>
public sealed class UiMemory
{
    public string? SelectedOperator { get; set; }
    public string PersonnelTab { get; set; } = "overview";
    public string? SelectedBond { get; set; }
    public bool FreeFocus { get; set; }
    public string? SelectedUpgrade { get; set; }
    public string UpgradeTab { get; set; } = "Facilities";
    public string ArmoryTab { get; set; } = "Utility";
    public string CombatTab { get; set; } = "allies";
    public string GalaxyTab { get; set; } = "influence";
    public string ArmoryFilter { get; set; } = "";
    public string AdvancedTab { get; set; } = "";
    public string AdvancedFilter { get; set; } = "";
    public string GameDataLog { get; set; } = "";
}

public sealed partial class NavItemViewModel : ViewModelBase
{
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] bool _isActive;
    /// <summary>Shown for the open file: the campaign tabs for a save, Profile and Advanced for the profile file.</summary>
    [ObservableProperty] bool _isVisible = true;
    public System.Windows.Input.ICommand Command { get; }
    public NavItemViewModel(string key, string label, Action<string> go)
    {
        Key = key; Label = label;
        Command = new RelayCommand(() => go(key));
    }
}

public sealed partial class MainViewModel : ViewModelBase
{
    static readonly (string Key, string Label)[] NavDefs =
    {
        ("profile", "Profile"), ("command", "Command"), ("personnel", "Personnel"), ("armory", "Armory"), ("weapons", "Weapons"), ("upgrades", "Upgrades"),
        ("medbay", "Medbay"), ("galaxy", "Galaxy"), ("combat", "Combat"), ("advanced", "Advanced"),
    };

    public EditorSession Session { get; }
    public PortraitService Portraits { get; }
    public UiMemory Memory { get; } = new();
    public ObservableCollection<NavItemViewModel> Nav { get; } = new();
    public PendingBarViewModel PendingBar { get; }
    public NavItemViewModel SavesNav { get; }
    public NavItemViewModel DataNav { get; }

    [ObservableProperty] IUiServices _ui = new NullUiServices();
    [ObservableProperty] ScreenViewModel? _screen;
    [ObservableProperty] string _currentKey = "";
    [ObservableProperty] HeaderViewModel? _header;
    [ObservableProperty] bool _hasSave;
    [ObservableProperty] string? _bannerText;
    [ObservableProperty] string _bannerKind = "info";

    public bool HasBanner => !string.IsNullOrEmpty(BannerText);
    public bool BannerIsError => BannerKind == "err";
    public bool BannerIsOk => BannerKind == "ok";
    public bool BannerIsInfo => BannerKind == "info";
    public string ChangeSaveLabel => HasSave ? "Change save" : "Saves";
    public bool NoBanner => !HasBanner;

    public MainViewModel(EditorSession session)
    {
        Session = session;
        Portraits = new PortraitService(session.Env.PortraitCacheDir);
        foreach (var (k, l) in NavDefs) Nav.Add(new NavItemViewModel(k, l, key => Navigate(key)));
        SavesNav = new NavItemViewModel("saves", "Saves", key => Navigate(key));
        DataNav = new NavItemViewModel("gamedata", "Game data", key => Navigate(key));
        PendingBar = new PendingBarViewModel(this);
        Navigate(HasSave ? "command" : "saves");
        if (!session.HasGameData) SetBanner("No game data yet. Names and costs show as ids until you extract them from your install (Game data tab).", "info");
    }

    partial void OnBannerTextChanged(string? value) => OnPropertyChanged(nameof(HasBanner));

    partial void OnBannerKindChanged(string value)
    {
        OnPropertyChanged(nameof(BannerIsError)); OnPropertyChanged(nameof(BannerIsOk)); OnPropertyChanged(nameof(BannerIsInfo));
    }

    partial void OnHasSaveChanged(bool value) => OnPropertyChanged(nameof(ChangeSaveLabel));

    public void SetBanner(string? text, string kind = "info")
    {
        BannerKind = kind; BannerText = text;
        if (kind == "err" && text != null) LastIncident = new DebugIncident(text, null, DateTime.Now);
    }

    /// <summary>The last error shown, for the debug report.</summary>
    public DebugIncident? LastIncident { get; private set; }

    public const string IssuesUrl = "https://github.com/starmast/zero-company-save-editor/issues/new/choose";

    /// <summary>The debug report for the current state. The pending list is read here, on the UI thread that owns it; the checks run in the background.</summary>
    public Task<string> BuildDebugReportAsync()
    {
        var open = Session.Current;
        var pending = open != null ? Session.Pending.Describe(open) : null;
        var incident = LastIncident;
        return Task.Run(() => DebugReport.Build(Session.Workspace, DebugReport.ToolName(typeof(MainViewModel).Assembly), open?.Path, pending, incident));
    }

    [RelayCommand]
    async Task CopyDebugReport()
    {
        try
        {
            await Ui.CopyToClipboardAsync(await BuildDebugReportAsync());
            SetBanner("Debug report copied. Paste it into a bug report; it holds counts and settings, not your save. " +
                      "If the problem needs the save itself, use Save debug bundle (Game data screen).", "ok");
        }
        catch (Exception e) { ReportUnexpected("Could not make the debug report", e); }
    }

    [RelayCommand]
    async Task SaveDebugBundle()
    {
        try
        {
            var stem = Session.Current is { } o ? System.IO.Path.GetFileNameWithoutExtension(o.Name) : "editor";
            var path = await Ui.PickSaveFileAsync("Save debug bundle", $"zc-debug-{stem}-{DateTime.Now:yyyyMMdd-HHmm}.zip", "Zip archive", "zip");
            if (path == null) return;
            var report = await BuildDebugReportAsync();
            var save = Session.Current?.Path;
            var holds = await Task.Run(() => DebugReport.WriteBundle(Session.Workspace, path, report, save));
            SetBanner($"Saved {path} ({holds}" + (save != null ? "; the save is the file on disk, without pending edits" : "") +
                      "). Attach it to a bug report.", "ok");
        }
        catch (Exception e) { ReportUnexpected("Could not save the debug bundle", e); }
    }

    [RelayCommand] Task OpenIssues() => Ui.OpenUrlAsync(IssuesUrl);

    /// <summary>
    /// An error nothing expected: log it with its stack trace and say so in the banner. The save is safe either way, because it is only
    /// replaced by an atomic move of an already verified file.
    /// </summary>
    public void ReportUnexpected(string what, Exception e)
    {
        var log = CrashLog.Write(Session.Env.DataDir, e);
        SetBanner($"{what}: {e.Message} (an unexpected error; the save on disk is unchanged unless a success message said otherwise." +
                  (log != null ? $" Details: {log})" : ")"), "err");
        LastIncident = new DebugIncident($"{what}: {e.Message}", e, DateTime.Now);
    }

    [RelayCommand] void DismissBanner() => SetBanner(null);

    [RelayCommand]
    void Go(string key) => Navigate(key);

    /// <summary>Show a screen. <paramref name="args"/> are screen-specific (e.g. an operator guid and a tab for Personnel).</summary>
    public void Navigate(string key, params string[] args)
    {
        if (HasSave == false && key is not ("saves" or "gamedata")) key = "saves";
        // the profile file has only its own screen and Advanced; a campaign save has every screen but Profile
        bool profile = Session.Current?.IsProfile == true;
        if (profile && key is not ("profile" or "advanced" or "saves" or "gamedata")) key = "profile";
        if (!profile && key == "profile") key = HasSave ? "command" : "saves";
        foreach (var n in Nav) n.IsVisible = profile ? n.Key is "profile" or "advanced" : n.Key != "profile";
        if (BannerKind != "err") SetBanner(null);
        var old = Screen;
        CurrentKey = key;
        foreach (var n in Nav) n.IsActive = n.Key == key;
        SavesNav.IsActive = key == "saves";
        DataNav.IsActive = key == "gamedata";
        Screen = CreateScreen(key, args);
        old?.Dispose();
        OnPropertyChanged(nameof(ChangeSaveLabel));
    }

    ScreenViewModel CreateScreen(string key, string[] args)
    {
        var open = Session.Current;
        return key switch
        {
            "saves" => new SavesViewModel(this),
            "gamedata" => new GameDataViewModel(this),
            "profile" when open?.Profile != null => new ProfileViewModel(this, open),
            "command" when open != null => new CommandViewModel(this, open),
            "armory" when open != null => new ArmoryViewModel(this, open, args.FirstOrDefault()),
            "weapons" when open != null => new WeaponsViewModel(this, open),
            "upgrades" when open != null => new UpgradesViewModel(this, open, args.FirstOrDefault()),
            "advanced" when open != null => new AdvancedViewModel(this, open, args.FirstOrDefault()),
            "personnel" when open != null => new PersonnelViewModel(this, open, args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)),
            "medbay" when open != null => new MedbayViewModel(this, open),
            "galaxy" when open != null => new GalaxyViewModel(this, open),
            "combat" when open != null => new CombatViewModel(this, open),
            _ => new PlaceholderViewModel(key),
        };
    }

    /// <summary>Rebuild the current screen from the open save (after an apply, restore or database change).</summary>
    public void ReloadScreen()
    {
        var open = Session.Current;
        Header?.Dispose();
        Header = open != null ? new HeaderViewModel(this, open) : null;
        HasSave = open != null;
        PendingBar.Refresh();
        Navigate(CurrentKey is "" ? "saves" : CurrentKey);
    }

    public void OpenSave(SaveEntry entry)
    {
        try
        {
            var open = Session.Open(entry.Dir, entry.Name);
            Portraits.Reset();
            Header?.Dispose();
            Header = new HeaderViewModel(this, open);
            HasSave = true;
            PendingBar.Refresh();
            var running = Session.Service.GameRunning();
            Navigate(open.IsProfile ? "profile" : "command");
            if (running != null)
                SetBanner($"The game appears to be running ({running}). Close it before applying, or the game may overwrite your edit.", "err");
        }
        catch (EditException e) { SetBanner(e.Message, "err"); }
        catch (Exception e) { ReportUnexpected("Could not open the save", e); }
    }

    public async Task ApplyAsync(bool copy, bool force, string? copyName = null)
    {
        var open = Session.Current;
        if (open == null) return;
        int total = Session.Pending.Count, acts = Session.Pending.ActionCount;
        if (!copy)
        {
            var msg = $"Apply {total} change(s) to {open.Name}?"
                      + (acts > 0 ? $"\n\nThis includes {acts} action(s) such as starting upgrades or healing operators; upgrades finish at the next turn change." : "")
                      + "\n\nA backup is made first.";
            if (!await Ui.ConfirmAsync("Apply to save", msg)) return;
        }
        PendingBar.Busy = true;
        try
        {
            var batch = Session.PrepareApply();                          // on the UI thread, which owns the pending set
            var res = await Task.Run(() => Session.Write(batch, copy, force, copyName));
            Session.FinishApply(copy);
            SetBanner(copy ? $"Saved a copy: {res.Written}. The open save is unchanged and your edits are still pending." : $"Applied {res.Count} change(s). Backup: {res.Backup}", "ok");
            ReloadScreenAfterApply(copy);
        }
        catch (EditException e) { SetBanner(e.Message, "err"); }
        catch (Exception e) { ReportUnexpected("Applying failed", e); }
        finally { PendingBar.Busy = false; PendingBar.Refresh(); }
    }

    void ReloadScreenAfterApply(bool copy)
    {
        var keep = (BannerText, BannerKind);
        ReloadScreen();
        SetBanner(keep.BannerText, keep.BannerKind);
    }

    public async Task RestoreAsync(string file, bool force)
    {
        var open = Session.Current;
        if (open == null) return;
        if (!await Ui.ConfirmAsync("Restore backup", $"Restore {file} over the current save?")) return;
        var running = Session.Service.GameRunning();
        if (!force && running != null &&
            !await Ui.ConfirmAsync("Game running", $"The game appears to be running ({running}). If it has this save loaded it may overwrite the restore. Restore anyway?"))
            return;
        try
        {
            var (restored, previous) = await Task.Run(() => Session.Service.Restore(file, force || running != null));
            Session.Pending.Reset(Session.Current);
            ReloadScreen();
            SetBanner($"Restored {restored}. Previous version kept as {previous}", "ok");
        }
        catch (EditException e) { SetBanner(e.Message, "err"); }
        catch (Exception e) { ReportUnexpected("Restoring failed", e); }
    }

    /// <summary>The game database changed: re-read the open save so names and costs pick it up.</summary>
    public void OnDatabaseChanged()
    {
        var open = Session.Current;
        if (open != null)
        {
            bool kept = true;
            try { kept = Session.Reload(); }
            catch (EditException) { }
            catch (Exception e) { ReportUnexpected("Could not re-read the save", e); }
            ReloadScreen();
            if (!kept) SetBanner("The save changed on disk (the game may have autosaved), so your pending edits were discarded.", "err");
            return;
        }
        ReloadScreen();
    }
}

public sealed class PlaceholderViewModel : ScreenViewModel
{
    public string Key { get; }
    public PlaceholderViewModel(string key) { Key = key; }
}
