using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

public sealed partial class UpgradeNodeViewModel : ViewModelBase
{
    static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI" };

    public UpgradeNodeViewModel(UpgradeNodeView data, int index, Action<UpgradeNodeViewModel> select)
    {
        Data = data;
        Name = data.Name;
        Label = data.Status == "Completed" ? "✓" : index + 1 < Roman.Length ? Roman[index + 1] : (index + 1).ToString(CultureInfo.InvariantCulture);
        IsDone = data.Status == "Completed";
        IsBuilding = data.Status == "InProgress";
        IsOpen = !IsDone && !IsBuilding && data.CanStart;
        IsLocked = !IsDone && !IsBuilding && !data.CanStart;
        IsMajor = data.Major;
        Tooltip = $"{data.Title} — {data.Status}";
        SelectCommand = new RelayCommand(() => select(this));
    }

    public UpgradeNodeView Data { get; }
    public string Name { get; }
    public string Label { get; }
    public bool IsDone { get; }
    public bool IsBuilding { get; }
    public bool IsOpen { get; }
    public bool IsLocked { get; }
    public bool IsMajor { get; }
    public double Size => IsMajor ? 36 : 30;
    /// <summary>Room for the rotated square: its diagonal.</summary>
    public double BoxSize => Size * 1.45;
    public string Tooltip { get; }
    public System.Windows.Input.ICommand SelectCommand { get; }
    [ObservableProperty] bool _isSelected;
    [ObservableProperty] bool _isQueued;
}

public sealed class UpgradeCellViewModel
{
    public int Den { get; init; }
    public List<UpgradeNodeViewModel> Nodes { get; } = new();
}

public sealed class UpgradeRowViewModel
{
    public string Label { get; init; } = "";
    public List<UpgradeCellViewModel> Cells { get; } = new();
}

public sealed class DenAxisCellViewModel
{
    public string Text { get; init; } = "";
    public bool IsCurrent { get; init; }
    public bool IsPast { get; init; }
    public bool IsFuture => !IsCurrent && !IsPast;
}

public sealed class BuildSlotViewModel
{
    public BuildSlotViewModel(UpgradeNodeViewModel node)
    {
        Title = node.Data.Title;
        Remaining = node.Data.Remaining is long r ? $"⌛ {r}" : "";
        HasRemaining = Remaining.Length > 0;
        SelectCommand = node.SelectCommand;
    }

    public string Title { get; }
    public string Remaining { get; }
    public bool HasRemaining { get; }
    public System.Windows.Input.ICommand SelectCommand { get; }
}

/// <summary>
/// Upgrades: tabs (Facilities / Crew / Weapons), a row per upgrade line on a Den Level 1-10 timeline, Build Slots and a detail
/// panel. Starting and expediting are queued as the same actions the game's own buttons perform; nothing is charged.
/// </summary>
public sealed partial class UpgradesViewModel : ScreenViewModel
{
    const int Cols = 10;
    static readonly Dictionary<string, string> Res = new() { ["Credits"] = "credits", ["UpgradeFacilityResource"] = "capacitors" };

    readonly MainViewModel _main;
    readonly UpgradesView _view;
    readonly PendingChanges _pending;
    readonly List<UpgradeNodeViewModel> _nodes = new();   // current tab
    string _tab;

