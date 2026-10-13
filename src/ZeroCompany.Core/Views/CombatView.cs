namespace ZeroCompany.Core.Views;

/// <summary>The in-combat state of a combat autosave (see <c>CombatOps</c>).</summary>
public sealed class CombatView
{
    public string Title { get; init; } = "";
    public List<CombatUnitView> Units { get; } = new();
    public List<ObjectiveView> Objectives { get; } = new();
    public List<TimerView> Timers { get; } = new();
    public ReinforcementsView Reinforcements { get; } = new();
    public SpawnSetView? SpawnSet { get; set; }
    public TurnStructureView? TurnStructure { get; set; }
    /// <summary>A three-way battle: Enemy2 can be used here.</summary>
    public bool ThreeWay { get; set; }
    public List<HazardView> Hazards { get; } = new();
    public List<DestructibleView> Destructibles { get; } = new();
    /// <summary>Full object paths of every prop the save marks as destroyed (with or without a health entry).</summary>
    /// <summary>Every prop the save tracks with a state tag, whatever its condition.</summary>
    public List<PropView> Props { get; } = new();
    public List<string> DestroyedProps { get; } = new();
    /// <summary>Full object paths of every prop that is destroyed or damaged: what the "Restore scenery" button brings back.</summary>
    public List<string> WreckedProps { get; } = new();
}

/// <summary>Reinforcements: waves already queued, the wave definitions the encounter draws from, and the trigger actors.</summary>
public sealed class ReinforcementsView
{
    public List<WaveView> Waves { get; } = new();
    public List<WaveConfigView> Configs { get; } = new();
    public List<TriggerView> Triggers { get; } = new();
    /// <summary>Spawn groups (full object paths) that appear in this save's reinforcement waves, for swapping a wave's group.</summary>
    public List<string> Groups { get; } = new();
}

/// <summary>A wave that is queued to arrive at a given round.</summary>
public sealed class WaveView
{
    /// <summary>Index into EncounterLogicActorWrappers (the encounter actor) and into its queued waves.</summary>
    public int Logic { get; init; }
    public int Index { get; init; }
    public string Group { get; init; } = "";
    public string EntryPoint { get; init; } = "";
    public int? ConfigIndex { get; init; }
    public FieldRef? Round { get; init; }
}

/// <summary>One of the encounter's reinforcement wave definitions.</summary>
public sealed class WaveConfigView
{
    public int Logic { get; init; }
    public int Index { get; init; }
    public string Group { get; init; } = "";
    public string EntryPoint { get; init; } = "";
    public bool Automatic { get; init; }
    public FieldRef? Delay { get; init; }
    public FieldRef? TimesToSpawn { get; init; }
    public FieldRef? TimesTriggered { get; init; }
}

/// <summary>A BP_TriggerReinforcements actor; <see cref="HasFlag"/> says whether its active flag is saved (and so editable).</summary>
public sealed class TriggerView
{
    public int Logic { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public bool HasFlag { get; init; }
    public bool Active { get; init; }
}

/// <summary>A labelled integer on an objective (points so far, interactions done ...).</summary>
public sealed class CounterView
{
    public string Label { get; init; } = "";
    public FieldRef Ref { get; init; } = new();
}

/// <summary>A countdown timer that fails objectives when it runs out.</summary>
public sealed class TimerView
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Turns left; null once the timer has finished and the game no longer saves it.</summary>
    public FieldRef? Remaining { get; init; }
    public FieldRef? Total { get; init; }
    /// <summary>What the timer counts, e.g. TeamTurnEnd (the end of the team's turn).</summary>
    public string CountsOn { get; init; } = "";
    /// <summary>Objectives that fail when the timer reaches zero.</summary>
    public List<string> Fails { get; } = new();
}

