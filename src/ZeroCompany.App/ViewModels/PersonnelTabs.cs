using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>Content of one Personnel tab for the selected operator.</summary>
public abstract class PersonnelTabViewModel : ViewModelBase, IDisposable
{
    public virtual void Dispose() { }
}

// ------------------------------------------------------------------------------------------------ overview
public sealed class EffectRowViewModel
{
    public EffectRowViewModel(EffectView e, FieldSet fields)
    {
        Title = e.Title; Description = e.Description;
        Stacks = fields.Make(e.Stacks, 0, e.Title + " - stacks");
        foreach (var m in e.Magnitudes) { var f = fields.Make(m, 0, e.Title); if (f != null) Magnitudes.Add(f); }
    }

    public string Title { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public EditableField? Stacks { get; }
    public bool HasStacks => Stacks != null;
    public List<EditableField> Magnitudes { get; } = new();
    public bool HasMagnitudes => Magnitudes.Count > 0;
}

/// <summary>A bond trait this operator could be given.</summary>
public sealed record TraitOption(string Asset, string Name, string Source, string Description)
{
    public override string ToString() => $"{Name} ({Source})";
}

public sealed partial class OverviewTabViewModel : PersonnelTabViewModel
{
    readonly PendingChanges _pending;
    readonly FocusView? _focus;
    readonly FieldSet _fields;
    readonly string _guid;
    readonly List<TraitOption> _allTraits = new();

    public OverviewTabViewModel(MainViewModel main, OperatorView op, FaceViewModel face)
    {
        _pending = main.Session.Pending;
        _fields = new FieldSet(_pending);
        Name = op.Name;
        Face = face;
        RoleLine = op.Dead ? "Fallen · Memorial" : string.IsNullOrEmpty(op.Role) ? "Operator" : op.Role;
        InjuredLine = op.Injuries != 0 && !op.Dead ? $"Injured ({op.Injuries})" : "";
        _focus = op.Focus;
        if (op.Focus != null) Unspent = _fields.Make(op.Focus, 0, "Unspent focus points");
        foreach (var e in op.Effects) Effects.Add(new EffectRowViewModel(e, _fields));
        if (Unspent != null) Unspent.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TotalText));
        _guid = op.Guid;
        if (!op.Dead && main.Session.Current is { } open)
        {
            var db = main.Session.Service.Db;
            _allTraits = TraitOps.Givable(open.Gvas, op.Guid)
                .Select(t =>
                {
                    var (title, desc) = db.EffectText(t.Asset);         // the game's own name and text once the game data is extracted
                    return new TraitOption(t.Asset, title is { Length: > 0 } ? title : t.Name, $"{t.Class} {t.Kind.ToLowerInvariant()}", desc);
                })
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        _pending.Changed += RefreshTraits;
        RefreshTraits();
    }

    // ---- give a bond trait -------------------------------------------------------------------
    public ObservableCollection<TraitOption> TraitOptions { get; } = new();
    public ObservableCollection<QueuedGrantViewModel> QueuedTraits { get; } = new();
    [ObservableProperty] TraitOption? _chosenTrait;
    public bool CanGiveTrait => _allTraits.Count > 0;
    public bool HasQueuedTraits => QueuedTraits.Count > 0;
    public bool HasChosenTrait => ChosenTrait != null;
    /// <summary>What the chosen trait does, from the game data (empty until it is extracted).</summary>
    public string ChosenTraitDescription => ChosenTrait?.Description ?? "";
    public bool HasChosenTraitDescription => ChosenTraitDescription.Length > 0;
    partial void OnChosenTraitChanged(TraitOption? value)
    {
        OnPropertyChanged(nameof(HasChosenTrait)); OnPropertyChanged(nameof(ChosenTraitDescription)); OnPropertyChanged(nameof(HasChosenTraitDescription));
    }

    [RelayCommand]
    void AddTrait()
    {
        if (ChosenTrait is { } t) _pending.SetTrait(_guid, t.Asset, true);
    }

