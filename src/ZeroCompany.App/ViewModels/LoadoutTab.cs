using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>One weapon part line, "Barrel  CPD_WP_Barrel_01_Default", marked when the part lost its mesh and variant data.</summary>
public sealed class WeaponPartRowViewModel
{
    public WeaponPartRowViewModel(WeaponPartView p) { Slot = p.Slot; Name = p.Name; HasData = p.HasData; }
    public string Slot { get; }
    public string Name { get; }
    public bool HasData { get; }
    public bool NoData => !HasData;
}

/// <summary>An operator's weapon; for a damaged one, queues copying a vanilla weapon from another operator.</summary>
public sealed class WeaponRowViewModel : ViewModelBase, IDisposable
{
    const string Automatic = "";
    readonly PendingChanges _pending;
    readonly string _guid;
    GroupChoice _donor;

    public WeaponRowViewModel(PendingChanges pending, WeaponRowView w)
    {
        _pending = pending; _guid = w.Guid;
        Name = w.Name;
        Meta = string.Join(" · ", new[] { w.Class, w.Type }.Where(s => s.Length > 0));
        Damaged = w.Damaged;
        Parts = w.Parts.Select(p => new WeaponPartRowViewModel(p)).ToList();
        Donors = new[] { new GroupChoice(Automatic, "Automatic") }.Concat(w.Donors.Select(d => new GroupChoice(d.Guid, d.Name))).ToList();
        _donor = Donors[0];
        CanCopy = w.Donors.Count > 0;
        // a working weapon is only replaced after a second click
        Toggle = new RelayCommand(() =>
        {
            if (!IsQueued && Healthy) { Confirming = true; return; }
            Queue(!IsQueued);
        });
        Confirm = new RelayCommand(() => Queue(true));
        Cancel = new RelayCommand(() => Confirming = false);
        _pending.Changed += OnChanged;
    }

    public string Name { get; }
    public string Meta { get; }
    public bool Damaged { get; }
    public bool Healthy => !Damaged;
    public bool CanCopy { get; }
    public bool CannotCopy => Damaged && !CanCopy;
    public string CopyHeading => Damaged ? "COPY THE VANILLA WEAPON OF" : "REPLACE THIS WEAPON WITH THE VANILLA WEAPON OF";
    bool _confirming;
    /// <summary>Asking whether to replace a weapon that works.</summary>
    public bool Confirming
    {
        get => _confirming;
        private set { if (SetProperty(ref _confirming, value)) OnPropertyChanged(nameof(ShowToggle)); }
    }
    public bool ShowToggle => !Confirming;
    public IReadOnlyList<WeaponPartRowViewModel> Parts { get; }
    public IReadOnlyList<GroupChoice> Donors { get; }
    public GroupChoice SelDonor
    {
        get => _donor;
        set
        {
            if (value == null || SetProperty(ref _donor, value) == false) return;
            if (IsQueued) _pending.SetWeaponCopy(_guid, true, value.Value.Length == 0 ? null : value.Value);
        }
    }
    public bool IsQueued => _pending.WeaponCopies.ContainsKey(_guid);
    public string ToggleText => IsQueued ? "Undo copy" : "Copy weapon";
    public System.Windows.Input.ICommand Toggle { get; }

    public System.Windows.Input.ICommand Confirm { get; }
    public System.Windows.Input.ICommand Cancel { get; }

    void Queue(bool on)
    {
        Confirming = false;
        _pending.SetWeaponCopy(_guid, on, _donor.Value.Length == 0 ? null : _donor.Value);
    }

    void OnChanged() { OnPropertyChanged(nameof(IsQueued)); OnPropertyChanged(nameof(ToggleText)); }
    public void Dispose() => _pending.Changed -= OnChanged;
}

/// <summary>One choice in a gear slot picker: an owned item (or "Empty"), with who holds it now.</summary>
public sealed record GearChoice(string Guid, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One gear slot of the selected operator: the picker queues putting an owned item in it, or emptying it.</summary>
public sealed class OperatorGearSlotViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly string _guid, _type;
    readonly int _index;
    readonly string? _saved;
    readonly List<GearItemView> _items;
    bool _syncing;
    GearChoice _selected;

    public OperatorGearSlotViewModel(PendingChanges pending, string guid, GearSlotView slot, IReadOnlyList<GearItemView> items, string operatorName)
    {
        _pending = pending; _guid = guid; _type = slot.Type; _index = slot.Index;
        _saved = slot.Item.Length == 0 ? null : slot.Item;
        _items = items.Where(i => i.Type == slot.Type).ToList();
        Label = $"{(slot.Type == "mod" ? "Weapon mod" : "Utility")} slot {slot.Index + 1}";
        Choices = new[] { new GearChoice("", "Empty") }
            .Concat(_items.Select(i => new GearChoice(i.Guid, i.Name + (i.HeldBy.Length > 0 && i.HeldBy != guid ? $"  (on {i.HeldByName})" : ""))))
            .ToList();
        _selected = Current();
        _pending.Changed += OnChanged;
    }

    public string Label { get; }
    public IReadOnlyList<GearChoice> Choices { get; }
    public bool IsQueued => _pending.Equips.ContainsKey((_guid, _type, _index));
    public string QueuedText => IsQueued ? "queued" : "";

    GearChoice Current()
    {
        var item = _pending.Equips.TryGetValue((_guid, _type, _index), out var q) ? q : _saved;
        return Choices.FirstOrDefault(c => item == null ? c.Guid.Length == 0 : c.Guid == item) ?? Choices[0];
    }

    public GearChoice Selected
    {
        get => _selected;
        set
        {
            if (value == null || _syncing || !SetProperty(ref _selected, value)) return;
            _pending.SetEquip(_guid, _type, _index, value.Guid.Length == 0 ? null : value.Guid, _saved);
        }
    }

    void OnChanged()
    {
        _syncing = true;
        try { Selected = Current(); }
        finally { _syncing = false; }
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsQueued)); OnPropertyChanged(nameof(QueuedText));
    }

    public void Dispose() => _pending.Changed -= OnChanged;
}

/// <summary>Personnel > Loadout: the selected operator's weapon (with Copy weapon) and gear slots.</summary>
public sealed class LoadoutTabViewModel : PersonnelTabViewModel
{
    public LoadoutTabViewModel(MainViewModel main, OpenSave open, OperatorView op)
    {
        var pending = main.Session.Pending;
        Name = op.Name;
        if (open.View.Weapons.Rows.FirstOrDefault(r => r.Guid == op.Guid) is { } w)
        {
            Weapon = new WeaponRowViewModel(pending, w);
        }
        if (open.View.Loadout.Slots.TryGetValue(op.Guid, out var slots))
            foreach (var s in slots) Slots.Add(new OperatorGearSlotViewModel(pending, op.Guid, s, open.View.Loadout.Items, op.Name));
        HasUtilityItems = open.View.Loadout.Items.Any(i => i.Type == "utility");
    }

    public string Name { get; }
    public WeaponRowViewModel? Weapon { get; }
    public bool HasWeapon => Weapon != null;
    public bool NoWeapon => Weapon == null;
    public ObservableCollection<OperatorGearSlotViewModel> Slots { get; } = new();
    public bool HasSlots => Slots.Count > 0;
    public bool NoSlots => Slots.Count == 0;
    /// <summary>False when the squad owns no utility item to put in a slot (Armory > Add items can add one).</summary>
    public bool HasUtilityItems { get; }
    public bool NoUtilityItems => !HasUtilityItems && HasSlots;

    public override void Dispose()
    {
        Weapon?.Dispose();
        foreach (var s in Slots) s.Dispose();
    }
}