/// <summary>One mission or tactical objective (an encounter-logic actor with an ObjectiveStatus).</summary>
public sealed class ObjectiveView
{
    /// <summary>Index into EncounterLogicActorWrappers.</summary>
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>"Active" or "Succeeded" (the only values the game writes), or null when the objective has not started.</summary>
    public string? Status { get; init; }
    /// <summary>What has to be interacted with, for interact objectives.</summary>
    public List<string> Targets { get; } = new();
    public List<CounterView> Counters { get; } = new();
    public bool CanEdit => Status != null;
}

public sealed class CombatUnitView
{
    public int Index { get; init; }
    public string Actor { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Last segment of the team tag: Player, Enemy, Civilian ...</summary>
    public string Team { get; init; } = "";
    public bool Dead { get; init; }
    /// <summary>True when the unit runs the player behaviour (a hero class built for a player to control).</summary>
    public bool PlayerBehavior { get; init; }
    public FieldRef? Advantage { get; init; }
    public FieldRef? X { get; set; }
    public FieldRef? Y { get; set; }
    public FieldRef? Z { get; set; }
    public List<AttributeView> Attributes { get; } = new();
    public List<UnitEffectView> Effects { get; } = new();

    public FieldRef? Attr(string name) => Attributes.FirstOrDefault(a => a.Name == name)?.Ref;
}

public sealed class AttributeView
{
    public string Set { get; init; } = "";
    public string Name { get; init; } = "";
    public FieldRef Ref { get; init; } = new();
}

public sealed class UnitEffectView
{
    public int Index { get; init; }
    public string Def { get; init; } = "";
    public string Title { get; init; } = "";
    public FieldRef? Stacks { get; init; }
    /// <summary>Seconds left, or -1 for "until removed".</summary>
    public double? Duration { get; init; }
}

/// <summary>A fire hazard zone (an ability actor) that burns for a number of turns.</summary>
public sealed class HazardView
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public FieldRef TurnsLeft { get; init; } = new();
    public double? Radius { get; init; }
    public double? X { get; set; }
    public double? Y { get; set; }
}

/// <summary>A destructible prop (railing, crate, canister ...) with its health and maximum.</summary>
public sealed class DestructibleView
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public FieldRef Health { get; init; } = new();
    public FieldRef MaxHealth { get; init; } = new();
    /// <summary>The full object path of the prop (what the save records it by).</summary>
    public string Actor { get; init; } = "";
    /// <summary>"Destroyed", "Damaged" or "Pristine" from the prop's actor record: that tag, not health, is what keeps it wrecked when the save loads.</summary>
    public string Condition { get; init; } = "";
    public bool Destroyed => Condition == "Destroyed";
    /// <summary>True when Restore can do something: the prop is destroyed or damaged.</summary>
    public bool NeedsRestore => Condition is "Destroyed" or "Damaged";
}

/// <summary>The enemy threat tier the mission draws its units from, with the tiers that can be swapped in.</summary>
public sealed class SpawnSetView
{
    public string Current { get; init; } = "";
    /// <summary>Encounters after the current one that have not started: the only ones the tier can still change.</summary>
    public int EncountersLeft { get; init; }
    public List<(string Value, string Label)> Choices { get; } = new();
}

/// <summary>The mission's turn structure: standard (two sides) or three-way battle (a second hostile team takes its own turns).</summary>
public sealed class TurnStructureView
{
    public string Current { get; init; } = "";
    public List<(string Value, string Label)> Choices { get; } = new();
}

/// <summary>A scenery prop the save tracks (every one with a state tag, not only the damaged ones that have a health entry).</summary>
public sealed class PropView
{
    /// <summary>The full object path, which is how the save and the actions refer to the prop.</summary>
    public string Actor { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>The sub-level the prop sits in, e.g. HM_Cly_020_Gameplay_Cover (useful for searching).</summary>
    public string Area { get; init; } = "";
    /// <summary>"Pristine", "Damaged" or "Destroyed".</summary>
    public string Condition { get; init; } = "";
    /// <summary>Its health entry, when the game has one (it only has one for props that were hit).</summary>
    public FieldRef? Health { get; init; }
    public FieldRef? MaxHealth { get; init; }
}
