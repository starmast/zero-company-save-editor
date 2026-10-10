using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>One operator in the roster strip at the bottom of Personnel.</summary>
public sealed partial class OperatorChipViewModel : ViewModelBase
{
    public OperatorChipViewModel(OperatorView op, FaceViewModel face, Action<string> select)
    {
        Guid = op.Guid; Name = op.Name; Face = face; IsDead = op.Dead;
        IsInjured = op.Injuries != 0 && !op.Dead;
        HasFocus = op.Focus != null;
        SelectCommand = new RelayCommand(() => select(op.Guid));
    }

    public string Guid { get; }
    public string Name { get; }
    public FaceViewModel Face { get; }
    public bool IsDead { get; }
    public bool IsLive => !IsDead;
    public bool IsInjured { get; }
    public bool HasFocus { get; }
    public System.Windows.Input.ICommand SelectCommand { get; }
    [ObservableProperty] string _unspent = "";
    [ObservableProperty] bool _isSelected;
    [ObservableProperty] bool _dropBefore;
    [ObservableProperty] bool _dropAfter;
    [ObservableProperty] bool _isDragging;
}

/// <summary>Personnel: roster strip (portraits) plus Overview / Bonds / Focus Tree for the chosen operator, as in the game.</summary>
public sealed partial class PersonnelViewModel : ScreenViewModel
{
    readonly MainViewModel _main;
    readonly OpenSave _open;
    readonly PendingChanges _pending;
    readonly Dictionary<string, FaceViewModel> _faces = new();
    readonly Dictionary<string, OperatorView> _byGuid = new();
    string _tab;
    string _selected = "";
    ToggleActionViewModel? _revive;
    ToggleActionViewModel? _fixTree;

