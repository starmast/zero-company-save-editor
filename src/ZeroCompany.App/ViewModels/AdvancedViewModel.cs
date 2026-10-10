using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

public sealed class AdvancedFieldRowViewModel
{
    public AdvancedFieldRowViewModel(Field f, EditableField field)
    {
        Label = f.Label; Note = f.Note; Now = "now " + f.Value.ToString("N0", CultureInfo.InvariantCulture); Field = field;
    }

    public string Label { get; }
    public string Note { get; }
    public bool HasNote => Note.Length > 0;
    public string Now { get; }
    public EditableField Field { get; }
}

public sealed class AdvancedSectionViewModel
{
    public string Name { get; init; } = "";
    public List<AdvancedFieldRowViewModel> Rows { get; init; } = new();
}

/// <summary>One node of the raw property tree. Children load the first time it is expanded.</summary>
public sealed partial class RawNodeViewModel : ViewModelBase
{
    static readonly RawNodeViewModel Placeholder = new();
    readonly SaveService? _svc;
    readonly FieldSet? _fields;
    bool _loaded;

    RawNodeViewModel() { Name = "…"; Meta = ""; }

    public RawNodeViewModel(SaveService svc, FieldSet fields, TreeRow row)
    {
        _svc = svc; _fields = fields; Row = row;
        Name = row.Name;
        Meta = row.Type + (row.Count != null ? $" [{row.Count}]" : "") + (row.Opaque ? " (raw)" : "");
        if (row.Field != null)
        {
            bool isFloat = row.Kind == "float";
            var r = new FieldRef
            {
                Id = row.Field, Value = Convert.ToDouble(row.Value, CultureInfo.InvariantCulture), Kind = row.Kind ?? "int",
                Min = isFloat ? -3.4e38 : int.MinValue, Max = isFloat ? 3.4e38 : int.MaxValue,
            };
            Field = fields.Make(r, 0, row.Name);
        }
        else if (row.Value != null) ValueText = Convert.ToString(row.Value, CultureInfo.InvariantCulture) ?? "";
        if (row.Expandable) Children.Add(Placeholder);
    }

    public TreeRow? Row { get; }
    public string Name { get; }
    public string Meta { get; }
    public EditableField? Field { get; }
    public string ValueText { get; } = "";
    public bool HasValueText => ValueText.Length > 0;
    public bool HasField => Field != null;
    public bool Expandable => Row?.Expandable == true;
    public ObservableCollection<RawNodeViewModel> Children { get; } = new();
    [ObservableProperty] bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded || _svc == null || Row == null) return;
        _loaded = true;
        Children.Clear();
        foreach (var r in _svc.Tree(Row.Id)) Children.Add(new RawNodeViewModel(_svc, _fields!, r));
    }
}

/// <summary>Advanced: every editable value grouped the way the save stores it, plus the raw property tree.</summary>
public sealed partial class AdvancedViewModel : ScreenViewModel
{
    static readonly string[] Groups = { "Resources", "Operators", "Abilities", "Bonds", "Galaxy", "Inventory", "Progression" };

    readonly MainViewModel _main;
    readonly OpenSave _open;
    readonly FieldSet _fields;
    readonly Dictionary<string, List<(Field F, EditableField E)>> _byGroup = new();
    string _tab;

    public AdvancedViewModel(MainViewModel main, OpenSave open, string? tab)
    {
        _main = main; _open = open;
        _fields = Own(new FieldSet(main.Session.Pending));
        foreach (var f in open.Model.Fields)
        {
            var r = new FieldRef { Id = f.Id, Value = f.Value, Min = f.Min, Max = f.Max, Kind = f.Kind };
            if (!_byGroup.TryGetValue(f.Group, out var list)) _byGroup[f.Group] = list = new();
            list.Add((f, _fields.Make(r, 0, f.Label)!));
        }
        var names = Groups.Where(_byGroup.ContainsKey).ToList();
        foreach (var g in names) Tabs.Add(new TabViewModel(g, $"{g} {_byGroup[g].Count}", Select));
        Tabs.Add(new TabViewModel("Raw", "Raw", Select));
        var want = tab ?? main.Memory.AdvancedTab;
        _tab = Tabs.Any(t => t.Key == want) ? want : Tabs[0].Key;
        _filter = main.Memory.AdvancedFilter;
        Rebuild();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<AdvancedSectionViewModel> Sections { get; } = new();
    public ObservableCollection<RawNodeViewModel> RawRoots { get; } = new();

    [ObservableProperty] string _filter;
    public bool IsRaw => _tab == "Raw";
    public bool IsFields => !IsRaw;
    public bool NothingMatches => IsFields && Sections.Count == 0;

    partial void OnFilterChanged(string value) { _main.Memory.AdvancedFilter = value; if (IsFields) Rebuild(); }

    void Select(string key)
    {
        _tab = key;
        _main.Memory.AdvancedTab = key;
        Rebuild();
    }

    void Rebuild()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _tab;
        OnPropertyChanged(nameof(IsRaw)); OnPropertyChanged(nameof(IsFields));
        Sections.Clear();
        RawRoots.Clear();
        if (IsRaw)
        {
            foreach (var row in _main.Session.Service.Tree(null)) RawRoots.Add(new RawNodeViewModel(_main.Session.Service, _fields, row));
        }
        else
        {
            var q = (Filter ?? "").Trim().ToLowerInvariant();
            foreach (var grp in _byGroup[_tab].Where(x => q.Length == 0 || (x.F.Label + " " + x.F.Section).ToLowerInvariant().Contains(q))
                                              .GroupBy(x => x.F.Section))
                Sections.Add(new AdvancedSectionViewModel { Name = grp.Key, Rows = grp.Select(x => new AdvancedFieldRowViewModel(x.F, x.E)).ToList() });
        }
        OnPropertyChanged(nameof(NothingMatches));
    }
}
