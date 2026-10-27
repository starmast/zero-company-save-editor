using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>Weapons: each operator's weapon and its parts, flagging weapons the game can no longer build (a mod weapon that stopped loading).</summary>
public sealed class WeaponsViewModel : ScreenViewModel
{
    public WeaponsViewModel(MainViewModel main, OpenSave open)
    {
        foreach (var w in open.View.Weapons.Rows) Rows.Add(Own(new WeaponRowViewModel(main.Session.Pending, w)));
        Damaged = open.View.Weapons.DamagedCount;
        Summary = Rows.Count == 0 ? "No operator in this save has a weapon the editor can read."
            : Damaged == 0 ? "Every weapon has its mesh and variant data. Copy weapon can still replace one with another operator's vanilla weapon."
            : $"{Damaged} weapon{(Damaged == 1 ? "" : "s")} lost {(Damaged == 1 ? "its" : "their")} data: the Armory shows no weapon and greys out Equip mods, Change weapon and Customize appearance.";
    }

    public ObservableCollection<WeaponRowViewModel> Rows { get; } = new();
    public int Damaged { get; }
    public string Summary { get; }
}