    public PersonnelViewModel(MainViewModel main, OpenSave open, string? guid, string? tab)
    {
        _main = main; _open = open; _pending = main.Session.Pending;
        var p = open.View.Personnel;
        foreach (var o in p.Roster.Concat(p.Memorial))
        {
            _byGuid[o.Guid] = o;
            _faces[o.Guid] = new FaceViewModel(o.Name, o.HasPortrait ? main.Portraits.Get(open, o.Guid) : null);
        }
        _tab = tab is "overview" or "bonds" or "focus" ? tab : main.Memory.PersonnelTab;
        foreach (var (k, l) in new[] { ("overview", "Overview"), ("bonds", "Bonds"), ("focus", "Focus Tree") })
            Tabs.Add(new TabViewModel(k, l, SelectTab));
        _selected = guid != null && _byGuid.ContainsKey(guid) ? guid
                  : main.Memory.SelectedOperator != null && _byGuid.ContainsKey(main.Memory.SelectedOperator) ? main.Memory.SelectedOperator
                  : p.Roster.Concat(p.Memorial).FirstOrDefault()?.Guid ?? "";
        _pending.Changed += OnPending;
        BuildStrip();
        BuildSelection();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<OperatorChipViewModel> Live { get; } = new();
    public ObservableCollection<OperatorChipViewModel> Memorial { get; } = new();
    public bool HasMemorial => Memorial.Count > 0;
    public bool HasOperators => _byGuid.Count > 0;
    public bool NoOperators => _byGuid.Count == 0;

    [ObservableProperty] PersonnelTabViewModel? _content;
    [ObservableProperty] FaceViewModel? _selectedFace;
    [ObservableProperty] string _selectedName = "";
    [ObservableProperty] string _selectedRole = "";
    [ObservableProperty] string _unspentText = "";
    [ObservableProperty] bool _showUnspent;
    [ObservableProperty] bool _showCross;
    [ObservableProperty] string _crossText = "";
    [ObservableProperty] bool _selectedIsDead;
    [ObservableProperty] string _reviveText = "";
    [ObservableProperty] ToggleActionViewModel? _reviveToggle;
    [ObservableProperty] string _positionText = "";
    [ObservableProperty] bool _canMoveEarlier;
    [ObservableProperty] bool _canMoveLater;
    [ObservableProperty] bool _treeIncomplete;
    [ObservableProperty] string _treeText = "";
    [ObservableProperty] ToggleActionViewModel? _treeToggle;

    public bool SelectedIsLive => !SelectedIsDead;
    public string SelectedGuid => _selected;

    // ---------------------------------------------------------------------------------------- strip
    IReadOnlyList<string> Order() => _pending.RosterOrder(_open.View);

    void BuildStrip()
    {
        Live.Clear(); Memorial.Clear();
        foreach (var g in Order().Where(_byGuid.ContainsKey)) Live.Add(Chip(_byGuid[g]));
        foreach (var o in _open.View.Personnel.Memorial) Memorial.Add(Chip(o));
        OnPropertyChanged(nameof(HasMemorial));
        RefreshChips();
    }

    OperatorChipViewModel Chip(OperatorView o) => new(o, _faces[o.Guid], SelectOperator);

    void RefreshChips()
    {
        foreach (var c in Live.Concat(Memorial))
        {
            c.IsSelected = c.Guid == _selected;
            if (_byGuid[c.Guid].Focus is { } f) c.Unspent = _pending.GetValue(f).ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Called by the strip when a drag ends: drop <paramref name="guid"/> after <paramref name="index"/> other operators.</summary>
    public void MoveTo(string guid, int index) => _pending.MoveOperatorTo(guid, index, _open.View);

    public void ClearDropMarks()
    {
        foreach (var c in Live) { c.DropBefore = c.DropAfter = c.IsDragging = false; }
    }

    // ---------------------------------------------------------------------------------------- selection
    void SelectOperator(string guid)
    {
        _selected = guid;
        _main.Memory.SelectedOperator = guid;
        RefreshChips();
        BuildSelection();
    }

    void SelectTab(string key)
    {
        _tab = key;
        _main.Memory.PersonnelTab = key;
        BuildSelection();
    }

    void OnPending()
    {
        // the roster order may have changed (drag or Earlier/Later); everything else only needs light updates
        var order = Order().Where(_byGuid.ContainsKey).ToList();
        if (!order.SequenceEqual(Live.Select(c => c.Guid))) BuildStrip(); else RefreshChips();
        RefreshSide();
    }

    void BuildSelection()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _tab;
        Content?.Dispose();
        _byGuid.TryGetValue(_selected, out var op);
        if (op == null) { Content = null; return; }
        _main.Memory.PersonnelTab = _tab;
        var face = _faces[op.Guid];
        SelectedFace = face;
        SelectedName = op.Name;
        SelectedIsDead = op.Dead;
        SelectedRole = op.Dead ? "Fallen" : op.Role;
        OnPropertyChanged(nameof(SelectedIsLive));
        Content = _tab switch
        {
            "bonds" => new BondsTabViewModel(_main, op, _faces),
            "focus" => new FocusTabViewModel(_main, op),
            _ => new OverviewTabViewModel(_main, op, face),
        };
        ShowUnspent = _tab != "overview" || op.Focus != null;
        ShowCross = _tab == "bonds";

        _revive?.Dispose(); _fixTree?.Dispose();
        ReviveToggle = null; TreeToggle = null;
        if (op.Dead)
        {
            _revive = new ToggleActionViewModel(_pending, () => _pending.Revives.Contains(op.Guid), () => _pending.ToggleRevive(op.Guid), "Bring back");
            ReviveToggle = _revive;
            ReviveText = $"Puts {op.Name} back on the roster as the last operator, removes the death and injury effects, and records the current turn as their "
                         + "recruited turn. Bonds with operators recruited after their death do not exist yet, and the roster's permanent health bonus is not added, so their Health can read lower.";
        }
        TreeIncomplete = op.TreeIncomplete && _tab == "focus";
        if (op.TreeIncomplete)
        {
            _fixTree = new ToggleActionViewModel(_pending, () => _pending.TreeFixes.Contains(op.Guid), () => _pending.ToggleTreeFix(op.Guid), "Complete tree");
            TreeToggle = _fixTree;
            TreeText = $"{op.Name}'s focus tree is missing the records for the higher tiers of some abilities, so the game cannot show or level them. "
                       + "This copies the missing tiers from another operator who has the same abilities (they are the same for everyone).";
        }
        RefreshSide();
    }

    void RefreshSide()
    {
        if (!_byGuid.TryGetValue(_selected, out var op)) return;
        UnspentText = op.Focus != null ? _pending.GetValue(op.Focus).ToString("N0", CultureInfo.InvariantCulture) : "0";
        var all = _open.View.Personnel.Roster.Concat(_open.View.Personnel.Memorial);
        double cross = all.SelectMany(o => o.Bonds.Where(b => b.Cross != null && string.CompareOrdinal(o.Guid, b.Partner) < 0))
                          .Sum(b => _pending.GetValue(b.Cross!));
        CrossText = cross.ToString("N0", CultureInfo.InvariantCulture);
        var order = Order();
        int i = order.ToList().IndexOf(op.Guid);
        PositionText = op.Dead ? "" : $"Position {i + 1} of {order.Count}. The game's strip follows this order.";
        CanMoveEarlier = !op.Dead && i > 0;
        CanMoveLater = !op.Dead && i >= 0 && i < order.Count - 1;
    }

    [RelayCommand] void MoveEarlier() => _pending.MoveOperator(_selected, -1, _open.View);
    [RelayCommand] void MoveLater() => _pending.MoveOperator(_selected, 1, _open.View);

    public override void Dispose()
    {
        _pending.Changed -= OnPending;
        Content?.Dispose();
        _revive?.Dispose(); _fixTree?.Dispose();
        base.Dispose();
    }
}
