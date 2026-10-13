using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

// Rows for the Objectives and Reinforcements sub-tabs of the Combat screen.

public sealed record GroupChoice(string Value, string Label);

/// <summary>A drop-down for the spawn group of a queued wave or a wave definition, bound to the pending store.</summary>
public sealed class GroupPicker : ViewModelBase, IDisposable
{
    readonly PendingChanges _p;
    readonly string _kind;
    readonly int _logic, _index;
    readonly string _saved;

    public GroupPicker(PendingChanges p, string kind, int logic, int index, string saved, IReadOnlyList<string> groups)
    {
        _p = p; _kind = kind; _logic = logic; _index = index; _saved = saved;
        Choices = groups.Select(g => new GroupChoice(g, CombatOps.GroupLabel(g))).ToList();
        p.Changed += OnChanged;
    }

    public IReadOnlyList<GroupChoice> Choices { get; }

    public GroupChoice? Selected
    {
        get => Choices.FirstOrDefault(c => c.Value == _p.GroupWanted(_kind, _logic, _index, _saved));
        set { if (value != null) _p.SetGroup(_kind, _logic, _index, value.Value, _saved); }
    }

    public bool IsChanged => _p.GroupChanges.ContainsKey((_kind, _logic, _index));
    void OnChanged() { OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(IsChanged)); }
    public void Dispose() => _p.Changed -= OnChanged;
}

/// <summary>A wave that is queued to arrive: round, group, and a Cancel shortcut.</summary>
public sealed class WaveRowViewModel : IDisposable
{
    /// <summary>"Cancel" pushes the wave this far away, so it never arrives.</summary>
    public const double CancelRound = 999;
    readonly ToggleActionViewModel _cancel;
    readonly GroupPicker _group;

    public WaveRowViewModel(PendingChanges p, FieldSet fields, ReinforcementsView rv, WaveView w)
    {
        Title = $"Wave {w.Index + 1}";
        Detail = $"Arrives at {CombatOps.SpawnerLabel(w.EntryPoint)}" + (w.ConfigIndex is { } c ? $" · from wave definition {c + 1}" : "");
        Round = fields.Make(w.Round, 0, "Arrives at round");
        _group = new GroupPicker(p, "wave", w.Logic, w.Index, w.Group, rv.Groups);
        _cancel = new ToggleActionViewModel(p, () => Round is { IsChanged: true } r && r.Value == CancelRound,
            () => { if (Round == null) return; if (Round.IsChanged && Round.Value == CancelRound) Round.Revert(); else Round.SetTo(CancelRound); },
            "Cancel wave", "Cancelled - undo", "Pushes the wave to round 999 so it never arrives");
    }

    public string Title { get; }
    public string Detail { get; }
    public EditableField? Round { get; }
    public bool HasRound => Round != null;
    public GroupPicker Group => _group;
    public ToggleActionViewModel Cancel => _cancel;
    public void Dispose() { _cancel.Dispose(); _group.Dispose(); }
}

/// <summary>One of the encounter's wave definitions: group, delay, how many times it may spawn.</summary>
public sealed class WaveConfigRowViewModel : IDisposable
{
    readonly GroupPicker _group;

    public WaveConfigRowViewModel(PendingChanges p, FieldSet fields, ReinforcementsView rv, WaveConfigView c)
    {
        Title = $"Definition {c.Index + 1}";
        Detail = $"{CombatOps.SpawnerLabel(c.EntryPoint)} · " + (c.Automatic ? "starts by itself" : "started by a trigger");
        Delay = fields.Make(c.Delay, 0, "Rounds before it spawns");
        TimesToSpawn = fields.Make(c.TimesToSpawn, 0, "Times it may spawn (-1 = no limit)");
        TimesTriggered = fields.Make(c.TimesTriggered, 0, "Times it has been triggered");
        _group = new GroupPicker(p, "config", c.Logic, c.Index, c.Group, rv.Groups);
    }

    public string Title { get; }
    public string Detail { get; }
    public EditableField? Delay { get; }
    public EditableField? TimesToSpawn { get; }
    public EditableField? TimesTriggered { get; }
    public GroupPicker Group => _group;
    public void Dispose() => _group.Dispose();
}

