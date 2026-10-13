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
        _pending = pending; _asset = it.Asset;
        Name = it.Name;
        Meta = string.Join(" · ", new[] { it.Kind == "Utility" ? "Utility item" : "Weapon mod", string.IsNullOrEmpty(it.Rarity) ? null : it.Rarity }.Where(s => s != null));
        Description = it.Description;
        Kind = it.Kind;
        Owned = it.Owned == 0 ? "NEVER OWNED" : $"OWNED {it.Owned}";
        NeverOwned = it.Owned == 0;
        Search = (it.Name + " " + it.Description).ToLowerInvariant();
        Plus = new RelayCommand(() => _pending.SetItemAdd(_asset, _pending.ItemAddCount(_asset) + 1));
        Minus = new RelayCommand(() => _pending.SetItemAdd(_asset, _pending.ItemAddCount(_asset) - 1));
        _pending.Changed += OnChanged;
    }

    public string Name { get; }
    public string Meta { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public string Kind { get; }
    public string Owned { get; }
    public bool NeverOwned { get; }
    public string Search { get; }
    public int Queued => _pending.ItemAddCount(_asset);
    public bool IsQueued => Queued > 0;
    public string QueuedText => IsQueued ? $"+{Queued}" : "0";
    public System.Windows.Input.ICommand Plus { get; }
    public System.Windows.Input.ICommand Minus { get; }

    void OnChanged() { OnPropertyChanged(nameof(Queued)); OnPropertyChanged(nameof(IsQueued)); OnPropertyChanged(nameof(QueuedText)); }
    public void Dispose() => _pending.Changed -= OnChanged;
}

/// <summary>Armory: Utility Items and Weapon Mods as cards (name, tier, rarity, description, count).</summary>
public sealed partial class ArmoryViewModel : ScreenViewModel
{
    static readonly (string Key, string Label)[] Kinds =
    {
        ("Utility", "Utility items"), ("Modification", "Weapon mods"), ("Other", "Other"),
    };

    readonly MainViewModel _main;
    readonly List<ArmoryCardViewModel> _all = new();
    readonly List<AddItemCardViewModel> _addable = new();
    const string AddKey = "Add";

    public ArmoryViewModel(MainViewModel main, OpenSave open, string? tab)
    {
        _main = main;
        var fields = Own(new FieldSet(main.Session.Pending));
        foreach (var it in open.View.Armory.Items) _all.Add(new ArmoryCardViewModel(it, fields.Make(it.Count, 0, it.Name)!));
        foreach (var a in open.View.Armory.Addable) _addable.Add(Own(new AddItemCardViewModel(main.Session.Pending, a)));
        HasGameData = _addable.Count > 0;
        foreach (var (k, l) in Kinds)
        {
            int n = _all.Count(c => c.Kind == k);
            if (n > 0) Tabs.Add(new TabViewModel(k, $"{l} {n}", Select));
        }
        Tabs.Add(new TabViewModel(AddKey, HasGameData ? $"Add items {_addable.Count}" : "Add items", Select));
        var want = tab ?? main.Memory.ArmoryTab;
        _active = Tabs.Any(t => t.Key == want) ? want : Tabs.FirstOrDefault()?.Key ?? "Utility";
        _filter = main.Memory.ArmoryFilter;
        Rebuild();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<ArmoryCardViewModel> Visible { get; } = new();
    public ObservableCollection<AddItemCardViewModel> VisibleAdd { get; } = new();
    public bool HasGameData { get; }
    public bool ShowAdd => _active == AddKey;
    public bool ShowOwned => !ShowAdd;
    public bool NoGameData => ShowAdd && !HasGameData;
    string _active;

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

    void Rebuild()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _active;
        var q = (Filter ?? "").Trim().ToLowerInvariant();
        Visible.Clear();
        VisibleAdd.Clear();
        if (ShowAdd)
        {
            foreach (var c in _addable.Where(c => q.Length == 0 || c.Search.Contains(q))) VisibleAdd.Add(c);
            EmptyText = HasGameData ? "Nothing matches."
                : "The game's item list comes from the game data: open the Game data tab and extract it once, then the items you can add appear here.";
        }
        else
        {
            foreach (var c in _all.Where(c => c.Kind == _active && (q.Length == 0 || c.Search.Contains(q)))) Visible.Add(c);
            EmptyText = _all.Count == 0 ? "Nothing in the inventory yet." : "Nothing matches.";
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowAdd));
        OnPropertyChanged(nameof(ShowOwned));
        OnPropertyChanged(nameof(NoGameData));
    }
}
