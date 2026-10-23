namespace ZeroCompany.Core.Actions;

/// <summary>Whether a saved attribute value is still there once the game has loaded the save and the unit has acted.</summary>
public enum StatKeep
{
    /// <summary>Kept.</summary>
    Yes,
    /// <summary>The game rebuilds it from the equipped gear for a player hero, so the edit is lost; other units keep it.</summary>
    NotForHeroes,
    /// <summary>The game puts it back, it is a scratch value, or changing it showed no effect: editing it does nothing that was found.</summary>
    No,
}

/// <summary>What is known about one saved attribute: how to show it, any limits worth enforcing, and whether the game keeps an edit.</summary>
public sealed record StatInfo(string Group, string Label, string Note = "", double Min = -1e6, double Max = 1e6, StatKeep Keep = StatKeep.Yes);

/// <summary>
/// Names, groups and notes for the attribute base values a combat save stores per unit. The save keeps only each attribute's
/// BaseValue; the game's current value is that base plus gear and effects (a hero's base MaxHealth is 28 but the game shows 151).
/// Notes only claim what was checked against the running game; the rest are named from the attribute itself.
/// </summary>
public static class CombatStatCatalog
{
    public const string Health = "Health", Actions = "Actions", Offense = "Offense", Defense = "Defense",
                        Ranged = "Ranged weapon", Melee = "Melee weapon", Grenade = "Grenade", Internal = "Internal";

    /// <summary>Display order of the groups.</summary>
    public static readonly string[] GroupOrder = { Health, Actions, Offense, Defense, Ranged, Melee, Grenade, "Other", Internal };

    const string Chance = "A fraction: 0.05 is 5%.";
    const string BaseOnly = "The save holds the base; gear and effects add on top in the game.";
    const string Rebuilt = "For your own heroes the game rebuilds this from the equipped weapon (on load, or when the unit first acts), so an edit is lost and never used in an attack.";
    const string Kept = "Stays after the unit fires.";
    const string Scratch = "Scratch value the game fills in while it resolves a hit; normally 0, so editing it does nothing useful.";
    const string NoShotEffect = "The game does not use it for hit chance or damage.";
    const string RefreshIgnored = "Heroes get the game's own amount at the start of each turn, whatever this says.";
    const string PerTurn = "The game sets it back at the start of the unit's turn, so an edit only lasts for the turn the save was made in.";

    /// <summary>Weapon stats the game rebuilds from the equipped weapon for player heroes (checked live: they went back to the weapon's values).</summary>
    static readonly HashSet<string> WeaponRebuilt = new()
    {
        "Damage", "CriticalHitChance", "CriticalHitDamage", "Range", "RangeAbsoluteMin", "RangeAbsoluteMax", "RangeOptimalMin", "RangeOptimalMax",
        "CloseRangeFalloffIncrement", "LongRangeFalloffIncrement", "ReactionDamage", "ReactionChanceToHit", "ReactionCriticalHitChance",
        "AssistDamage", "AssistCriticalHitChance", "OverwatchRange", "OverwatchArc", "LOSTraceRadius", "MaxSkillBonus", "RetainMarkChance",
    };

