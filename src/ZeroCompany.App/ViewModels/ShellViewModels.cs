using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.ViewModels;

/// <summary>One number in the top bar (Credits, Intel, LV, Turn ...). Click to edit it in a flyout.</summary>
public sealed partial class ChipViewModel : ViewModelBase
{
    readonly EditableField _field;
    readonly string _prefix;

    public ChipViewModel(string icon, string label, EditableField field, string prefix = "")
    {
        Icon = icon; Label = label; _field = field; _prefix = prefix;
        _field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditableField.Value) or nameof(EditableField.IsChanged))
            {
                OnPropertyChanged(nameof(Display)); OnPropertyChanged(nameof(IsChanged));
            }
        };
    }

    public string Icon { get; }
    public string Label { get; }
    public EditableField Field => _field;
    public bool IsChanged => _field.IsChanged;
    public string Display => _prefix + _field.Value.ToString("N0", CultureInfo.InvariantCulture);
}

public sealed class HeaderViewModel : ViewModelBase, IDisposable
{
    readonly FieldSet _fields;
    static readonly Dictionary<string, string> Icons = new()
    {
        ["Credits"] = "◈", ["Intelligence"] = "✦", ["UpgradeFacilityResource"] = "▣", ["Contacts"] = "☍",
    };

    public HeaderViewModel(MainViewModel main, OpenSave open)
    {
        _fields = new FieldSet(main.Session.Pending);
        var hd = open.View.Header;
        Title = open.View.Command.Save.Title?.ToString() is { Length: > 0 } t ? t : open.Name;
        Subtitle = $"{open.Name} · {open.DirId}";
        foreach (var r in hd.Resources)
            Chips.Add(new ChipViewModel(Icons.GetValueOrDefault(r.Key, "◇"), r.Label, _fields.Make(r, 0, r.Label)!));
        if (hd.Level != null) Chips.Add(new ChipViewModel("⬡", "Level (LV)", _fields.Make(hd.Level, hd.Level.Offset, "Level")!, "LV "));
        if (hd.Turn != null) Chips.Add(new ChipViewModel("⟳", "Strategy turn", _fields.Make(hd.Turn, 0, "Turn")!, "Turn "));
    }

    public string Title { get; }
    public string Subtitle { get; }
    public ObservableCollection<ChipViewModel> Chips { get; } = new();

    public void Dispose() => _fields.Dispose();
}

/// <summary>The bar at the bottom: how many edits are pending, what they are, and Apply / Copy / Discard.</summary>
public sealed partial class PendingBarViewModel : ViewModelBase
{
    readonly MainViewModel _main;

    public PendingBarViewModel(MainViewModel main)
    {
        _main = main;
        main.Session.Pending.Changed += Refresh;
    }

    [ObservableProperty] int _count;
    [ObservableProperty] bool _isOpen;
    [ObservableProperty] bool _busy;
    [ObservableProperty] bool _force;
    [ObservableProperty] string? _gameRunning;

    public ObservableCollection<PendingLineViewModel> Lines { get; } = new();
    public bool HasChanges => Count > 0;
    public bool ShowGameWarning => GameRunning != null;
    public bool CanApply => Count > 0 && !Busy;
    public string Summary => $"{Count} pending change{(Count == 1 ? "" : "s")}{(IsOpen ? " ▾" : " ▸")}";
    public string ApplyLabel => Busy ? "Writing & verifying…" : "Apply to save";

    partial void OnCountChanged(int value) { OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(Summary)); }
    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(Summary));
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(ApplyLabel)); }
    partial void OnGameRunningChanged(string? value) => OnPropertyChanged(nameof(ShowGameWarning));

    public void Refresh()
    {
        var s = _main.Session;
        Count = s.Pending.Count;
        Lines.Clear();
        if (s.Current != null && Count > 0)
            foreach (var l in s.Pending.Describe(s.Current)) Lines.Add(new PendingLineViewModel(l));
        GameRunning = s.Current != null && Count > 0 ? s.Service.GameRunning() : null;
    }

    [RelayCommand] void Toggle() => IsOpen = !IsOpen;
    [RelayCommand] void Discard() { _main.Session.Pending.Discard(); _main.ReloadScreen(); }
    [RelayCommand] Task ApplyCopy() => _main.ApplyAsync(copy: true, force: Force);
    [RelayCommand] Task Apply() => _main.ApplyAsync(copy: false, force: Force);
}

public sealed class PendingLineViewModel
{
    public PendingLineViewModel(PendingLine l)
    {
        Where = l.Where;
        Label = l.Text ?? l.Label;
        IsAction = l.Text != null;
        From = l.From is double f ? f.ToString("N0", CultureInfo.InvariantCulture) + " →" : "";
        To = l.To is double t ? t.ToString("N0", CultureInfo.InvariantCulture) : "";
    }

    public string Where { get; }
    public string Label { get; }
    public bool IsAction { get; }
    public string From { get; }
    public string To { get; }
}
