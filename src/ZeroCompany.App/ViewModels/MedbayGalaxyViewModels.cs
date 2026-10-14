using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

// ------------------------------------------------------------------------------------------------ medbay
public sealed class SlotViewModel
{
    public SlotViewModel(string letter, bool busy, string tip) { Letter = letter; Busy = busy; Tip = tip; }
    public string Letter { get; }
    public bool Busy { get; }
    public string Tip { get; }
}

public sealed class InjuredRowViewModel : IDisposable
{
    readonly ToggleActionViewModel _heal;

    public InjuredRowViewModel(MainViewModel main, InjuredView o)
    {
        Name = o.Name;
        Detail = o.Injuries + (o.Injuries == 1 ? " injury" : " injuries");
        Face = new FaceViewModel(o.Name, o.HasPortrait && main.Session.Current != null ? main.Portraits.Get(main.Session.Current, o.Guid) : null);
        var p = main.Session.Pending;
        _heal = new ToggleActionViewModel(p, () => p.Heals.Contains(o.Guid), () => p.ToggleHeal(o.Guid), "Heal");
        Heal = _heal;
        OpenCommand = new RelayCommand(() => main.Navigate("personnel", o.Guid, "overview"));
    }

    public string Name { get; }
    public string Detail { get; }
    public FaceViewModel Face { get; }
    public ToggleActionViewModel Heal { get; }
    public System.Windows.Input.ICommand OpenCommand { get; }
    public void Dispose() => _heal.Dispose();
}

public sealed class TreatingRowViewModel
{
    public TreatingRowViewModel(TreatingView t)
    {
        Slot = t.Slot;
        Who = t.Operators.Count > 0 ? string.Join(", ", t.Operators) : "-";
        Since = t.Started is >= 0 ? $"since turn {t.Started}" : "";
    }

    public string Slot { get; }
    public string Who { get; }
    public string Since { get; }
}

/// <summary>Medbay: beds, the bacta tank, who is injured, who is in treatment. Heal queues the game's own removal of the injury.</summary>
public sealed class MedbayViewModel : ScreenViewModel
{
    public MedbayViewModel(MainViewModel main, OpenSave open)
    {
        var mb = open.View.Medbay;
        int busyBeds = mb.Treating.Count(t => t.Slot.StartsWith("Bed", StringComparison.Ordinal));
        bool tankBusy = mb.Treating.Any(t => t.Slot == "Bacta tank");
        for (int i = 0; i < mb.Beds; i++) Slots.Add(new SlotViewModel("B", i < busyBeds, "Medical bed"));
        for (int i = 0; i < mb.Tanks; i++) Slots.Add(new SlotViewModel("T", tankBusy, "Bacta tank"));
        SlotNote = $"{mb.Beds} {(mb.Beds == 1 ? "bed" : "beds")} and {mb.Tanks} {(mb.Tanks == 1 ? "bacta tank" : "bacta tanks")}. More beds come from the Medical Bed upgrades.";
        foreach (var o in mb.Injured) Injured.Add(Own(new InjuredRowViewModel(main, o)));
        foreach (var t in mb.Treating) Treating.Add(new TreatingRowViewModel(t));
        BedCost = $"1 bed · {mb.Cost.Bed.ToString("N0", CultureInfo.InvariantCulture)} credits";
        TankCost = $"1 charge · {mb.Cost.Tank.ToString("N0", CultureInfo.InvariantCulture)} credits";
    }

    public ObservableCollection<SlotViewModel> Slots { get; } = new();
    public string SlotNote { get; }
    public ObservableCollection<InjuredRowViewModel> Injured { get; } = new();
    public ObservableCollection<TreatingRowViewModel> Treating { get; } = new();
    public bool NobodyInjured => Injured.Count == 0;
    public bool AnyTreating => Treating.Count > 0;
    public string BedCost { get; }
    public string TankCost { get; }
}