    static readonly Dictionary<(string Set, string Name), StatInfo> Known = new()
    {
        [("BitReactorHealthSet", "Health")] = new(Health, "Health", "Current health. Damage lowers the saved base too. The game limits it to the unit's real maximum when it loads the save, for allies and enemies alike.", 0),
        [("BitReactorHealthSet", "MaxHealth")] = new(Health, "Max health (base)", BaseOnly + " A hero's in-game maximum is several times the base once gear and progression are counted.", 0),
        [("BitReactorHealthSet", "IncomingDamageMultiplier")] = new(Defense, "Incoming damage multiplier", "1 = normal, 0.5 = half damage taken.", 0),
        [("BitReactorHealthSet", "FlatDamageReduction")] = new(Defense, "Flat damage reduction", "Takes this much off every hit. Armor and shields are added into it by effects, so the in-game value is usually higher than the base.", 0),
        [("BitReactorHealthSet", "ChanceToIgnoreIncomingDamage")] = new(Defense, "Chance to ignore damage", Chance + " At 1 the unit takes no damage from hits.", 0, 1),
        [("BitReactorHealthSet", "IncomingHealMulitplier")] = new(Health, "Incoming heal multiplier", "1 = normal.", 0),
        [("BitReactorHealthSet", "FlatHealIncrease")] = new(Health, "Flat heal bonus", "", 0),
        [("BitReactorHealthSet", "PostEncounterAutoHealPercentage")] = new(Health, "Heal after the encounter", "1 = full heal.", 0, 1),
        [("BitReactorHealthSet", "PreviewHealth")] = new(Internal, "Preview health", Scratch, Keep: StatKeep.No),
        [("BitReactorHealthSet", "Healing")] = new(Internal, "Healing", Scratch, Keep: StatKeep.No),
        [("BitReactorHealthSet", "Damage")] = new(Internal, "Damage", Scratch, Keep: StatKeep.No),
        [("BitReactorHealthSet", "ShieldDamage")] = new(Internal, "Shield damage", Scratch, Keep: StatKeep.No),
        [("BitReactorHealthSet", "HealthDamage")] = new(Internal, "Health damage", Scratch, Keep: StatKeep.No),

        [("BitReactorCombatSet", "ActionPoints")] = new(Actions, "Action points", "Spent by attacks and abilities (a standard attack costs 2). The game limits this to 3 when it loads the save, so a higher number comes back as 3. " + PerTurn, 0, 3),
        [("BitReactorCombatSet", "MovementActionPoints")] = new(Actions, "Movement action points", "", 0, 99),
        [("BitReactorCombatSet", "RefreshActionPoints")] = new(Actions, "Action points per turn", RefreshIgnored + " For heroes that is 3.", 0, 99, StatKeep.NotForHeroes),
        [("BitReactorCombatSet", "RefreshMovementActionPoints")] = new(Actions, "Movement points per turn", RefreshIgnored, 0, 99, StatKeep.NotForHeroes),
        [("BitReactorCombatSet", "MovementPerAP")] = new(Actions, "Movement per action point", "Distance one point of movement covers, in game units. Doubling it about doubles the movement range.", 0),
        [("BitReactorCombatSet", "ClassTacticPoints")] = new(Actions, "Class tactic points", PerTurn),
        [("BitReactorCombatSet", "RefreshClassTacticPoints")] = new(Actions, "Class tactic points per turn", "The class tactic points the unit gets at the start of each turn (0 = none)."),
        [("BitReactorCombatSet", "SpecialActionPoints")] = new(Actions, "Special action points", PerTurn),
        [("BitReactorCombatSet", "RefreshSpecialActionPoints")] = new(Actions, "Special points per turn", RefreshIgnored + " For heroes that is 0.", Keep: StatKeep.NotForHeroes),
        [("BitReactorCombatSet", "AbilityUsesPerCooldown")] = new(Actions, "Ability uses per cooldown", "", 0),

        [("BitReactorCombatSet", "Attack")] = new(Offense, "Attack", NoShotEffect, Keep: StatKeep.No),
        [("BitReactorCombatSet", "Accuracy")] = new(Offense, "Accuracy", NoShotEffect, Keep: StatKeep.No),
        [("BitReactorCombatSet", "MaxAccuracy")] = new(Offense, "Max accuracy", "Caps the hit chance of this unit's shots: 0.2 limits every shot to 20%, whatever the odds would be."),
        [("BitReactorCombatSet", "AccuracyReduction")] = new(Offense, "Accuracy reduction", "Each point takes 5 points off the hit chance of this unit's shots. Effects also lower it in the game."),
        [("BitReactorCombatSet", "Aim")] = new(Offense, "Aim", NoShotEffect, Keep: StatKeep.No),
        [("BitReactorCombatSet", "DamageMultiplier")] = new(Offense, "Damage multiplier", "1 = normal. Multiplies the damage of this unit's attacks: 2 doubles it.", 0),
        [("BitReactorCombatSet", "BonusDamage")] = new(Offense, "Bonus damage", "Added to the damage of this unit's attacks before the damage multiplier."),
        [("BitReactorCombatSet", "BonusShieldDamage")] = new(Offense, "Bonus shield damage"),
        [("BitReactorCombatSet", "DefensePenetration")] = new(Offense, "Defense penetration", "Cancels the target's armor: as much penetration as the target has armor lets a shot do its full damage."),
        [("BitReactorCombatSet", "CriticalHitCount")] = new(Offense, "Critical hit count", "", 0),
        [("BitReactorCombatSet", "MaxDamageAllowed")] = new(Offense, "Max damage per hit", "Caps what a single hit of this unit can do: 10 limits every hit to 10.", 0),
        [("BitReactorCombatSet", "BonusHealing")] = new(Offense, "Bonus healing"),
        [("BitReactorCombatSet", "OutgoingHealMultiplier")] = new(Offense, "Outgoing heal multiplier", "1 = normal.", 0),
        [("BitReactorCombatSet", "ShockApplyChanceBonus")] = new(Offense, "Shock chance bonus", "Attacks do not use it to shock their target.", Keep: StatKeep.No),
        [("BitReactorCombatSet", "BurnApplyChanceBonus")] = new(Offense, "Burn chance bonus", "Attacks do not use it to burn their target.", Keep: StatKeep.No),
        [("BitReactorCombatSet", "VengeanceTriggerThreshold")] = new(Offense, "Vengeance threshold"),

        [("BitReactorCombatSet", "Defense")] = new(Defense, "Defense", NoShotEffect, Keep: StatKeep.No),
        [("BitReactorCombatSet", "DeflectionChance")] = new(Defense, "Deflection chance", "Multiplies the hit chance of shots at this unit by (1 - value): 0.25 turns a 60% shot into 45%, and 1 or more makes it impossible to hit."),
        [("BitReactorCombatSet", "BaseArmor")] = new(Defense, "Armor (base)", "Cuts every hit this unit takes; enough armor stops a hit completely. An effect adds armor into flat damage reduction; set the current armor too.", 0),
        [("BitReactorCombatSet", "CurrentArmor")] = new(Defense, "Armor (current)", "The armor the unit has now (the HUD shows this number). It cuts every hit; enough armor stops a hit completely.", 0),
        [("BitReactorCombatSet", "BaseShields")] = new(Defense, "Shields (base)", "Set the current shields too.", 0),
        [("BitReactorCombatSet", "CurrentShields")] = new(Defense, "Shields (current)", "A pool that takes damage before health does.", 0),
        [("BitReactorCombatSet", "CriticalHitResistance")] = new(Defense, "Critical hit resistance"),

        [("BitReactorCombatSet", "BonusOptimizationsSlots")] = new("Other", "Bonus optimization slots", "", 0),
        [("BitReactorCombatSet", "BonusEquipmentSlots")] = new("Other", "Bonus equipment slots", "", 0),
        [("BitReactorCombatSet", "BonusModificationSlots")] = new("Other", "Bonus modification slots", "", 0),

        [("BitReactorAdvantageSet", "Advantage")] = new("Other", "Advantage (this unit)", "The game puts this back to its own value when it loads the save.", 0, Keep: StatKeep.No),
        [("BitReactorAdvantageSet", "MaximumAdvantage")] = new("Other", "Maximum advantage", "The game puts this back to its own value when it loads the save.", 0, Keep: StatKeep.No),

        [("BitReactorGrenadeUserSet", "GrenadeMaxThrowDistance")] = new(Grenade, "Throw distance", "Game units; gear raises it in the game. " + Rebuilt, 0, Keep: StatKeep.NotForHeroes),
        [("BitReactorGrenadeUserSet", "GrenadeBlastRadius")] = new(Grenade, "Blast radius", "Game units. " + Rebuilt, 0, Keep: StatKeep.NotForHeroes),
        [("BitReactorGrenadeUserSet", "GrenadeDamage")] = new(Grenade, "Damage", Rebuilt, 0, Keep: StatKeep.NotForHeroes),
        [("BitReactorGrenadeUserSet", "GrenadeBonusDamage")] = new(Grenade, "Bonus damage", "Thrown grenades do not use it: the grenade item sets its own damage.", Keep: StatKeep.No),
        [("BitReactorGrenadeUserSet", "CriticalHitChance")] = new(Grenade, "Critical chance", Chance + " With Critical damage it raises what a grenade does.", 0, 1),
        [("BitReactorGrenadeUserSet", "CriticalHitDamage")] = new(Grenade, "Critical damage", "Extra damage when a grenade crits.", 0),
    };