    public UpgradesViewModel(MainViewModel main, OpenSave open, string? tab)
    {
        _main = main;
        _pending = main.Session.Pending;
        _view = open.View.Upgrades ?? new UpgradesView();
        _tab = _view.Tabs.Any(t => t.Key == tab) ? tab! : _view.Tabs.Any(t => t.Key == main.Memory.UpgradeTab) ? main.Memory.UpgradeTab : "Facilities";
        foreach (var t in _view.Tabs) Tabs.Add(new TabViewModel(t.Key, t.Key, SelectTab));
        BuildTab();
        _pending.Changed += Refresh;
        Refresh();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<UpgradeRowViewModel> Rows { get; } = new();
    public ObservableCollection<DenAxisCellViewModel> Axis { get; } = new();
    public ObservableCollection<BuildSlotViewModel> Slots { get; } = new();
    public bool NoRows => Rows.Count == 0;
    public bool NoSlots => Slots.Count == 0;
    public string SlotsHeading => $"{_view.InProgress} in progress";

    // ---- toolbar
    public bool AlsoExpedite
    {
        get => _pending.AlsoExpedite;
        set => _pending.AlsoExpedite = value;
    }
    [ObservableProperty] string _queueAllLabel = "";
    [ObservableProperty] string _expediteAllLabel = "";
    [ObservableProperty] bool _canQueueAll;
    [ObservableProperty] bool _canExpediteAll;
    [ObservableProperty] bool _canClearQueue;

    // ---- detail
    [ObservableProperty] UpgradeNodeViewModel? _selected;
    [ObservableProperty] ToggleActionViewModel? _startToggle;
    [ObservableProperty] ToggleActionViewModel? _expediteToggle;
    public bool HasSelection => Selected != null;
    public bool NoSelection => Selected == null;
    public string DetailTitle => Selected?.Data.Title ?? "";
    public string DetailStatus => Selected == null ? "" : (Selected.Data.Major ? "Major upgrade" : "Upgrade") + " · " + (Selected.Data.Status == "InProgress" ? "Building" : Selected.Data.Status);
    public string DetailDescription => Selected?.Data.Description ?? "";
    public bool HasDescription => DetailDescription.Length > 0;
    public string DetailCost => Selected == null ? "" : CostLine(Selected.Data);
    public bool HasCost => DetailCost.Length > 0;
    public string DetailDen => Selected?.Data.Den is int d
        ? $"Den Level {d}" + (Selected.Data.DenMet ? "" : $" — you are at {_view.Level} (the editor can still start it)") : "";
    public bool HasDen => DetailDen.Length > 0;
    public bool DenWarn => Selected is { Data.DenMet: false };
    public string DetailReason => Selected?.Data.Reason ?? "";
    public bool HasReason => DetailReason.Length > 0;
    public bool IsCompleted => Selected?.IsDone == true;
    public bool AlreadyExpedited => Selected is { IsBuilding: true } s && !s.Data.CanExpedite;

    partial void OnSelectedChanged(UpgradeNodeViewModel? value)
    {
        _main.Memory.SelectedUpgrade = value?.Name;
        foreach (var n in _nodes) n.IsSelected = n == value;
        foreach (var p in new[] { nameof(HasSelection), nameof(NoSelection), nameof(DetailTitle), nameof(DetailStatus), nameof(DetailDescription),
                                  nameof(HasDescription), nameof(DetailCost), nameof(HasCost), nameof(DetailDen), nameof(HasDen), nameof(DenWarn),
                                  nameof(DetailReason), nameof(HasReason), nameof(IsCompleted), nameof(AlreadyExpedited) })
            OnPropertyChanged(p);
        BuildToggles();
    }

    static string CostLine(UpgradeNodeView n)
    {
        var parts = new List<string>();
        if (n.Duration is int d) parts.Add($"⌛ {d} {(d == 1 ? "turn" : "turns")}");
        foreach (var (k, v) in n.Cost) parts.Add($"{v.ToString("N0", CultureInfo.InvariantCulture)} {(Res.TryGetValue(k, out var r) ? r : k)}");
        return string.Join("  ·  ", parts);
    }

    void SelectTab(string key)
    {
        _tab = key;
        _main.Memory.UpgradeTab = key;
        BuildTab();
        Refresh();
    }

    void BuildTab()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _tab;
        var tv = _view.Tabs.First(t => t.Key == _tab);
        _nodes.Clear();
        Rows.Clear();
        foreach (var r in tv.Rows)
        {
            var row = new UpgradeRowViewModel { Label = r.Label };
            for (int c = 1; c <= Cols; c++) row.Cells.Add(new UpgradeCellViewModel { Den = c });
            for (int i = 0; i < r.Nodes.Count; i++)
            {
                var vm = new UpgradeNodeViewModel(r.Nodes[i], i, n => Selected = n);
                _nodes.Add(vm);
                row.Cells[Math.Clamp(r.Nodes[i].Den ?? 1, 1, Cols) - 1].Nodes.Add(vm);
            }
            Rows.Add(row);
        }
        Axis.Clear();
        for (int i = 1; i <= Cols; i++) Axis.Add(new DenAxisCellViewModel { Text = i.ToString(CultureInfo.InvariantCulture), IsCurrent = i == _view.Level, IsPast = i < _view.Level });
        Slots.Clear();
        foreach (var n in _view.Slots) { var vm = _nodes.FirstOrDefault(x => x.Name == n.Name); if (vm != null) Slots.Add(new BuildSlotViewModel(vm)); }
        OnPropertyChanged(nameof(NoRows)); OnPropertyChanged(nameof(NoSlots)); OnPropertyChanged(nameof(SlotsHeading));
        // pre-select something useful on arrival
        var want = _nodes.FirstOrDefault(n => n.Name == _main.Memory.SelectedUpgrade)
                   ?? _nodes.FirstOrDefault(n => n.IsBuilding) ?? _nodes.FirstOrDefault(n => n.IsOpen) ?? _nodes.FirstOrDefault();
        Selected = null;
        Selected = want;
    }

