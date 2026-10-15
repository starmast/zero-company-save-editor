namespace ZeroCompany.Core.Actions;

/// <summary>What is known about one saved attribute: how to show it and any limits worth enforcing.</summary>
public sealed record StatInfo(string Group, string Label, string Note = "", double Min = -1e6, double Max = 1e6);

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
    const string Rebuilt = "In testing the game rebuilt this from the equipped weapon when the unit first fired, so an edit may only last until then.";
    const string Kept = "Kept after the unit fired in testing.";
    const string Scratch = "Scratch value the game fills in while it resolves a hit; normally 0, so editing it does nothing useful.";

    static readonly Dictionary<(string Set, string Name), StatInfo> Known = new()
    {
        [("BitReactorHealthSet", "Health")] = new(Health, "Health", "Current health. Damage lowers the saved base too.", 0),
        [("BitReactorHealthSet", "MaxHealth")] = new(Health, "Max health (base)", BaseOnly + " A hero with base 28 can have 151 in the game once gear is counted.", 0),
        [("BitReactorHealthSet", "IncomingDamageMultiplier")] = new(Defense, "Incoming damage multiplier", "1 = normal, 0.5 = half damage taken.", 0),
        [("BitReactorHealthSet", "FlatDamageReduction")] = new(Defense, "Flat damage reduction", "Armor and shields are added into this by effects, so the in-game value is usually higher than the base.", 0),
        [("BitReactorHealthSet", "ChanceToIgnoreIncomingDamage")] = new(Defense, "Chance to ignore damage", Chance, 0, 1),
        [("BitReactorHealthSet", "IncomingHealMulitplier")] = new(Health, "Incoming heal multiplier", "1 = normal.", 0),
        [("BitReactorHealthSet", "FlatHealIncrease")] = new(Health, "Flat heal bonus", "", 0),
        [("BitReactorHealthSet", "PostEncounterAutoHealPercentage")] = new(Health, "Heal after the encounter", "1 = full heal.", 0, 1),
        [("BitReactorHealthSet", "PreviewHealth")] = new(Internal, "Preview health", Scratch),
        [("BitReactorHealthSet", "Healing")] = new(Internal, "Healing", Scratch),
        [("BitReactorHealthSet", "Damage")] = new(Internal, "Damage", Scratch),
        [("BitReactorHealthSet", "ShieldDamage")] = new(Internal, "Shield damage", Scratch),
        [("BitReactorHealthSet", "HealthDamage")] = new(Internal, "Health damage", Scratch),

        [("BitReactorCombatSet", "ActionPoints")] = new(Actions, "Action points", "Spent by attacks and abilities (a standard attack costs 2). More than the usual 3 works in the game, but its own pips only show the usual row.", 0, 99),
        [("BitReactorCombatSet", "MovementActionPoints")] = new(Actions, "Movement action points", "", 0, 99),
        [("BitReactorCombatSet", "RefreshActionPoints")] = new(Actions, "Action points per turn", "What the unit gets back at the start of its turn.", 0, 99),
        [("BitReactorCombatSet", "RefreshMovementActionPoints")] = new(Actions, "Movement points per turn", "", 0, 99),
        [("BitReactorCombatSet", "MovementPerAP")] = new(Actions, "Movement per action point", "Distance one point of movement covers, in game units.", 0),
        [("BitReactorCombatSet", "ClassTacticPoints")] = new(Actions, "Class tactic points", ""),
        [("BitReactorCombatSet", "RefreshClassTacticPoints")] = new(Actions, "Class tactic points per turn", ""),
        [("BitReactorCombatSet", "SpecialActionPoints")] = new(Actions, "Special action points", ""),
        [("BitReactorCombatSet", "RefreshSpecialActionPoints")] = new(Actions, "Special points per turn", ""),
        [("BitReactorCombatSet", "AbilityUsesPerCooldown")] = new(Actions, "Ability uses per cooldown", "", 0),

        [("BitReactorCombatSet", "Attack")] = new(Offense, "Attack", "Raising it from 7 to 30 did not change the damage of a standard shot in testing."),
        [("BitReactorCombatSet", "Accuracy")] = new(Offense, "Accuracy"),
        [("BitReactorCombatSet", "MaxAccuracy")] = new(Offense, "Max accuracy"),
        [("BitReactorCombatSet", "AccuracyReduction")] = new(Offense, "Accuracy reduction", "Effects lower this in the game (a hero showed -2 from a base of 0)."),
        [("BitReactorCombatSet", "Aim")] = new(Offense, "Aim"),
        [("BitReactorCombatSet", "DamageMultiplier")] = new(Offense, "Damage multiplier", "1 = normal. A standard shot did the same damage at 1 and at 3 in testing.", 0),
        [("BitReactorCombatSet", "BonusDamage")] = new(Offense, "Bonus damage", "The game recomputes the current value from effects after a shot; a saved base of 20 showed no effect in testing."),
        [("BitReactorCombatSet", "BonusShieldDamage")] = new(Offense, "Bonus shield damage"),
        [("BitReactorCombatSet", "DefensePenetration")] = new(Offense, "Defense penetration"),
        [("BitReactorCombatSet", "CriticalHitCount")] = new(Offense, "Critical hit count", "", 0),
        [("BitReactorCombatSet", "MaxDamageAllowed")] = new(Offense, "Max damage per hit", "", 0),
        [("BitReactorCombatSet", "BonusHealing")] = new(Offense, "Bonus healing"),
        [("BitReactorCombatSet", "OutgoingHealMultiplier")] = new(Offense, "Outgoing heal multiplier", "1 = normal.", 0),
        [("BitReactorCombatSet", "ShockApplyChanceBonus")] = new(Offense, "Shock chance bonus"),
        [("BitReactorCombatSet", "BurnApplyChanceBonus")] = new(Offense, "Burn chance bonus"),
        [("BitReactorCombatSet", "VengeanceTriggerThreshold")] = new(Offense, "Vengeance threshold"),

        [("BitReactorCombatSet", "Defense")] = new(Defense, "Defense"),
        [("BitReactorCombatSet", "DeflectionChance")] = new(Defense, "Deflection chance"),
        [("BitReactorCombatSet", "BaseArmor")] = new(Defense, "Armor (base)", "An effect adds armor into flat damage reduction. Both the base and current armor are stored.", 0),
        [("BitReactorCombatSet", "CurrentArmor")] = new(Defense, "Armor (current)", "Armor left now; damage can wear it down.", 0),
        [("BitReactorCombatSet", "BaseShields")] = new(Defense, "Shields (base)", "", 0),
        [("BitReactorCombatSet", "CurrentShields")] = new(Defense, "Shields (current)", "", 0),
        [("BitReactorCombatSet", "CriticalHitResistance")] = new(Defense, "Critical hit resistance"),

        [("BitReactorCombatSet", "BonusOptimizationsSlots")] = new("Other", "Bonus optimization slots", "", 0),
        [("BitReactorCombatSet", "BonusEquipmentSlots")] = new("Other", "Bonus equipment slots", "", 0),
        [("BitReactorCombatSet", "BonusModificationSlots")] = new("Other", "Bonus modification slots", "", 0),

        [("BitReactorAdvantageSet", "Advantage")] = new("Other", "Advantage (this unit)", "", 0),
        [("BitReactorAdvantageSet", "MaximumAdvantage")] = new("Other", "Maximum advantage", "", 0),

        [("BitReactorGrenadeUserSet", "GrenadeMaxThrowDistance")] = new(Grenade, "Throw distance", "Game units; gear raises it in the game (1800 saved, 2000 shown).", 0),
        [("BitReactorGrenadeUserSet", "GrenadeBlastRadius")] = new(Grenade, "Blast radius", "Game units.", 0),
        [("BitReactorGrenadeUserSet", "GrenadeDamage")] = new(Grenade, "Damage", "", 0),
        [("BitReactorGrenadeUserSet", "GrenadeBonusDamage")] = new(Grenade, "Bonus damage"),
        [("BitReactorGrenadeUserSet", "CriticalHitChance")] = new(Grenade, "Critical chance", Chance, 0, 1),
        [("BitReactorGrenadeUserSet", "CriticalHitDamage")] = new(Grenade, "Critical damage", "", 0),
    };

    /// <summary>Names shared by the ranged and melee weapon sets, shown under whichever weapon set holds them.</summary>
    static readonly Dictionary<string, (string Label, string Note, double Min, double Max)> Weapon = new()
    {
        ["Damage"] = ("Damage", BaseOnly + " A hero's ranged base 12 showed 16 in the game with gear. " + Rebuilt, 0, 1e6),
        ["CriticalHitChance"] = ("Critical chance", Chance + " " + BaseOnly + " " + Rebuilt, 0, 1),
        ["CriticalHitDamage"] = ("Critical damage", Rebuilt, 0, 1e6),
        ["Range"] = ("Range", Rebuilt, 0, 1e6),
        ["RangeAbsoluteMin"] = ("Range: absolute min", "Game units. " + Rebuilt, 0, 1e6),
        ["RangeAbsoluteMax"] = ("Range: absolute max", "Game units. " + Rebuilt, 0, 1e6),
        ["RangeOptimalMin"] = ("Range: optimal min", "Game units. " + Rebuilt, 0, 1e6),
        ["RangeOptimalMax"] = ("Range: optimal max", "Game units; gear raised 1000 to 1200 in the game. " + Rebuilt, 0, 1e6),
        ["CloseRangeFalloffIncrement"] = ("Close range falloff", "", 0, 1e6),
        ["LongRangeFalloffIncrement"] = ("Long range falloff", "", 0, 1e6),
        ["FlankDamage"] = ("Flank damage", Kept, -1e6, 1e6),
        ["SureshotDamage"] = ("Sureshot damage", "The saved base was kept after the unit fired in testing, but the game showed 0 as the current value.", -1e6, 1e6),
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
        if (group != "Other" && Weapon.TryGetValue(name, out var w)) return new StatInfo(group, w.Label, w.Note, w.Min, w.Max);
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