    /// <summary>Names shared by the ranged and melee weapon sets, shown under whichever weapon set holds them.</summary>
    static readonly Dictionary<string, (string Label, string Note, double Min, double Max)> Weapon = new()
    {
        ["Damage"] = ("Damage", BaseOnly + " " + Rebuilt + " An enemy keeps it and uses it in its attacks (armor still comes off).", 0, 1e6),
        ["CriticalHitChance"] = ("Critical chance", Chance + " " + BaseOnly + " " + Rebuilt, 0, 1),
        ["CriticalHitDamage"] = ("Critical damage", Rebuilt, 0, 1e6),
        ["Range"] = ("Range", Rebuilt, 0, 1e6),
        ["RangeAbsoluteMin"] = ("Range: absolute min", "Game units. " + Rebuilt, 0, 1e6),
        ["RangeAbsoluteMax"] = ("Range: absolute max", "Game units. " + Rebuilt, 0, 1e6),
        ["RangeOptimalMin"] = ("Range: optimal min", "Game units. " + Rebuilt, 0, 1e6),
        ["RangeOptimalMax"] = ("Range: optimal max", "Game units; gear raises it in the game. " + Rebuilt, 0, 1e6),
        ["CloseRangeFalloffIncrement"] = ("Close range falloff", "", 0, 1e6),
        ["LongRangeFalloffIncrement"] = ("Long range falloff", "", 0, 1e6),
        ["FlankDamage"] = ("Flank damage", Kept, -1e6, 1e6),
        ["SureshotDamage"] = ("Sureshot damage", "The saved base stays after the unit fires, but the game may show 0 as the current value.", -1e6, 1e6),
        ["CloseRangeDamage"] = ("Close range damage", "", -1e6, 1e6),
        ["LongRangeDamage"] = ("Long range damage", "", -1e6, 1e6),
        ["HighGroundDamage"] = ("High ground damage", "", -1e6, 1e6),
        ["AssistDamage"] = ("Assist damage", "", -1e6, 1e6),
        ["AssistCriticalHitChance"] = ("Assist critical chance", Chance, 0, 1),
        ["ReactionDamage"] = ("Reaction damage", Rebuilt, -1e6, 1e6),
        ["ReactionCriticalHitChance"] = ("Reaction critical chance", Chance, 0, 1),
        ["ReactionChanceToHit"] = ("Reaction chance to hit", "", 0, 1e6),
        ["OverwatchRange"] = ("Overwatch range", "Game units.", 0, 1e6),
        ["OverwatchArc"] = ("Overwatch arc", "", 0, 360),
        ["StoppingPower"] = ("Stopping power", Kept, 0, 1e6),
        ["LOSTraceRadius"] = ("Line-of-sight trace radius", "", 0, 1e6),
    };

    /// <summary>The catalog entry for an attribute, or a plain one named from the attribute for anything not listed.</summary>
    public static StatInfo Of(string set, string name)
    {
        if (Known.TryGetValue((set, name), out var k)) return k;
        var group = set switch { "BitReactorRangedWeaponSet" => Ranged, "BitReactorMeleeWeaponSet" => Melee, _ => "Other" };
        if (group != "Other" && Weapon.TryGetValue(name, out var w))
            return new StatInfo(group, w.Label, w.Note, w.Min, w.Max, WeaponRebuilt.Contains(name) ? StatKeep.NotForHeroes : StatKeep.Yes);
        return new StatInfo(group, Spaced(name));
    }

    /// <summary>"LongRangeFalloffIncrement" -> "Long range falloff increment".</summary>
    public static string Spaced(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])))) sb.Append(' ');
            sb.Append(name[i]);
        }
        var s = sb.ToString().ToLowerInvariant();
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }
}
