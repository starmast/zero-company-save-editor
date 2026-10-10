using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

/// <summary>Armory: Utility Items and Weapon Mods as cards (name, tier, rarity, description, count).</summary>
public sealed partial class ArmoryViewModel : ScreenViewModel
{
    static readonly (string Key, string Label)[] Kinds =
    {
        ("Utility", "Utility items"), ("Modification", "Weapon mods"), ("Other", "Other"),
    };

    readonly MainViewModel _main;
    readonly List<ArmoryCardViewModel> _all = new();

    public ArmoryViewModel(MainViewModel main, OpenSave open, string? tab)
    {
        _main = main;
        var fields = Own(new FieldSet(main.Session.Pending));
        foreach (var it in open.View.Armory.Items) _all.Add(new ArmoryCardViewModel(it, fields.Make(it.Count, 0, it.Name)!));
        foreach (var (k, l) in Kinds)
        {
            int n = _all.Count(c => c.Kind == k);
            if (n > 0) Tabs.Add(new TabViewModel(k, $"{l} {n}", Select));
        }
        var want = tab ?? main.Memory.ArmoryTab;
        _active = Tabs.Any(t => t.Key == want) ? want : Tabs.FirstOrDefault()?.Key ?? "Utility";
        _filter = main.Memory.ArmoryFilter;
        Rebuild();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<ArmoryCardViewModel> Visible { get; } = new();
    string _active;

    [ObservableProperty] string _filter;
    [ObservableProperty] string _emptyText = "";
    public bool IsEmpty => Visible.Count == 0;

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
        foreach (var c in _all.Where(c => c.Kind == _active && (q.Length == 0 || c.Search.Contains(q)))) Visible.Add(c);
        EmptyText = _all.Count == 0 ? "Nothing in the inventory yet." : "Nothing matches.";
        OnPropertyChanged(nameof(IsEmpty));
    }
}