// ------------------------------------------------------------------------------------------------ galaxy
public sealed class RegionCardViewModel
{
    public RegionCardViewModel(RegionView r, FieldSet fields)
    {
        Name = r.Name;
        Influence = fields.Make(r.Influence, 0, "Influence");
        Contacts = fields.Make(r.Contacts, 0, "Contacts");
        Reward = fields.Make(r.Reward, 0, "Reward tier claimed");
    }

    public string Name { get; }
    public EditableField? Influence { get; }
    public EditableField? Contacts { get; }
    public EditableField? Reward { get; }
    public bool HasInfluence => Influence != null;
    public bool HasContacts => Contacts != null;
    public bool HasReward => Reward != null;
}

public sealed class CoilRowViewModel : IDisposable
{
    readonly ToggleActionViewModel _remove, _prevent;

    public CoilRowViewModel(PendingChanges p, CoilRowView u)
    {
        Id = u.Id;
        Heading = u.Unit + " · " + u.Tier;
        Name = u.Name;
        Description = u.Description;
        _remove = new ToggleActionViewModel(p, () => p.CoilChanges.GetValueOrDefault(u.Id) == "Available",
            () => p.SetCoil(u.Id, p.CoilChanges.GetValueOrDefault(u.Id) == "Available" ? null : "Available"),
            "Remove", "Remove (queued) - undo", "Take it away; the crisis can be failed (and the upgrade gained) again");
        _prevent = new ToggleActionViewModel(p, () => p.CoilChanges.GetValueOrDefault(u.Id) == "Prevented",
            () => p.SetCoil(u.Id, p.CoilChanges.GetValueOrDefault(u.Id) == "Prevented" ? null : "Prevented"),
            "Prevent", "Prevent (queued) - undo", "Take it away as if the crisis had been won");
    }

    public string Id { get; }
    public string Heading { get; }
    public string Name { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public ToggleActionViewModel Remove => _remove;
    public ToggleActionViewModel Prevent => _prevent;

    public void Dispose() { _remove.Dispose(); _prevent.Dispose(); }
}

/// <summary>Galaxy (Holotable): one card per map region, plus the Coil's permanent upgrades, each of which can be removed.</summary>
/// <summary>A Holotable operation with how long it has left on offer, and buttons to give it more time.</summary>
public sealed class OperationRowViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _p;
    readonly OperationView _o;

    public OperationRowViewModel(PendingChanges p, OperationView o)
    {
        _p = p; _o = o;
        Longer = new RelayCommand(() => Set(Age - 1), () => CanEdit && Age > 0);
        Shorter = new RelayCommand(() => Set(Age + 1), () => CanEdit && Age < _o.Expiration - 1);
        Reset = new RelayCommand(() => Set(0), () => CanEdit && Age > 0);
        p.Changed += OnChanged;
    }

    public string Title => _o.Title;
    public string Detail => _o.Detail;
    public bool CanEdit => _o.CanEdit;
    public int Age => _p.OperationAgeWanted(_o.Id, _o.Age);

    /// <summary>"Expires in 2 cycles", or that nothing is known about it.</summary>
    public string Expires => _o.Expiration <= 0 ? "Expiry unknown (extract the game data)"
        : $"Expires in {Math.Max(0, _o.Expiration - Age)} of {_o.Expiration} cycle{(_o.Expiration == 1 ? "" : "s")}";

    public bool IsChanged => _p.OperationAgeChanges.ContainsKey(_o.Id);
    public RelayCommand Longer { get; }
    public RelayCommand Shorter { get; }
    public RelayCommand Reset { get; }

    void Set(int age) => _p.SetOperationAge(_o.Id, Math.Clamp(age, 0, Math.Max(0, _o.Expiration - 1)), _o.Age);

    void OnChanged()
    {
        OnPropertyChanged(nameof(Age)); OnPropertyChanged(nameof(Expires)); OnPropertyChanged(nameof(IsChanged));
        Longer.NotifyCanExecuteChanged(); Shorter.NotifyCanExecuteChanged(); Reset.NotifyCanExecuteChanged();
    }
    public void Dispose() => _p.Changed -= OnChanged;
}