    void RefreshTraits()
    {
        var queued = _pending.Traits.Where(x => x.Guid == _guid).Select(x => x.Trait).ToHashSet();
        var keep = ChosenTrait;
        TraitOptions.Clear();
        foreach (var t in _allTraits.Where(t => !queued.Contains(t.Asset))) TraitOptions.Add(t);
        ChosenTrait = keep != null && TraitOptions.Contains(keep) ? keep : null;
        QueuedTraits.Clear();
        foreach (var t in _allTraits.Where(t => queued.Contains(t.Asset)))
            QueuedTraits.Add(new QueuedGrantViewModel($"{t.Name} ({t.Source})", () => _pending.SetTrait(_guid, t.Asset, false)));
        OnPropertyChanged(nameof(HasQueuedTraits));
    }

    public string Name { get; }
    public FaceViewModel Face { get; }
    public string RoleLine { get; }
    public string InjuredLine { get; }
    public bool IsInjured => InjuredLine.Length > 0;
    public EditableField? Unspent { get; }
    public bool HasFocus => Unspent != null;
    /// <summary>Total focus as it will be written: the pending total, which an unspent-focus edit already moves with it.</summary>
    public string TotalText => _focus == null || Unspent == null ? "" :
        (_focus.TotalRef != null ? _pending.GetValue(_focus.TotalRef) : (_focus.Total ?? 0) + (Unspent.Value - Unspent.Original))
            .ToString("N0", CultureInfo.InvariantCulture);
    public List<EffectRowViewModel> Effects { get; } = new();
    public bool NoEffects => Effects.Count == 0;

    public override void Dispose() { _pending.Changed -= RefreshTraits; _fields.Dispose(); }
}

// ------------------------------------------------------------------------------------------------ bonds
public sealed partial class BondFaceViewModel : ViewModelBase
{
    public BondFaceViewModel(BondView b, FaceViewModel face, Action<string> select)
    {
        Partner = b.Partner; Name = b.PartnerName; Face = face;
        SelectCommand = new RelayCommand(() => select(b.Partner));
    }

    public string Partner { get; }
    public string Name { get; }
    public FaceViewModel Face { get; }
    public System.Windows.Input.ICommand SelectCommand { get; }
    [ObservableProperty] bool _isSelected;
    [ObservableProperty] bool _isChanged;
}

public sealed class BondColumnViewModel
{
    public string Label { get; init; } = "";
    public bool IsNeutral { get; init; }
    public List<BondFaceViewModel> Faces { get; init; } = new();
}

public sealed partial class BondsTabViewModel : PersonnelTabViewModel
{
    readonly MainViewModel _main;
    readonly OperatorView _op;
    readonly PendingChanges _pending;
    readonly FieldSet _fields;
    readonly Dictionary<string, FaceViewModel> _faces;
    readonly Dictionary<string, EditableField> _levelFields = new();

    public BondsTabViewModel(MainViewModel main, OperatorView op, Dictionary<string, FaceViewModel> faces)
    {
        _main = main; _op = op; _faces = faces;
        _pending = main.Session.Pending;
        _fields = new FieldSet(_pending);
        foreach (var b in op.Bonds) if (b.Level != null) _levelFields[b.Partner] = _fields.Make(b.Level, ViewBuilderOffset, "Bond level")!;
        var want = main.Memory.SelectedBond;
        _selectedPartner = op.Bonds.Any(b => b.Partner == want) ? want : op.Bonds.FirstOrDefault()?.Partner;     // strongest first
        _pending.Changed += Rebuild;
        Rebuild();
    }

    const int ViewBuilderOffset = ViewBuilder.BondOffset;

    string? _selectedPartner;
    public ObservableCollection<BondColumnViewModel> Columns { get; } = new();
    public string[] Words { get; } = { "Very low", "Neutral", "Very high" };
    [ObservableProperty] string _detailTitle = "Select a partner";
    [ObservableProperty] EditableField? _level;
    [ObservableProperty] EditableField? _progress;
    [ObservableProperty] EditableField? _cross;
    [ObservableProperty] EditableField? _highest;
    public bool HasSelection => Level != null;
    public bool NoSelection => Level == null;
    public bool HasHighest => Highest != null;

