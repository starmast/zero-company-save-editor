using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>One scenery prop: its condition, an editable health when the game has one, and Destroy / Restore toggles.</summary>
public sealed class PropRowViewModel : IDisposable
{
    readonly ToggleActionViewModel? _destroy, _restore;

    public PropRowViewModel(PendingChanges pending, FieldSet fields, PropView p)
    {
        Title = p.Title; Area = p.Area; Condition = p.Condition;
        Health = p.Health == null ? null : fields.Make(p.Health, 0, "Health");
        Max = p.MaxHealth == null ? "" : $"of {p.MaxHealth.Value:0}";
        if (p.Condition != "Destroyed")
            _destroy = new ToggleActionViewModel(pending, () => pending.SceneryDestroys.Contains(p.Actor), () => pending.ToggleSceneryDestroy(p.Actor),
                "Destroy", "Queued - undo", "Wrecks this prop when the save loads");
        if (p.Condition != "Pristine")
            _restore = new ToggleActionViewModel(pending, () => pending.SceneryRestores.Contains(p.Actor), () => pending.ToggleSceneryRestore(p.Actor),
                "Restore", "Queued - undo", "Rebuilds this prop at full health. Setting health alone does not: the save also marks the prop destroyed or damaged.");
    }

    public string Title { get; }
    public string Area { get; }
    /// <summary>Pristine, Damaged or Destroyed; shown as "Intact" for pristine.</summary>
    public string Condition { get; }
    public string ConditionText => Condition == "Pristine" ? "Intact" : Condition;
    public EditableField? Health { get; }
    public string Max { get; }
    public ToggleActionViewModel? Destroy => _destroy;
    public ToggleActionViewModel? Restore => _restore;
    public bool HasHealth => Health != null;
    public bool CanDestroy => _destroy != null;
    public bool CanRestore => _restore != null;

    public void Dispose() { _destroy?.Dispose(); _restore?.Dispose(); }
}

/// <summary>
/// The Scenery list: every prop the save tracks (hundreds in a real level), with a search, a condition filter and bulk Destroy / Restore for
/// whatever the filters currently show. Rows are built only for what is on screen.
/// </summary>
public sealed partial class SceneryPanelViewModel : ViewModelBase, IDisposable
{
    /// <summary>The most rows drawn at once; bulk buttons still act on every match.</summary>
    public const int MaxShown = 150;

    readonly PendingChanges _pending;
    readonly FieldSet _fields;
    readonly IReadOnlyList<PropView> _props;
    readonly Dictionary<string, PropRowViewModel> _rows = new();
    List<PropView> _matches = new();

    public SceneryPanelViewModel(PendingChanges pending, FieldSet fields, IReadOnlyList<PropView> props)
    {
        _pending = pending; _fields = fields; _props = props;
        ConditionChoices = new[]
        {
            new GroupChoice("", "Any condition"), new GroupChoice("Pristine", "Intact"),
            new GroupChoice("Damaged", "Damaged"), new GroupChoice("Destroyed", "Destroyed"),
        };
        _selCondition = ConditionChoices[0];
        DestroyShown = new RelayCommand(() => _pending.QueueSceneryDestroys(_matches.Where(p => p.Condition != "Destroyed").Select(p => p.Actor)));
        RestoreShown = new RelayCommand(() => _pending.QueueSceneryRestores(_matches.Where(p => p.Condition != "Pristine").Select(p => p.Actor)));
        ClearQueued = new RelayCommand(() => _pending.ClearScenery());
        ClearFilters = new RelayCommand(() => { _search = ""; _selCondition = ConditionChoices[0]; _queuedOnly = false; OnPropertyChanged(nameof(Search)); OnPropertyChanged(nameof(SelCondition)); OnPropertyChanged(nameof(QueuedOnly)); Rebuild(); });
        _pending.Changed += OnPendingChanged;
        Rebuild();
    }

    public ObservableCollection<PropRowViewModel> Visible { get; } = new();
    public IReadOnlyList<GroupChoice> ConditionChoices { get; }
    [ObservableProperty] string _search = "";
    [ObservableProperty] GroupChoice _selCondition;
    [ObservableProperty] bool _queuedOnly;
    [ObservableProperty] string _summary = "";
    [ObservableProperty] string _destroyShownText = "";
    [ObservableProperty] string _restoreShownText = "";
    public System.Windows.Input.ICommand DestroyShown { get; }
    public System.Windows.Input.ICommand RestoreShown { get; }
    public System.Windows.Input.ICommand ClearQueued { get; }
    public System.Windows.Input.ICommand ClearFilters { get; }
    public bool IsEmpty => Visible.Count == 0;
    public int MatchCount => _matches.Count;

    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnSelConditionChanged(GroupChoice value) => Rebuild();
    partial void OnQueuedOnlyChanged(bool value) => Rebuild();

    SceneryQuery Query => new((Search ?? "").Trim(), SelCondition.Value, QueuedOnly);

    bool Queued(string actor) => _pending.SceneryDestroys.Contains(actor) || _pending.SceneryRestores.Contains(actor);

    void OnPendingChanged()
    {
        if (QueuedOnly) Rebuild(); else UpdateSummary();
    }

    void Rebuild()
    {
        _matches = SceneryFilter.Run(_props, Query, Queued);
        Visible.Clear();
        foreach (var p in _matches.Take(MaxShown))
        {
            if (!_rows.TryGetValue(p.Actor, out var row)) _rows[p.Actor] = row = new PropRowViewModel(_pending, _fields, p);
            Visible.Add(row);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(MatchCount));
        UpdateSummary();
    }

    void UpdateSummary()
    {
        int destroyable = _matches.Count(p => p.Condition != "Destroyed"), restorable = _matches.Count(p => p.Condition != "Pristine");
        DestroyShownText = $"Destroy all {destroyable} shown";
        RestoreShownText = $"Restore all {restorable} shown";
        var shown = _matches.Count > MaxShown ? $"Showing {MaxShown} of {_matches.Count} matches (search to narrow them; the buttons below act on all {_matches.Count})"
                                              : $"Showing {_matches.Count} of {_props.Count}";
        var queued = new List<string>();
        if (_pending.SceneryDestroys.Count > 0) queued.Add($"{_pending.SceneryDestroys.Count} queued to destroy");
        if (_pending.SceneryRestores.Count > 0) queued.Add($"{_pending.SceneryRestores.Count} queued to restore");
        Summary = shown + (queued.Count > 0 ? " · " + string.Join(", ", queued) : "");
    }

    public void Dispose()
    {
        _pending.Changed -= OnPendingChanged;
        foreach (var r in _rows.Values) r.Dispose();
        _rows.Clear();
    }
}
