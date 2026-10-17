using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.ViewModels;

public sealed class FieldRowViewModel
{
    public FieldRowViewModel(string label, EditableField field, string note = "")
    {
        Label = label; Field = field; Note = note;
    }

    public string Label { get; }
    public string Note { get; }
    public bool HasNote => Note.Length > 0;
    /// <summary>The note as a tooltip, or null so an empty one is never shown.</summary>
    public string? Tip => HasNote ? Note : null;
    public EditableField Field { get; }
}

public sealed class BackupRowViewModel
{
    readonly CommandViewModel _owner;

    public BackupRowViewModel(CommandViewModel owner, BackupInfo b)
    {
        _owner = owner;
        File = b.File; IsOriginal = b.Original;
        Detail = $"{b.MTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(b.Size >= 1 << 20 ? $"{b.Size / (1024.0 * 1024.0):0.0} MB" : $"{Math.Max(1, b.Size / 1024)} KB")}";
        RestoreCommand = new AsyncRelayCommand(() => _owner.RestoreAsync(File));
    }

    public string File { get; }
    public bool IsOriginal { get; }
    public string Detail { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
}

/// <summary>One kind of gear slot (weapon mod or utility) on the Command screen: the number every operator should have.</summary>
public sealed class GearSlotRowViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly string _type;
    int _value;

    public GearSlotRowViewModel(PendingChanges pending, string type, string label, string note, int current, int most)
    {
        _pending = pending; _type = type;
        Label = label; Note = note; Minimum = current; Maximum = most;
        _value = pending.GearSlots.TryGetValue(type, out var queued) ? queued : current;
        _pending.Changed += Sync;
    }

    void Sync()
    {
        int v = _pending.GearSlots.TryGetValue(_type, out var queued) ? queued : Minimum;
        if (v == _value) return;
        _value = v;
        OnPropertyChanged(nameof(Value));
    }

    public void Dispose() => _pending.Changed -= Sync;

    public string Label { get; }
    public string Note { get; }
    public int Minimum { get; }
    public int Maximum { get; }

    public decimal Value
    {
        get => _value;
        set
        {
            int v = (int)Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            _pending.SetGearSlots(_type, v > Minimum ? v : 0);        // back at what the save has: nothing to write
            OnPropertyChanged();
        }
    }
}

/// <summary>One Black Market quality table on the Command screen: how many items it rolls per refresh.</summary>
public sealed class MarketTableRowViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly string _kind;
    readonly int _tier;
    readonly int _saved;
    int _value;

    public MarketTableRowViewModel(PendingChanges pending, string kind, int tier, int saved)
    {
        _pending = pending; _kind = kind; _tier = tier; _saved = saved;
        Label = $"{MarketOps.Quality(tier)} {(kind == "mods" ? "weapon mods" : "utility items")}";
        _value = pending.MarketTables.TryGetValue((kind, tier), out var queued) ? queued : saved;
        _pending.Changed += Sync;
    }

    public string Label { get; }

    void Sync()
    {
        int v = _pending.MarketTables.TryGetValue((_kind, _tier), out var queued) ? queued : _saved;
        if (v == _value) return;
        _value = v;
        OnPropertyChanged(nameof(Value));
    }

    public decimal Value
    {
        get => _value;
        set
        {
            int v = (int)Math.Clamp(value, 0, MarketOps.MaxCount);
            if (v == _value) return;
            _value = v;
            _pending.SetMarketTable(_kind, _tier, v == _saved ? -1 : v);        // back at what the save has: nothing to write
            OnPropertyChanged();
        }
    }

    public void Dispose() => _pending.Changed -= Sync;
}

/// <summary>Command: campaign overview, progression, stockpile, save info and backups (the "hub" screen).</summary>
public sealed class CommandViewModel : ScreenViewModel
{
    readonly MainViewModel _main;