/// <summary>A reinforcement trigger with an On / Off switch (when its flag is saved).</summary>
public sealed class TriggerRowViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _p;
    readonly TriggerView _t;
    readonly ToggleActionViewModel _toggle;

    public TriggerRowViewModel(PendingChanges p, TriggerView t)
    {
        _p = p; _t = t;
        Title = t.Title;
        _toggle = new ToggleActionViewModel(p, () => p.TriggerChanges.ContainsKey(t.Logic),
            () => p.SetTrigger(t.Logic, !p.TriggerWanted(t.Logic, t.Active), t.Active),
            t.Active ? "Switch off" : "Switch on", "Queued - undo",
            "Switches the trigger that brings this wave in");
        p.Changed += OnChanged;
    }

    public string Title { get; }
    public bool HasFlag => _t.HasFlag;
    public string StatusText => !_t.HasFlag ? "Not triggered yet" : _p.TriggerWanted(_t.Logic, _t.Active) ? "On" : "Off";
    public ToggleActionViewModel Toggle => _toggle;
    void OnChanged() => OnPropertyChanged(nameof(StatusText));
    public void Dispose() { _p.Changed -= OnChanged; _toggle.Dispose(); }
}

/// <summary>A countdown timer with an editable turns-left field.</summary>
public sealed class TimerRowViewModel
{
    public TimerRowViewModel(FieldSet fields, TimerView t)
    {
        Title = t.Title;
        Remaining = fields.Make(t.Remaining, 0, "Turns left");
        Total = t.Total == null ? "" : $"of {t.Total.Value:0}";
        var counts = t.CountsOn == "TeamTurnEnd" ? "Counts down at the end of each of the team's turns" : "Counts down: " + t.CountsOn;
        var fails = t.Fails.Count > 0 ? ". When it reaches zero it fails: " + string.Join(", ", t.Fails) : "";
        Detail = t.Remaining == null ? "Finished: the game no longer saves this timer." : counts + fails + ".";
    }

    public string Title { get; }
    public string Detail { get; }
    public string Total { get; }
    public EditableField? Remaining { get; }
    public bool HasRemaining => Remaining != null;
}

/// <summary>One objective with a button that queues Active / Succeeded, and its editable counters.</summary>
public sealed class ObjectiveRowViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly ObjectiveView _o;
    readonly ToggleActionViewModel _toggle;

    public ObjectiveRowViewModel(PendingChanges pending, FieldSet fields, ObjectiveView o)
    {
        _pending = pending; _o = o;
        foreach (var c in o.Counters) Counters.Add(new FieldRowViewModel(c.Label, fields.Make(c.Ref, 0, c.Label)!));
        Title = o.Title;
        Detail = o.Targets.Count > 0 ? "Interact with: " + string.Join(", ", o.Targets) : o.Name;
        _toggle = new ToggleActionViewModel(pending, () => pending.ObjectiveChanges.ContainsKey(o.Index),
            () => pending.SetObjective(o.Index, pending.ObjectiveWanted(o.Index, o.Status) == "Succeeded" ? "Active" : "Succeeded", o.Status),
            o.Status == "Succeeded" ? "Set back to active" : "Mark succeeded", "Queued - undo",
            "Changes only the objective's saved status. The game may not run the follow-on steps a normal completion triggers.");
        pending.Changed += OnChanged;
    }

    public string Title { get; }
    public string Detail { get; }
    public List<FieldRowViewModel> Counters { get; } = new();
    public bool HasCounters => Counters.Count > 0;
    public bool CanEdit => _o.CanEdit;
    public ToggleActionViewModel Toggle => _toggle;

    /// <summary>The saved status, or the queued one.</summary>
    public string StatusText => _o.Status == null ? "Not started" : _pending.ObjectiveWanted(_o.Index, _o.Status)!;
    public bool IsDone => _pending.ObjectiveWanted(_o.Index, _o.Status) == "Succeeded";
    public bool IsLive => _pending.ObjectiveWanted(_o.Index, _o.Status) == "Active";

    void OnChanged()
    {
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsDone)); OnPropertyChanged(nameof(IsLive));
    }

    public void Dispose() { _pending.Changed -= OnChanged; _toggle.Dispose(); }
}

