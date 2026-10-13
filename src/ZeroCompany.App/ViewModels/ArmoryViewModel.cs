using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

public sealed class ArmoryCardViewModel
{
    public ArmoryCardViewModel(ArmoryItemView it, EditableField count)
    {
        Name = it.Name;
        Meta = string.Join(" · ", new[] { it.Tier is > 0 ? $"Tier {it.Tier}" : null, string.IsNullOrEmpty(it.Rarity) ? null : it.Rarity }.Where(s => s != null));
        Description = it.Description;
        Count = count;
        Search = (it.Name + " " + it.Description).ToLowerInvariant();
        Kind = it.Kind;
        UseStepper = it.Count!.Value <= 99;
    }

    public string Name { get; }
    public string Meta { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public EditableField Count { get; }
    public string Search { get; }
    public string Kind { get; }
    public bool UseStepper { get; }
    public bool UseNumber => !UseStepper;
}

/// <summary>An item the game defines, with how many the squad owns and how many are queued to be added.</summary>
public sealed class AddItemCardViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly string _asset;

    public AddItemCardViewModel(PendingChanges pending, ArmoryAddView it)
    {
        _pending = pending; _asset = it.Asset; Item = it;
        Name = it.Name;
        var what = it.Kind == "Utility" ? "Utility item" : it.Slot.Length > 0 ? $"Weapon mod · {it.Slot}" : "Weapon mod";
        Meta = string.Join(" · ", new[] { what, string.IsNullOrEmpty(it.Rarity) ? null : it.Rarity }.Where(s => s != null));
        Description = it.Description;
        Kind = it.Kind;
        Owned = it.Owned == 0 ? "NEVER OWNED" : $"OWNED {it.Owned}";
        NeverOwned = it.Owned == 0;
        Plus = new RelayCommand(() => _pending.SetItemAdd(_asset, _pending.ItemAddCount(_asset) + 1));
        Minus = new RelayCommand(() => _pending.SetItemAdd(_asset, _pending.ItemAddCount(_asset) - 1));
        _pending.Changed += OnChanged;
    }

    /// <summary>The item as the view layer describes it (what the filter and sort work on).</summary>
    public ArmoryAddView Item { get; }
    public string Name { get; }
    public string Meta { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public string Kind { get; }
    public string Owned { get; }
    public bool NeverOwned { get; }
    public int Queued => _pending.ItemAddCount(_asset);
    public bool IsQueued => Queued > 0;
    public string QueuedText => IsQueued ? $"+{Queued}" : "0";
    public System.Windows.Input.ICommand Plus { get; }
    public System.Windows.Input.ICommand Minus { get; }

    void OnChanged() { OnPropertyChanged(nameof(Queued)); OnPropertyChanged(nameof(IsQueued)); OnPropertyChanged(nameof(QueuedText)); }
    public void Dispose() => _pending.Changed -= OnChanged;
}

/// <summary>Armory: Utility Items and Weapon Mods as cards (name, tier, rarity, description, count), plus a tab to add items you never had.</summary>
public sealed partial class ArmoryViewModel : ScreenViewModel
{
    static readonly (string Key, string Label)[] Kinds =
    {
        ("Utility", "Utility items"), ("Modification", "Weapon mods"), ("Other", "Other"),
    };

    static readonly GroupChoice[] SortChoicesList =
    {
        new(nameof(AddSort.Name), "Name (A to Z)"),
        new(nameof(AddSort.TierLow), "Tier (low to high)"),
        new(nameof(AddSort.TierHigh), "Tier (high to low)"),
        new(nameof(AddSort.Rarity), "Rarity (rarest first)"),
        new(nameof(AddSort.FewestOwned), "Owned (fewest first)"),
    };

    readonly MainViewModel _main;
    readonly PendingChanges _pending;
    readonly List<ArmoryCardViewModel> _all = new();
    readonly List<AddItemCardViewModel> _addable = new();
    const string AddKey = "Add";

