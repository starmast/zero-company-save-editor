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
public sealed class GalaxyViewModel : ScreenViewModel
{
    readonly PendingChanges _pending;

    public GalaxyViewModel(MainViewModel main, OpenSave open)
    {
        _pending = main.Session.Pending;
        var fields = Own(new FieldSet(_pending));
        foreach (var r in open.View.Galaxy.Regions) Regions.Add(new RegionCardViewModel(r, fields));
        foreach (var u in open.View.Coil.Active) Coil.Add(Own(new CoilRowViewModel(_pending, u)));
        CoilHeading = "Active Coil upgrades: " + Coil.Count;
        RemoveAll = Own(new ToggleActionViewModel(_pending, () => AllOn("Available"), () => Bulk("Available"), "Remove all", "Undo all"));
        PreventAll = Own(new ToggleActionViewModel(_pending, () => AllOn("Prevented"), () => Bulk("Prevented"), "Prevent all", "Undo all"));
    }

    public ObservableCollection<RegionCardViewModel> Regions { get; } = new();
    public ObservableCollection<CoilRowViewModel> Coil { get; } = new();
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