    void Select(string partner)
    {
        _selectedPartner = partner;
        _main.Memory.SelectedBond = partner;
        Rebuild();
    }

    void Rebuild()
    {
        // faces sit in the column of their (pending) bond level on the 0-8 game scale
        Columns.Clear();
        for (int lvl = 0; lvl <= 8; lvl++)
        {
            var col = new BondColumnViewModel { Label = lvl.ToString(CultureInfo.InvariantCulture), IsNeutral = lvl == 4 };
            foreach (var b in _op.Bonds.Where(b => b.Level != null && (int)Math.Round(_levelFields[b.Partner].Value) == lvl))
                col.Faces.Add(new BondFaceViewModel(b, _faces.GetValueOrDefault(b.Partner) ?? new FaceViewModel(b.PartnerName, null), Select)
                {
                    IsSelected = b.Partner == _selectedPartner, IsChanged = _pending.IsChanged(b.Level!),
                });
            Columns.Add(col);
        }
        var sel = _op.Bonds.FirstOrDefault(b => b.Partner == _selectedPartner);
        DetailTitle = sel != null ? "Bond with " + sel.PartnerName : "Select a partner";
        if (sel == null) { Level = Progress = Cross = Highest = null; }
        else if (Level == null || Level.Ref.Id != sel.Level?.Id)
        {
            Level = _levelFields[sel.Partner];
            Progress = _fields.Make(sel.Progress, 0, "Progress to next level");
            Cross = _fields.Make(sel.Cross, 0, "Cross training available");
            Highest = _fields.Make(sel.Highest, ViewBuilderOffset, "Highest level reached");
        }
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(NoSelection)); OnPropertyChanged(nameof(HasHighest));
    }

    public override void Dispose()
    {
        _pending.Changed -= Rebuild;
        _fields.Dispose();
    }
}

// ------------------------------------------------------------------------------------------------ focus tree
public sealed partial class PipViewModel : ViewModelBase
{
    public PipViewModel(int level, string tooltip, Action<int> set)
    {
        Level = level; Tooltip = tooltip;
        Command = new RelayCommand(() => set(level));
    }

    public int Level { get; }
    public string Tooltip { get; }
    public System.Windows.Input.ICommand Command { get; }
    [ObservableProperty] bool _isOn;
    [ObservableProperty] bool _isChanged;
}

public sealed partial class AbilityRowViewModel : ViewModelBase
{
    readonly FocusTabViewModel _owner;
    readonly AbilityView _a;
    readonly PendingChanges _pending;

    public AbilityRowViewModel(FocusTabViewModel owner, OperatorView op, AbilityView a, PendingChanges pending, FieldSet fields)
    {
        _owner = owner; _a = a; _pending = pending;
        Name = a.Name;
        bool tree = a.Thresholds != null && a.Level != null && op.Focus?.TotalRef != null;
        if (tree)
        {
            Mode = RowMode.Levelled;
            for (int i = 0; i < a.Thresholds!.Length; i++)
            {
                int lvl = i + 1; int cost = a.Thresholds[i];
                Pips.Add(new PipViewModel(lvl, $"Level {lvl}" + (cost > 0 ? $" · {cost} focus in total" : " · free"), l => _owner.SetAbilityLevel(this, l)));
            }
        }
        else if (a.Level != null)
        {
            Mode = RowMode.Independent;                                  // no cost table known: edit independently
            for (int i = 1; i <= 6; i++)
            {
                int lvl = i;
                Pips.Add(new PipViewModel(lvl, $"Level {lvl}", l => _pending.SetEdit(a.Level!, l)));
            }
            Spent = fields.Make(a.Spent, 0, a.Name + " - focus spent");
        }
        else Mode = RowMode.Fixed;
        Refresh();
    }

    public enum RowMode { Levelled, Independent, Fixed }

    public string Name { get; }
    public RowMode Mode { get; }
    public bool IsLevelled => Mode == RowMode.Levelled;
    public bool IsIndependent => Mode == RowMode.Independent;
    public bool IsFixed => Mode == RowMode.Fixed;
    public List<PipViewModel> Pips { get; } = new();
    public EditableField? Spent { get; }
    public AbilityView Ability => _a;
    public string FixedText => $"Level {_a.CurrentLevel} (fixed)";
    [ObservableProperty] string _info1 = "";
    [ObservableProperty] string _info2 = "";