    public CommandViewModel(MainViewModel main, OpenSave open)
    {
        _main = main;
        var fields = Own(new FieldSet(main.Session.Pending));
        var v = open.View;
        var info = v.Command.Save;
        Title = info.Title?.ToString() is { Length: > 0 } t ? t : open.Name;
        Where = $"{open.Name} ({open.DirId})";
        var kind = info.Type?.ToString() == "AutoSave" ? "Autosave" : "Manual save";
        Line1 = string.Join(" · ", new[] { info.World?.ToString(), info.Mode?.ToString(), kind }.Where(s => !string.IsNullOrEmpty(s)));
        bool perma = info.Permadeath?.ToString() == "true";
        Line2 = $"Difficulty level {info.Difficulty?.ToString() ?? "?"}{(perma ? " · Permadeath on" : "")}";
        Line3 = $"Created {info.Created?.ToString() ?? "?"}";
        Summary = $"{v.Personnel.Roster.Count} operators on the roster · {v.Personnel.Memorial.Count} in the Memorial · "
                  + $"{open.Upgrades.Items.Count(u => u.Status == "Completed")} base upgrades completed.";

        var p = v.Command.Progression;
        void Add(string key, string label, int offset, string note)
        {
            if (p.TryGetValue(key, out var r) && r != null) Campaign.Add(new FieldRowViewModel(label, fields.Make(r, offset, label)!, note));
        }
        Add("turn", "Strategy turn", 0, "The save's info file keeps its own copy, which is not updated.");
        Add("roster_level", "Roster level (LV)", 1, "Shown as LV in the game; Den Level is the same scale.");
        Add("roster_cap", "Roster capacity", 0, "How many operators the roster holds; roster upgrades raise it. Going past what the Den allows works, but the roster screens can look odd.");
        Add("roster_xp", "Roster XP", 0, "");
        Add("base_focus", "Base total focus points", 0, "Raised by the Crew Focus upgrades.");
        var gvas = open.Gvas;
        foreach (var (type, label, most) in new[] { ("utility", "Utility slots per operator", GearOps.MaxUtilitySlots), ("mod", "Weapon mod slots per operator", GearOps.MaxModSlots) })
        {
            int now = GearOps.Current(gvas, GearOps.TypeTag(type));
            if (now > 0)
                GearSlots.Add(Own(new GearSlotRowViewModel(main.Session.Pending, type, label,
                    type == "mod" ? "A weapon takes one mod of each kind, so four is the most that can be used."
                                  : "Every operator gets the new slots, empty and ready to equip.", now, most)));
        }
        foreach (var (key, _, label, _, note) in SaveModel.DenFacts) Add(key, label, 0, note);
        if (MarketOps.Counts(gvas) is { } market)
            foreach (var marketKind in MarketOps.Kinds)
                for (int tier = MarketOps.MinTier; tier <= MarketOps.MaxTier; tier++)
                    MarketTables.Add(Own(new MarketTableRowViewModel(main.Session.Pending, marketKind, tier, market.GetValueOrDefault(MarketOps.Tag(marketKind, tier)))));
        foreach (var r in v.Header.Resources)
            Stockpile.Add(new FieldRowViewModel(r.Label, fields.Make(r, 0, r.Label)!, r.Description));
        foreach (var b in main.Session.Service.ListBackups(open.Name)) Backups.Add(new BackupRowViewModel(this, b));
    }

    public string Title { get; }
    public string Where { get; }
    public string Line1 { get; }
    public string Line2 { get; }
    public string Line3 { get; }
    public string Summary { get; }
    public List<FieldRowViewModel> Campaign { get; } = new();
    public List<FieldRowViewModel> Stockpile { get; } = new();
    public List<GearSlotRowViewModel> GearSlots { get; } = new();
    public bool HasGearSlots => GearSlots.Count > 0;
    public List<MarketTableRowViewModel> MarketTables { get; } = new();
    public bool HasMarketTables => MarketTables.Count > 0;
    public List<BackupRowViewModel> Backups { get; } = new();

    public Task RestoreAsync(string file) => _main.RestoreAsync(file, _main.PendingBar.Force);
}
