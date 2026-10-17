using ZeroCompany.Core.Actions;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

public sealed class CombatEffectRowViewModel : IDisposable
{
    readonly ToggleActionViewModel _remove;

    public CombatEffectRowViewModel(PendingChanges p, FieldSet fields, int unit, UnitEffectView e)
    {
        Title = e.Title;
        Detail = e.Duration is null or < 0 ? "until removed" : e.Duration.Value.ToString("0.##", CultureInfo.InvariantCulture) + " left";
        Stacks = fields.Make(e.Stacks, 0, "Stacks");
        _remove = new ToggleActionViewModel(p, () => p.EffectRemovals.Contains((unit, e.Index)), () => p.ToggleEffectRemoval(unit, e.Index),
            "Remove", "Removing - undo", e.Def);
    }

    public string Title { get; }
    public string Detail { get; }
    public EditableField? Stacks { get; }
    public bool HasStacks => Stacks != null;
    public ToggleActionViewModel Remove => _remove;
    public void Dispose() => _remove.Dispose();
}

/// <summary>One switch shared by every unit card: also list the stats the game does not keep (off by default).</summary>
public sealed partial class StatVisibility : ObservableObject
{
    [ObservableProperty] bool _showAll;
}

/// <summary>An effect that already exists on some unit and can be copied onto another.</summary>
public sealed record EffectChoice(string Label, int FromUnit, int FromIndex);

public sealed class QueuedCopyViewModel
{
    public QueuedCopyViewModel(string text, Action undo) { Text = text; Undo = new RelayCommand(undo); }
    public string Text { get; }
    public System.Windows.Input.ICommand Undo { get; }
}