    public double SpentValue => _a.Spent != null ? _pending.GetValue(_a.Spent) : 0;

    public void Refresh()
    {
        if (_a.Level == null) return;
        double lvl = _pending.GetValue(_a.Level);
        bool changed = _pending.IsChanged(_a.Level);
        foreach (var p in Pips) { p.IsOn = p.Level <= lvl; p.IsChanged = changed; }
        if (Mode == RowMode.Levelled && _a.Thresholds != null)
        {
            var t = _a.Thresholds;
            int l = (int)lvl;
            Info1 = $"Level {l}/{t.Length} · {t[Math.Clamp(l - 1, 0, t.Length - 1)]} spent";
            Info2 = l < t.Length ? $"Next level costs {t[l] - t[l - 1]}" : "Max level";
        }
    }
}

public sealed class AbilityGroupViewModel
{
    public string Name { get; init; } = "";
    public List<AbilityRowViewModel> Rows { get; init; } = new();
}

/// <summary>An ability another operator in the save holds, which this operator could be given.</summary>
public sealed record GrantOption(string Tag, string Name, string From)
{
    public override string ToString() => $"{Name} (as {From} has it)";
}

public sealed partial class QueuedGrantViewModel : ViewModelBase
{
    public QueuedGrantViewModel(string text, Action remove) { Text = text; RemoveCommand = new RelayCommand(remove); }
    public string Text { get; }
    public System.Windows.Input.ICommand RemoveCommand { get; }
}

public sealed partial class FocusTabViewModel : PersonnelTabViewModel
{
    static readonly Dictionary<string, string> KindLabels = new()
    {
        ["Class"] = "Class abilities", ["Passive"] = "Passives", ["Identity"] = "Signature", ["Defense"] = "Defense",
    };

    readonly MainViewModel _main;
    readonly OperatorView _op;
    readonly PendingChanges _pending;
    readonly FieldSet _fields;