/// <summary>A mission offered in the Den, with a drop-down for the enemy faction and threat tier it will spawn.</summary>
public sealed class MissionRowViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _p;
    readonly MissionView _m;

    public MissionRowViewModel(PendingChanges p, MissionView m)
    {
        _p = p; _m = m;
        Choices = m.Choices.Select(c => new GroupChoice(c.Value, c.Label)).ToList();
        CrisisChoices = m.CrisisChoices.Select(c => new GroupChoice(c.Value, c.Label)).ToList();
        Longer = new RelayCommand(() => SetAdded(Added + 1), () => CanEditTime && Added < _m.TurnNow);
        Shorter = new RelayCommand(() => SetAdded(Added - 1), () => CanEditTime && Left > 1);
        FullTime = new RelayCommand(() => SetAdded(_m.TurnNow), () => CanEditTime && Added < _m.TurnNow);
        p.Changed += OnChanged;
    }

    public string Title => _m.Title;
    public string Detail => string.Join(" · ", new[]
    {
        _m.Kind, _m.Place, _m.Difficulty, _m.UsualFaction.Length > 0 ? "normally vs " + _m.UsualFaction : "", _m.Status,
        _m.TurnAdded >= 0 ? $"offered cycle {_m.TurnAdded}" : "", _m.Lifespan > 0 ? $"lasts {_m.Lifespan} cycle{(_m.Lifespan == 1 ? "" : "s")}" : "",
    }.Where(s => s.Length > 0));

    // time on offer: the mission runs out when the cycle it was added on plus its lifespan passes the current cycle
    public bool CanEditTime => _m.CanEdit && _m.Lifespan > 0 && _m.TurnNow >= 0 && _m.TurnAdded >= 0;
    int Added => _p.MissionTurnWanted(_m.Id, (int)_m.TurnAdded);
    int Left => _m.Lifespan - (_m.TurnNow - Added);
    public string Expires => _m.Lifespan <= 0 || _m.TurnNow < 0 ? "" : $"Expires in {Math.Max(0, Left)} of {_m.Lifespan} cycle{(_m.Lifespan == 1 ? "" : "s")}";
    public bool HasExpires => Expires.Length > 0;
    public RelayCommand Longer { get; private set; } = null!;
    public RelayCommand Shorter { get; private set; } = null!;
    public RelayCommand FullTime { get; private set; } = null!;
    void SetAdded(int turn) => _p.SetMissionTurn(_m.Id, Math.Clamp(turn, _m.TurnNow - _m.Lifespan + 1, _m.TurnNow), (int)_m.TurnAdded);

    // crisis missions: which Coil upgrade the crisis is about
    public bool IsCrisis => _m.CanEdit && _m.CrisisEffect.Length > 0;
    public IReadOnlyList<GroupChoice> CrisisChoices { get; private set; } = Array.Empty<GroupChoice>();
    public GroupChoice? SelectedCrisis
    {
        get => CrisisChoices.FirstOrDefault(c => c.Value == _p.MissionCrisisWanted(_m.Id, _m.CrisisEffect));
        set { if (value != null) _p.SetMissionCrisis(_m.Id, value.Value, _m.CrisisEffect); }
    }
    public bool CanEdit => _m.CanEdit;
    public bool CannotEdit => !_m.CanEdit;
    public string CurrentLabel => _m.Current.Length == 0 ? "No enemy set saved" : ZeroCompany.Core.Actions.CombatOps.SpawnSetLabel(_m.Current);
    public IReadOnlyList<GroupChoice> Choices { get; }

    public GroupChoice? Selected
    {
        get => Choices.FirstOrDefault(c => c.Value == _p.MissionSetWanted(_m.Id, _m.Current));
        set { if (value != null) _p.SetMissionSet(_m.Id, value.Value, _m.Current); }
    }

    public bool IsChanged => _p.MissionSetChanges.ContainsKey(_m.Id) || _p.MissionTurnChanges.ContainsKey(_m.Id) || _p.MissionCrisisChanges.ContainsKey(_m.Id);
    void OnChanged()
    {
        OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedCrisis)); OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(Expires)); OnPropertyChanged(nameof(HasExpires));
        Longer.NotifyCanExecuteChanged(); Shorter.NotifyCanExecuteChanged(); FullTime.NotifyCanExecuteChanged();
    }
    public void Dispose() => _p.Changed -= OnChanged;
}