    void Refresh()
    {
        foreach (var n in _nodes)
            n.IsQueued = n.Data.Id != null && (_pending.Starts.Contains(n.Data.Id) || _pending.Expedites.Contains(n.Data.Id));
        var all = _view.Tabs.SelectMany(t => t.Rows).SelectMany(r => r.Nodes).ToList();
        var startable = all.Where(n => n.CanStart && n.Id != null).ToList();
        var expeditable = all.Where(n => n.CanExpedite && n.Id != null).ToList();
        QueueAllLabel = $"Queue all startable ({startable.Count})";
        ExpediteAllLabel = $"Expedite all building ({expeditable.Count})";
        CanQueueAll = startable.Count > 0;
        CanExpediteAll = expeditable.Count > 0;
        CanClearQueue = _pending.Starts.Count > 0 || _pending.Expedites.Count > 0;
        OnPropertyChanged(nameof(AlsoExpedite));
        BuildToggles();
    }

    void BuildToggles()
    {
        StartToggle?.Dispose(); ExpediteToggle?.Dispose();
        StartToggle = ExpediteToggle = null;
        var n = Selected?.Data;
        if (n?.Id == null) return;
        var id = n.Id;
        if (n.Status == "Available" && n.CanStart)
            StartToggle = new ToggleActionViewModel(_pending, () => _pending.Starts.Contains(id), () => _pending.ToggleStart(id),
                _pending.AlsoExpedite ? "Queue: start + expedite" : "Queue: start", "Remove from queue");
        else if (n.Status == "InProgress" && n.CanExpedite)
            ExpediteToggle = new ToggleActionViewModel(_pending, () => _pending.Expedites.Contains(id), () => _pending.ToggleExpedite(id),
                "Queue: expedite", "Remove from queue");
    }

    [RelayCommand]
    void QueueAll()
    {
        var all = _view.Tabs.SelectMany(t => t.Rows).SelectMany(r => r.Nodes);
        _pending.QueueUpgrades(all.Where(n => n.CanStart && n.Id != null).Select(n => n.Id!), Array.Empty<string>());
    }

    [RelayCommand]
    void ExpediteAll()
    {
        var all = _view.Tabs.SelectMany(t => t.Rows).SelectMany(r => r.Nodes);
        _pending.QueueUpgrades(Array.Empty<string>(), all.Where(n => n.CanExpedite && n.Id != null).Select(n => n.Id!));
    }

    [RelayCommand] void ClearQueue() => _pending.ClearUpgradeQueue();

    public override void Dispose()
    {
        _pending.Changed -= Refresh;
        StartToggle?.Dispose(); ExpediteToggle?.Dispose();
        base.Dispose();
    }
}