    public FocusTabViewModel(MainViewModel main, OperatorView op)
    {
        _main = main; _op = op;
        _pending = main.Session.Pending;
        _fields = new FieldSet(_pending);
        _freeFocus = main.Memory.FreeFocus;
        var groups = new Dictionary<string, AbilityGroupViewModel>();
        foreach (var a in op.Abilities)
        {
            var k = KindLabels.TryGetValue(a.Kind, out var l) ? l : string.IsNullOrEmpty(a.Kind) ? "Abilities" : a.Kind;
            if (!groups.TryGetValue(k, out var g)) groups[k] = g = new AbilityGroupViewModel { Name = k };
            g.Rows.Add(new AbilityRowViewModel(this, op, a, _pending, _fields));
        }
        Groups = groups.Values.ToList();
        if (!op.Dead)
        {
            var open = main.Session.Current!;
            var db = main.Session.Service.Db;
            var names = open.View.Personnel.Roster.Concat(open.View.Personnel.Memorial).ToDictionary(o => o.Guid, o => o.Name);
            _allOptions = AbilityOps.Grantable(open.Gvas, op.Guid)
                .Select(d => new GrantOption(d.Tag, Naming.Ability(db, d.Tag), names.GetValueOrDefault(d.FromGuid, "another operator")))
                .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        _pending.Changed += Refresh;
        Refresh();
    }

    public List<AbilityGroupViewModel> Groups { get; }
    readonly List<GrantOption> _allOptions = new();
    public ObservableCollection<GrantOption> GrantOptions { get; } = new();
    public ObservableCollection<QueuedGrantViewModel> QueuedGrants { get; } = new();
    [ObservableProperty] GrantOption? _chosenGrant;
    public bool CanGrant => _allOptions.Count > 0;
    public bool HasQueuedGrants => QueuedGrants.Count > 0;
    public bool HasChosenGrant => ChosenGrant != null;
    partial void OnChosenGrantChanged(GrantOption? value) => OnPropertyChanged(nameof(HasChosenGrant));

    [RelayCommand]
    void AddGrant()
    {
        if (ChosenGrant is not { } o) return;
        _pending.SetGrant(_op.Guid, o.Tag, 1);
    }

    void RefreshGrants()
    {
        var queued = _pending.Grants.Where(kv => kv.Key.Guid == _op.Guid).Select(kv => kv.Key.Tag).ToHashSet();
        var keep = ChosenGrant;
        GrantOptions.Clear();
        foreach (var o in _allOptions.Where(o => !queued.Contains(o.Tag))) GrantOptions.Add(o);
        ChosenGrant = keep != null && GrantOptions.Contains(keep) ? keep : null;
        QueuedGrants.Clear();
        foreach (var o in _allOptions.Where(o => queued.Contains(o.Tag)))
            QueuedGrants.Add(new QueuedGrantViewModel($"{o.Name}, copied from {o.From}, at level 1 (raise it after saving)", () => _pending.SetGrant(_op.Guid, o.Tag, 0)));
        OnPropertyChanged(nameof(HasQueuedGrants));
    }
    public bool NoAbilities => Groups.Count == 0;
    [ObservableProperty] string _notice = "";
    [ObservableProperty] string _totals = "";
    bool _freeFocus;

    public bool SpendMode
    {
        get => !_freeFocus;
        set { if (value) SetFree(false); }
    }

    public bool GrantMode
    {
        get => _freeFocus;
        set { if (value) SetFree(true); }
    }

    void SetFree(bool v)
    {
        _freeFocus = v;
        _main.Memory.FreeFocus = v;
        Notice = "";
        OnPropertyChanged(nameof(SpendMode)); OnPropertyChanged(nameof(GrantMode));
    }

    void Refresh()
    {
        RefreshGrants();
        foreach (var g in Groups) foreach (var r in g.Rows) r.Refresh();
        double spent = Groups.SelectMany(g => g.Rows).Sum(r => r.SpentValue);
        string unspent = _op.Focus != null ? _pending.GetValue(_op.Focus).ToString("N0", CultureInfo.InvariantCulture) : "";
        Totals = $"Spent {spent.ToString("N0", CultureInfo.InvariantCulture)}" + (unspent.Length > 0 ? $" · Unspent {unspent}" : "");
    }

    /// <summary>
    /// Set an ability to level <paramref name="lvl"/> using the game's own cumulative cost table, keeping total = focus spent +
    /// unspent (the invariant every saved operator obeys).
    /// </summary>
    public bool SetAbilityLevel(AbilityRowViewModel row, int lvl)
    {
        var a = row.Ability;
        var op = _op;
        var t = a.Thresholds!;
        int newSpent = t[lvl - 1];
        double d = newSpent - _pending.GetValue(a.Spent!);
        double avail = _pending.GetValue(op.Focus!);
        double total = _pending.GetValue(op.Focus!.TotalRef!);
        var inv = CultureInfo.InvariantCulture;
        if (!_freeFocus && d > avail)
        {
            Notice = $"{a.Name} level {lvl} needs {d.ToString(inv)} more focus but only {avail.ToString(inv)} is unspent. Switch to \"Grant the focus\" to level it anyway.";
            return false;
        }
        if (!_freeFocus && avail - d > op.Focus.Max) { Notice = "That would refund more focus than the game allows."; return false; }
        if (_freeFocus && (total + d < op.Focus.TotalRef!.Min || total + d > op.Focus.TotalRef.Max))
        {
            Notice = "Total focus would leave its allowed range.";
            return false;
        }
        Notice = "";
        _pending.SetEdit(a.Level!, lvl);
        _pending.SetEdit(a.Spent!, newSpent);
        if (d != 0)
        {
            if (_freeFocus) _pending.SetEdit(op.Focus.TotalRef!, total + d);
            else _pending.SetEdit(op.Focus, avail - d, link: false);          // total stays: points just move
        }
        return true;
    }

    public override void Dispose()
    {
        _pending.Changed -= Refresh;
        _fields.Dispose();
    }
}