/// <summary>A fire hazard zone with an editable turns-left field.</summary>
public sealed class HazardRowViewModel
{
    public HazardRowViewModel(FieldSet fields, HazardView h)
    {
        Title = h.Title;
        TurnsLeft = fields.Make(h.TurnsLeft, 0, "Turns left");
        Detail = h.X != null && h.Y != null
            ? $"Fire zone, radius {h.Radius:0} at {h.X:0}, {h.Y:0}. Counts down one per round."
            : "Fire zone. Counts down one per round.";
    }

    public string Title { get; }
    public string Detail { get; }
    public EditableField? TurnsLeft { get; }
}


/// <summary>The enemy threat tier of the mission: a drop-down bound to the pending store.</summary>
public sealed class SpawnSetPicker : ViewModelBase, IDisposable
{
    readonly PendingChanges _p;
    readonly string _saved;

    public SpawnSetPicker(PendingChanges p, SpawnSetView v)
    {
        _p = p; _saved = v.Current;
        Choices = v.Choices.Select(c => new GroupChoice(c.Value, c.Label)).ToList();
        p.Changed += OnChanged;
    }

    public IReadOnlyList<GroupChoice> Choices { get; }

    public GroupChoice? Selected
    {
        get => Choices.FirstOrDefault(c => c.Value == _p.SpawnSetWanted(_saved));
        set { if (value != null) _p.SetSpawnSet(value.Value, _saved); }
    }

    public bool IsChanged => _p.SpawnSetChange != null;
    void OnChanged() { OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(IsChanged)); }
    public void Dispose() => _p.Changed -= OnChanged;
}

/// <summary>The mission's turn structure (standard / three-way): a drop-down bound to the pending store.</summary>
public sealed class TurnStructurePicker : ViewModelBase, IDisposable
{
    readonly PendingChanges _p;
    readonly string _saved;

    public TurnStructurePicker(PendingChanges p, TurnStructureView v)
    {
        _p = p; _saved = v.Current;
        Choices = v.Choices.Select(c => new GroupChoice(c.Value, c.Label)).ToList();
        p.Changed += OnChanged;
    }

    public IReadOnlyList<GroupChoice> Choices { get; }

    public GroupChoice? Selected
    {
        get => Choices.FirstOrDefault(c => c.Value == _p.TurnWanted(_saved));
        set { if (value != null) _p.SetTurn(value.Value, _saved); }
    }

    public bool IsChanged => _p.TurnChange != null;
    void OnChanged() { OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(IsChanged)); }
    public void Dispose() => _p.Changed -= OnChanged;
}

/// <summary>A destructible prop with editable health (maximum shown beside it) and, when it is destroyed, a Restore button.</summary>
public sealed class DestructibleRowViewModel : IDisposable
{
    readonly ToggleActionViewModel? _restore;

    public DestructibleRowViewModel(PendingChanges pending, FieldSet fields, DestructibleView d)
    {
        Title = d.Title;
        Health = fields.Make(d.Health, 0, "Health");
        Max = $"of {d.MaxHealth.Value:0}";
        Destroyed = d.Destroyed; State = d.NeedsRestore ? d.Condition : "";
        if (d.NeedsRestore)
            _restore = new ToggleActionViewModel(pending, () => pending.SceneryRestores.Contains(d.Actor), () => pending.ToggleSceneryRestore(d.Actor),
                "Restore", "Queued - undo", "Rebuilds this prop at full health. Setting health alone does not: the save also marks the prop destroyed or damaged.");
    }

    public string Title { get; }
    public string Max { get; }
    public bool Destroyed { get; }
    public string State { get; }
    public EditableField? Health { get; }
    public ToggleActionViewModel? Restore => _restore;
    public bool CanRestore => _restore != null;
    public void Dispose() => _restore?.Dispose();
}