    public ArmoryViewModel(MainViewModel main, OpenSave open, string? tab)
    {
        _main = main;
        _pending = main.Session.Pending;
        var fields = Own(new FieldSet(main.Session.Pending));
        foreach (var it in open.View.Armory.Items) _all.Add(new ArmoryCardViewModel(it, fields.Make(it.Count, 0, it.Name)!));
        foreach (var a in open.View.Armory.Addable) _addable.Add(Own(new AddItemCardViewModel(main.Session.Pending, a)));
        HasGameData = _addable.Count > 0;

        var views = open.View.Armory.Addable;
        GroupChoices = ArmoryAddFilter.Groups(views).Select(g => new GroupChoice(g.Value, g.Label)).ToList();
        TierChoices = new[] { new GroupChoice("0", "Any tier") }.Concat(ArmoryAddFilter.Tiers(views).Select(t => new GroupChoice(t.ToString(), $"Tier {t}"))).ToList();
        RarityChoices = new[] { new GroupChoice("", "Any rarity") }.Concat(ArmoryAddFilter.Rarities(views).Select(r => new GroupChoice(r, r))).ToList();
        SortChoices = SortChoicesList;
        _selGroup = GroupChoices[0]; _selTier = TierChoices[0]; _selRarity = RarityChoices[0]; _selSort = SortChoices[0];

        foreach (var (k, l) in Kinds)
        {
            int n = _all.Count(c => c.Kind == k);
            if (n > 0) Tabs.Add(new TabViewModel(k, $"{l} {n}", Select));
        }
        Tabs.Add(new TabViewModel(AddKey, HasGameData ? $"Add items {_addable.Count}" : "Add items", Select));
        var want = tab ?? main.Memory.ArmoryTab;
        _active = Tabs.Any(t => t.Key == want) ? want : Tabs.FirstOrDefault()?.Key ?? "Utility";
        _filter = main.Memory.ArmoryFilter;
        ClearFilters = new RelayCommand(ResetAddFilters);
        _pending.Changed += OnPendingChanged;
        Rebuild();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<ArmoryCardViewModel> Visible { get; } = new();
    public ObservableCollection<AddItemCardViewModel> VisibleAdd { get; } = new();
    public bool HasGameData { get; }
    public bool ShowAdd => _active == AddKey;
    public bool ShowOwned => !ShowAdd;
    public bool NoGameData => ShowAdd && !HasGameData;
    public bool ShowAddTools => ShowAdd && HasGameData;
    string _active;

    // ---- Add items: filters and sort -------------------------------------------------
    public IReadOnlyList<GroupChoice> GroupChoices { get; }
    public IReadOnlyList<GroupChoice> TierChoices { get; }
    public IReadOnlyList<GroupChoice> RarityChoices { get; }
    public IReadOnlyList<GroupChoice> SortChoices { get; }
    [ObservableProperty] GroupChoice _selGroup;
    [ObservableProperty] GroupChoice _selTier;
    [ObservableProperty] GroupChoice _selRarity;
    [ObservableProperty] GroupChoice _selSort;
    [ObservableProperty] bool _neverOwnedOnly;
    [ObservableProperty] bool _queuedOnly;
    [ObservableProperty] string _addSummary = "";
    public System.Windows.Input.ICommand ClearFilters { get; }

    partial void OnSelGroupChanged(GroupChoice value) => Rebuild();
    partial void OnSelTierChanged(GroupChoice value) => Rebuild();
    partial void OnSelRarityChanged(GroupChoice value) => Rebuild();
    partial void OnSelSortChanged(GroupChoice value) => Rebuild();
    partial void OnNeverOwnedOnlyChanged(bool value) => Rebuild();
    partial void OnQueuedOnlyChanged(bool value) => Rebuild();

    /// <summary>What the Add items tab is currently asking for.</summary>
    public AddQuery CurrentQuery => new(
        (Filter ?? "").Trim(), SelGroup.Value, int.TryParse(SelTier.Value, out var t) ? t : 0, SelRarity.Value,
        NeverOwnedOnly, QueuedOnly, Enum.TryParse<AddSort>(SelSort.Value, out var s) ? s : AddSort.Name);

    void ResetAddFilters()
    {
        _selGroup = GroupChoices[0]; _selTier = TierChoices[0]; _selRarity = RarityChoices[0]; _selSort = SortChoices[0];
        _neverOwnedOnly = false; _queuedOnly = false; _filter = "";
        _main.Memory.ArmoryFilter = "";
        foreach (var n in new[] { nameof(SelGroup), nameof(SelTier), nameof(SelRarity), nameof(SelSort), nameof(NeverOwnedOnly), nameof(QueuedOnly), nameof(Filter) })
            OnPropertyChanged(n);
        Rebuild();
    }

    void OnPendingChanged()
    {
        if (ShowAdd && QueuedOnly) Rebuild();            // an item that is no longer queued leaves the "queued only" list
        else UpdateSummary();
    }

    void UpdateSummary()
    {
        int copies = _pending.ItemAdds.Values.Sum();
        AddSummary = $"Showing {VisibleAdd.Count} of {_addable.Count}" + (copies > 0 ? $" · {copies} cop{(copies == 1 ? "y" : "ies")} queued" : "");
    }

    [ObservableProperty] string _filter;
    [ObservableProperty] string _emptyText = "";
    public bool IsEmpty => ShowAdd ? VisibleAdd.Count == 0 : Visible.Count == 0;

    partial void OnFilterChanged(string value) { _main.Memory.ArmoryFilter = value; Rebuild(); }

    void Select(string key)
    {
        _active = key;
        _main.Memory.ArmoryTab = key;
        Rebuild();
    }

    public override void Dispose()
    {
        _pending.Changed -= OnPendingChanged;
        base.Dispose();
    }

    void Rebuild()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _active;
        Visible.Clear();
        VisibleAdd.Clear();
        if (ShowAdd)
        {
            var byAsset = _addable.ToDictionary(c => c.Item.Asset);
            var shown = ArmoryAddFilter.Run(_addable.Select(c => c.Item), CurrentQuery, a => _pending.ItemAddCount(a));
            foreach (var it in shown) VisibleAdd.Add(byAsset[it.Asset]);
            EmptyText = HasGameData ? "Nothing matches these filters."
                : "The game's item list comes from the game data: open the Game data tab and extract it once, then the items you can add appear here.";
            UpdateSummary();
        }
        else
        {
            var q = (Filter ?? "").Trim().ToLowerInvariant();
            foreach (var c in _all.Where(c => c.Kind == _active && (q.Length == 0 || c.Search.Contains(q)))) Visible.Add(c);
            EmptyText = _all.Count == 0 ? "Nothing in the inventory yet." : "Nothing matches.";
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowAdd));
        OnPropertyChanged(nameof(ShowOwned));
        OnPropertyChanged(nameof(NoGameData));
        OnPropertyChanged(nameof(ShowAddTools));
    }
}
