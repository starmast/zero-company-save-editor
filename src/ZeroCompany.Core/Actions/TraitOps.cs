using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Bond traits: the class traits an operator learns from a bond partner of another class through cross training (for example a
/// Scout teaching Difficult Target). Each is a gameplay effect under BondData/TestCrossTrainingEffects that grants the trait's
/// ability, and an operator who has learned one keeps it as one record in their character data's GameplayEffectsToPersist; nothing
/// else in the save refers to it. <see cref="Give"/> appends such a record: a copy of a trait record already in the save (the
/// operator's own when they have one, so the captured tags are theirs), with its effect class switched to the trait wanted. Early in a
/// campaign nobody has a trait yet; every persisted effect record has the same layout, so another one of the operator's own (their
/// health progression, say) serves instead, with its stat modifiers removed (a trait has none) and one stack.
/// </summary>
public static class TraitOps
{
    public const string Folder = "/Game/Game/GameData/BondData/TestCrossTrainingEffects/";

    /// <summary>One trait: its effect asset, whether it is an offense, defense or support trait, the class that teaches it, and its in-game name.</summary>
    public sealed record Trait(string Asset, string Kind, string Class, string Name);

    static Trait T(string asset, string name)
    {
        var p = asset.Split('_');
        return new Trait(asset, p[1], p[2], name);
    }

    /// <summary>Every bond trait the game defines (32), with the name the game shows for it.</summary>
    public static readonly IReadOnlyList<Trait> All = new[]
    {
        T("GE_Offense_Assault_AggressiveCombat", "Aggressive Combat"), T("GE_Offense_Assault_BehindTheLines", "Behind The Lines"),
        T("GE_Offense_Assault_CourageUnderFire", "Courage Under Fire"),
        T("GE_Offense_Astromech_Tinkerer", "Tinkerer"), T("GE_Support_Astromech_DeepCover", "Deep Cover"), T("GE_Support_Astromech_IntoTheSmoke", "Into The Smoke"),
        T("GE_Offense_Gunslinger_OneTwoThree", "One, Two, Three"), T("GE_Offense_Gunslinger_PinpointParade", "Pinpoint Parade"),
        T("GE_Offense_Gunslinger_Premeditation", "Premeditation"),
        T("GE_Defense_Heavy_DoubleDown", "Double Down"), T("GE_Defense_Heavy_ToughAsNails", "Tough As Nails"), T("GE_Support_Heavy_Resilience", "Resilience"),
        T("GE_Support_Medic_BodyAndMind", "Body And Mind"), T("GE_Support_Medic_HealthAndVigor", "Health And Vigor"), T("GE_Support_Medic_SavingGrace", "Saving Grace"),
        T("GE_Defense_Padawan_MysteriousConnection", "Mysterious Connection"), T("GE_Offense_Padawan_FlowOfTheDuelist", "Flow Of The Duelist"),
        T("GE_Offense_Scoundrel_FinishThemOff", "Finish Them Off"), T("GE_Support_Scoundrel_InOnTheAction", "In On The Action"),
        T("GE_Support_Scoundrel_OpportunityKnocks", "Opportunity Knocks"),
        T("GE_Defense_Scout_DifficultTarget", "Difficult Target"), T("GE_Defense_Scout_SlipperyTarget", "Slip Through The Gaps"),
        T("GE_Support_Scout_PackTactics", "Pack Tactics"),
        T("GE_Offense_Sniper_NoOneIsSafe", "No One Is Safe"), T("GE_Offense_Sniper_SuperiorPosition", "Superior Position"),
        T("GE_Offense_Sniper_TacticalOffense", "Tactical Offense"),
        T("GE_Offense_Soldier_ExplosivesMaster", "Explosives Master"), T("GE_Offense_Soldier_Grenadier", "Grenadier"),
        T("GE_Offense_Soldier_WearThemDown", "Wear Them Down"),
        T("GE_Offense_Warrior_ShockingMoments", "Aftershock"), T("GE_Support_Warrior_NoMercy", "No Mercy"), T("GE_Support_Warrior_ProtectiveLayers", "Hazard Training"),
    };

    public static Trait? Find(string asset) => All.FirstOrDefault(t => t.Asset == asset);

    /// <summary>The effect class path the save stores for a trait.</summary>
    public static string DefPath(string asset) => $"{Folder}{asset}.Default__{asset}_C";

    static string? DefOf(GvasFile g, GvasNode el) => g.Child(el, "Def") is { } d ? g.GetStr(d) : null;

    static bool IsTraitRecord(GvasFile g, GvasNode el) => DefOf(g, el) is { } d && d.StartsWith(Folder, StringComparison.Ordinal);

    /// <summary>The bond traits (effect asset names) an operator has.</summary>
    public static List<string> Held(GvasFile g, string guid) =>
        (MedbayOps.EffectsNode(g, guid)?.Children ?? new List<GvasNode>()).Where(el => IsTraitRecord(g, el))
            .Select(el => MedbayOps.EffectName(g, el)).ToList();

    static List<GvasNode> Records(GvasFile g, string gid) => MedbayOps.EffectsNode(g, gid)?.Children ?? new List<GvasNode>();

    /// <summary>A permanent effect with a plain layout: the kind of record a trait can be made from once its modifiers are removed.</summary>
    static bool IsPlainPermanent(GvasFile g, GvasNode el) =>
        g.Child(el, "Duration") is { } d && g.GetDouble(d) < 0 && g.Child(el, "Modifiers") != null && g.Child(el, "StackCount") != null
        && (g.Child(el, "GrantedAbilitySpecs")?.Children?.Count ?? 0) == 0
        && DefOf(g, el) is { } p && (p.Contains("/CrossTrainingStatRewards/", StringComparison.Ordinal) || p.Contains("/GameData/Progression/", StringComparison.Ordinal));

    /// <summary>
    /// A record to copy for <paramref name="guid"/>, and whether it is a trait record: the operator's own trait, else another operator's
    /// record of the trait wanted, else any trait record in the save, else the operator's own cross-training or progression record.
    /// Null when there is none of these.
    /// </summary>
    static (byte[] Raw, bool Trait)? Template(GvasFile g, string guid, string asset)
    {
        GvasNode? Pick(string gid, Func<GvasNode, bool> ok) => Records(g, gid).FirstOrDefault(el => IsTraitRecord(g, el) && ok(el));
        var el = Pick(guid, _ => true)
                 ?? MedbayOps.Characters(g).Keys.Select(gid => Pick(gid, e => MedbayOps.EffectName(g, e) == asset)).FirstOrDefault(e => e != null)
                 ?? MedbayOps.Characters(g).Keys.Select(gid => Pick(gid, _ => true)).FirstOrDefault(e => e != null);
        if (el != null) return (g.Slice(el.Start, el.End), true);
        var plain = Records(g, guid).FirstOrDefault(e => IsPlainPermanent(g, e));
        return plain == null ? null : (g.Slice(plain.Start, plain.End), false);
    }

    /// <summary>Whether a trait can be given to <paramref name="guid"/>: the save has a trait record, or the operator a plain permanent effect, to copy.</summary>
    public static bool CanGive(GvasFile g, string guid) =>
        MedbayOps.Characters(g).Keys.Any(gid => Records(g, gid).Any(el => IsTraitRecord(g, el))) || Records(g, guid).Any(el => IsPlainPermanent(g, el));

    /// <summary>The traits <paramref name="guid"/> could be given: every trait they do not have, when there is a record to copy.</summary>
    public static List<Trait> Givable(GvasFile g, string guid)
    {
        if (MedbayOps.EffectsNode(g, guid) == null || !CanGive(g, guid)) return new List<Trait>();
        var held = Held(g, guid).ToHashSet();
        return All.Where(t => !held.Contains(t.Asset)).ToList();
    }

    /// <summary>Give <paramref name="guid"/> the trait <paramref name="asset"/>: one new record at the end of their persisted effects.</summary>
    public static GvasFile Give(GvasFile g, string guid, string asset, CutInfo cut)
    {
        if (Find(asset) == null) throw new GvasException($"unknown bond trait {asset}");
        var arr = MedbayOps.EffectsNode(g, guid) ?? throw new GvasException("this operator has no character data to add a trait to");
        if (Held(g, guid).Contains(asset)) throw new GvasException("this operator already has that trait");
        var (raw, isTrait) = Template(g, guid, asset) ?? throw new GvasException("this operator has no effect record a trait can be copied from");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        int count = arr.Children?.Count ?? 0;
        g = g.ArrayAppendRaw(arr, raw);
        GvasNode Added() => MedbayOps.EffectsNode(g, guid)!.Children![count];
        if (!isTrait)
        {
            // a stat or progression record: a trait carries no modifiers, no captured attributes for them, and one stack
            GvasNode? List(string name) => name.Contains('/')
                ? g.Child(g.Child(Added(), name.Split('/')[0]), name.Split('/')[1]) : g.Child(Added(), name);
            foreach (var list in new[] { "Modifiers", "ModifiedAttributes", "CapturedRelevantAttributes/SourceAttributes", "CapturedRelevantAttributes/TargetAttributes" })
                while (List(list) is { Children.Count: > 0 } items) g = g.ArrayRemoveElement(items, items.Children!.Count - 1);
            g.Set(g.Child(Added(), "StackCount")!, 1);
        }
        var def = g.Child(Added(), "Def") ?? throw new GvasException("the copied record has no effect class");
        if (g.GetStr(def) != DefPath(asset)) g = g.SetString(def, DefPath(asset));
        cut.TraitsGiven.Add((guid, asset));
        return g;
    }

    /// <summary>How many times <paramref name="guid"/> holds <paramref name="asset"/> (the verifier wants exactly one after a give).</summary>
    public static int Count(GvasFile g, string guid, string asset) => Held(g, guid).Count(a => a == asset);
}