public sealed class GalaxyViewModel : ScreenViewModel
{
    readonly MainViewModel _main;
    readonly PendingChanges _pending;
    string _active;

    public GalaxyViewModel(MainViewModel main, OpenSave open)
    {
        _main = main;
        _pending = main.Session.Pending;
        var fields = Own(new FieldSet(_pending));
        foreach (var r in open.View.Galaxy.Regions) Regions.Add(new RegionCardViewModel(r, fields));
        foreach (var o in open.View.Galaxy.Operations) Operations.Add(Own(new OperationRowViewModel(_pending, o)));
        foreach (var m in open.View.Galaxy.Missions) Missions.Add(Own(new MissionRowViewModel(_pending, m)));
        foreach (var u in open.View.Coil.Active) Coil.Add(Own(new CoilRowViewModel(_pending, u)));
        CoilHeading = "Active Coil upgrades: " + Coil.Count;
        foreach (var (key, label) in new[] { ("influence", "Influence"), ("operations", $"Operations {Operations.Count}"), ("missions", $"Missions {Missions.Count}"), ("coil", $"Coil upgrades {Coil.Count}") })
            Tabs.Add(new TabViewModel(key, label, Select));
        _active = Tabs.Any(t => t.Key == main.Memory.GalaxyTab) ? main.Memory.GalaxyTab : "influence";
        Refresh();
        RemoveAll = Own(new ToggleActionViewModel(_pending, () => AllOn("Available"), () => Bulk("Available"), "Remove all", "Undo all"));
        PreventAll = Own(new ToggleActionViewModel(_pending, () => AllOn("Prevented"), () => Bulk("Prevented"), "Prevent all", "Undo all"));
    }

    public ObservableCollection<RegionCardViewModel> Regions { get; } = new();
    public ObservableCollection<CoilRowViewModel> Coil { get; } = new();
    public ObservableCollection<OperationRowViewModel> Operations { get; } = new();
    public bool NoOperations => Operations.Count == 0;
    public ObservableCollection<MissionRowViewModel> Missions { get; } = new();
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public bool ShowInfluence => _active == "influence";
    public bool ShowOperations => _active == "operations";
    public bool ShowMissions => _active == "missions";
    public bool ShowCoil => _active == "coil";
    public string ActiveTab => _active;

    void Select(string key) { _active = key; _main.Memory.GalaxyTab = key; Refresh(); }

    void Refresh()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _active;
        foreach (var n in new[] { nameof(ShowInfluence), nameof(ShowOperations), nameof(ShowMissions), nameof(ShowCoil), nameof(ActiveTab) }) OnPropertyChanged(n);
    }
    public bool HasMissions => Missions.Count > 0;
    public bool NoMissions => Missions.Count == 0;
    public string CoilHeading { get; }
    public bool HasRegions => Regions.Count > 0;
    public bool NoRegions => Regions.Count == 0;
    public bool HasCoil => Coil.Count > 0;
    public bool NoCoil => Coil.Count == 0;
    public ToggleActionViewModel RemoveAll { get; }
    public ToggleActionViewModel PreventAll { get; }

    bool AllOn(string to) => Coil.Count > 0 && Coil.All(u => _pending.CoilChanges.GetValueOrDefault(u.Id) == to);

    void Bulk(string to)
    {
        bool all = AllOn(to);
        foreach (var u in Coil) _pending.SetCoil(u.Id, all ? null : to);
    }
}