/// <summary>One unit on the map: its main numbers, position, every other stat, effects, and the team dropdown.</summary>
public sealed partial class UnitCardViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly CombatUnitView _unit;
    readonly CombatView _combat;
    readonly ToggleActionViewModel _dead;

    readonly StatVisibility _visibility;
    readonly List<(FieldRowViewModel Row, bool Ignored)> _allStats = new();

    public UnitCardViewModel(PendingChanges pending, FieldSet fields, CombatView combat, CombatUnitView u, IReadOnlyList<EffectChoice> choices,
                             StatVisibility? visibility = null)
    {
        _pending = pending; _unit = u; _combat = combat;
        _visibility = visibility ?? new StatVisibility();
        TeamChoices = combat.ThreeWay ? new[] { "Ally", "Friendly AI", "Enemy", "Enemy2" } : new[] { "Ally", "Friendly AI", "Enemy" };
        Name = u.Name;
        Team = u.Team;
        Health = fields.Make(u.Attr("Health"), 0, "Health");
        MaxHealth = fields.Make(u.Attr("MaxHealth"), 0, "Max health");
        ActionPoints = fields.Make(u.Attr("ActionPoints"), 0, "Action points");
        MovePoints = fields.Make(u.Attr("MovementActionPoints"), 0, "Movement action points");
        Advantage = fields.Make(u.Advantage, 0, "Advantage");
        X = fields.Make(u.X, 0, "X"); Y = fields.Make(u.Y, 0, "Y"); Z = fields.Make(u.Z, 0, "Z");
        HasPosition = X != null && Y != null && Z != null;
        foreach (var (label, field) in new[] { ("Health", Health), ("Max health (base)", MaxHealth), ("Action points", ActionPoints),
                                               ("Movement action points", MovePoints), ("Advantage", Advantage) })
            if (field != null) Rows.Add(new FieldRowViewModel(label, field));
        // Everything the save stores for the unit, grouped (Health, Actions, Offense ...) with plain names and notes.
        // Health, max health and the action points are already in the main rows above.
        var groupRank = CombatStatCatalog.GroupOrder.Select((g, i) => (g, i)).ToDictionary(x => x.g, x => x.i);
        foreach (var a in u.Attributes
                     .Where(a => !(a.Name is "Health" or "MaxHealth" or "ActionPoints" or "MovementActionPoints"
                                   && a.Set is "BitReactorHealthSet" or "BitReactorCombatSet"))
                     .OrderBy(a => groupRank.GetValueOrDefault(a.Info.Group, 99)))
        {
            // A stat with no lasting effect (the game rebuilds it, puts it back, or showed no effect) is hidden until the "show" box is ticked.
            var ignored = a.Info.Keep == StatKeep.No || (a.Info.Keep == StatKeep.NotForHeroes && u.PlayerBehavior);
            var label = $"{a.Info.Group} · {a.Info.Label}" + (ignored ? " (no lasting effect)" : "");
            _allStats.Add((new FieldRowViewModel(label, fields.Make(a.Ref, 0, a.Name)!, a.Info.Note), ignored));
        }
        _visibility.PropertyChanged += OnVisibilityChanged;
        RebuildStats();
        foreach (var e in u.Effects) Effects.Add(new CombatEffectRowViewModel(pending, fields, u.Index, e));
        Choices = choices.Where(c => c.FromUnit != u.Index).ToList();
        _dead = new ToggleActionViewModel(pending, () => pending.DeadChanges.ContainsKey(u.Index),
            () => pending.SetDead(u.Index, !pending.WantsDead(u.Index, u.Dead), u.Dead),
            u.Dead ? "Revive" : "Mark dead", "Queued - undo",
            "Sets the unit's dead flag. The game may still read health, so also set it to 0 if you want it gone.");
        AddEffect = new RelayCommand(() => { if (SelectedChoice is { } c) _pending.AddEffectCopy(_unit.Index, c.FromUnit, c.FromIndex); });
        _pending.Changed += RefreshCopies;
        _pending.Changed += OnPendingChanged;
        RefreshCopies();
    }

    /// <summary>The sub-tab a unit of this team is listed under.</summary>
    public static string TabOf(string team) => team switch { "Player" => "allies", "Enemy" or "Enemy2" => "enemies", "Civilian" => "civilians", _ => "other" };

    public string Name { get; }
    public string Team { get; }
    public string TabKey => TabOf(Team);

    /// <summary>Ally = player team (you control it). Enemy2 is offered only in three-way battles. Friendly AI = the civilian team: friendly units the game controls (rescued VIPs, allied fighters).</summary>
    public IReadOnlyList<string> TeamChoices { get; }
    static string TeamLabel(string team) => team switch { "Player" => "Ally", "Civilian" => "Friendly AI", _ => team };
    static string TeamKey(string label) => label switch { "Ally" => "Player", "Friendly AI" => "Civilian", _ => label };

    /// <summary>The team as shown in the dropdown (saved, or queued). Changing it queues an experimental team change.</summary>
    public string SelectedTeam
    {
        get => TeamLabel(_pending.TeamWanted(_unit.Index, _unit.Team));
        set { if (value != null) _pending.SetTeam(_unit.Index, TeamKey(value), _unit.Team); }
    }

    public bool TeamChanged => _pending.TeamChanges.ContainsKey(_unit.Index);

    /// <summary>A player-class unit moved to Enemy / Friendly AI: in testing it skipped its turn (the AI has no abilities for it).</summary>
    public bool TeamWarning => TeamChanged && _unit.PlayerBehavior && _pending.TeamWanted(_unit.Index, _unit.Team) != "Player";

    void OnPendingChanged() { OnPropertyChanged(nameof(SelectedTeam)); OnPropertyChanged(nameof(TeamChanged)); OnPropertyChanged(nameof(TeamWarning)); }

    public bool IsDeadNow => _unit.Dead;
    public EditableField? Health { get; }
    public EditableField? MaxHealth { get; }
    public EditableField? ActionPoints { get; }
    public EditableField? MovePoints { get; }
    public EditableField? Advantage { get; }
    public EditableField? X { get; }
    public EditableField? Y { get; }
    public EditableField? Z { get; }
    public bool HasPosition { get; }
    public ToggleActionViewModel Dead => _dead;
    /// <summary>Health, action points and the like, as label + field rows (only those the unit has).</summary>
    public List<FieldRowViewModel> Rows { get; } = new();
    public ObservableCollection<FieldRowViewModel> Stats { get; } = new();
    public string StatsHeader
    {
        get
        {
            var hidden = _allStats.Count - Stats.Count;
            return hidden > 0 ? $"All stats ({Stats.Count}, {hidden} hidden)" : $"All stats ({Stats.Count})";
        }
    }

    void OnVisibilityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RebuildStats();

    void RebuildStats()
    {
        Stats.Clear();
        foreach (var (row, ignored) in _allStats)
            if (_visibility.ShowAll || !ignored) Stats.Add(row);
        OnPropertyChanged(nameof(StatsHeader));
    }
    public ObservableCollection<CombatEffectRowViewModel> Effects { get; } = new();
    public string EffectsHeader => $"Effects ({Effects.Count})";
    public IReadOnlyList<EffectChoice> Choices { get; }
    public bool HasChoices => Choices.Count > 0;
    [ObservableProperty] EffectChoice? _selectedChoice;
    public System.Windows.Input.ICommand AddEffect { get; }
    public ObservableCollection<QueuedCopyViewModel> QueuedCopies { get; } = new();
    public bool HasQueuedCopies => QueuedCopies.Count > 0;

    void RefreshCopies()
    {
        if (_pending.EffectCopies.Count == 0 && QueuedCopies.Count == 0) return;
        QueuedCopies.Clear();
        var all = _pending.EffectCopies;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Unit != _unit.Index) continue;
            int idx = i;
            var src = _combat.Units[all[i].FromUnit];
            QueuedCopies.Add(new QueuedCopyViewModel($"Adding {src.Effects[all[i].FromIndex].Title} (copied from {src.Name})", () => _pending.RemoveEffectCopy(idx)));
        }
        OnPropertyChanged(nameof(HasQueuedCopies));
    }

    public void Dispose()
    {
        _pending.Changed -= RefreshCopies;
        _pending.Changed -= OnPendingChanged;
        _visibility.PropertyChanged -= OnVisibilityChanged;
        _dead.Dispose();
        foreach (var r in Effects) r.Dispose();
    }
}
