using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Cross-training stat upgrades (ranged damage, crit chance, defense and the rest). Each is a gameplay effect under
/// BondData/CrossTrainingStatRewards, and an operator who has trained it holds one record in their character data's
/// GameplayEffectsToPersist, with a stack count and the stat value per stack in its modifiers. <see cref="Give"/> appends such a
/// record for an operator who has never trained the stat: a copy of the same stat's record from another operator when the save has
/// one (modifiers and all). Otherwise it copies any other plain permanent record, switches its effect class and gives it the stat's
/// modifier entries built from the game data: one per modifier the effect defines, each holding the value per stack.
/// </summary>
public static class StatOps
{
    public const string Folder = "/Game/Game/GameData/BondData/CrossTrainingStatRewards/";

    /// <summary>One stat upgrade: its effect asset and a readable name.</summary>
    public sealed record Stat(string Asset, string Name);

    static Stat S(string asset) => new(asset, Naming.Pretty(asset["GE_CrossTraining_".Length..]));

    /// <summary>Every cross-training stat the game defines (19).</summary>
    public static readonly IReadOnlyList<Stat> All = new[]
    {
        "AssistDamage", "CritChance", "CritDamage", "Defense", "DodgeChance", "GainBond_AFriendlyGame", "GainBond_ConflictResolution",
        "GainFocus", "GrenadeCritChance", "GrenadeDamage", "Health", "Large_DodgeChance", "Large_Speed", "MeleeDamage", "OverwatchDamage",
        "RangedDamage", "ReduceRangePenalties", "Speed", "UtilityDamage",
    }.Select(n => S("GE_CrossTraining_" + n)).ToArray();

    public static Stat? Find(string asset) => All.FirstOrDefault(s => s.Asset == asset);

    /// <summary>The effect class path the save stores for a stat.</summary>
    public static string DefPath(string asset) => $"{Folder}{asset}.Default__{asset}_C";

    static string? DefOf(GvasFile g, GvasNode el) => g.Child(el, "Def") is { } d ? g.GetStr(d) : null;

    static bool IsStatRecord(GvasFile g, GvasNode el) => DefOf(g, el) is { } d && d.StartsWith(Folder, StringComparison.Ordinal);

    static List<GvasNode> Records(GvasFile g, string gid) => MedbayOps.EffectsNode(g, gid)?.Children ?? new List<GvasNode>();

    /// <summary>The stats (effect asset names) an operator has.</summary>
    public static List<string> Held(GvasFile g, string guid) =>
        Records(g, guid).Where(el => IsStatRecord(g, el)).Select(el => MedbayOps.EffectName(g, el)).ToList();

    /// <summary>
    /// A record to copy for <paramref name="guid"/>, and whether it is already the stat wanted: the same stat on any operator, else
    /// any other stat record in the save, else the operator's own plain permanent record. Null when there is none.
    /// </summary>
    static (byte[] Raw, bool Same)? Template(GvasFile g, string guid, string asset)
    {
        var all = MedbayOps.Characters(g).Keys.SelectMany(gid => Records(g, gid)).ToList();
        var same = all.FirstOrDefault(e => IsStatRecord(g, e) && MedbayOps.EffectName(g, e) == asset);
        if (same != null) return (g.Slice(same.Start, same.End), true);
        var other = Records(g, guid).FirstOrDefault(e => IsStatRecord(g, e)) ?? all.FirstOrDefault(e => IsStatRecord(g, e));
        if (other != null) return (g.Slice(other.Start, other.End), false);
        var plain = Records(g, guid).FirstOrDefault(e => TraitOps.IsPlainPermanent(g, e));
        return plain == null ? null : (g.Slice(plain.Start, plain.End), false);
    }

    /// <summary>Whether a stat can be given to <paramref name="guid"/>: the save has a record to copy.</summary>
    public static bool CanGive(GvasFile g, string guid) =>
        MedbayOps.Characters(g).Keys.Any(gid => Records(g, gid).Any(el => IsStatRecord(g, el))) || Records(g, guid).Any(el => TraitOps.IsPlainPermanent(g, el));

    /// <summary>The stats <paramref name="guid"/> could be given: every stat they do not have, when there is a record to copy.</summary>
    public static List<Stat> Givable(GvasFile g, string guid)
    {
        if (MedbayOps.EffectsNode(g, guid) == null || !CanGive(g, guid)) return new List<Stat>();
        var held = Held(g, guid).ToHashSet();
        return All.Where(s => !held.Contains(s.Asset)).ToList();
    }

    /// <summary>The value per stack of each modifier of a stat, from the game data (empty when the game data is not extracted).</summary>
    public static List<double> Magnitudes(GameDatabase? db, string asset) =>
        db != null && db.Effects.TryGetValue(asset, out var e) ? e.Modifiers.Select(m => m.Value ?? 0).ToList() : new List<double>();

    /// <summary>One modifier entry (a struct holding only EvaluatedMagnitude) taken from any effect record in the save, or null when none has one.</summary>
    static byte[]? ModifierElement(GvasFile g) =>
        MedbayOps.Characters(g).Keys.SelectMany(gid => Records(g, gid))
            .Select(el => g.Child(el, "Modifiers")?.Children?.FirstOrDefault()).FirstOrDefault(m => m != null) is { } m ? g.Slice(m.Start, m.End) : null;

    /// <summary>
    /// Give <paramref name="guid"/> the stat <paramref name="asset"/> at one stack: one new record at the end of their persisted effects.
    /// With the game data (<paramref name="db"/>) a record made from another stat's gets this stat's modifier entries and values.
    /// </summary>
    public static GvasFile Give(GvasFile g, string guid, string asset, CutInfo cut, GameDatabase? db = null)
    {
        if (Find(asset) == null) throw new GvasException($"unknown stat upgrade {asset}");
        var arr = MedbayOps.EffectsNode(g, guid) ?? throw new GvasException("this operator has no character data to add a stat to");
        if (Held(g, guid).Contains(asset)) throw new GvasException("this operator already has that stat");
        var (raw, same) = Template(g, guid, asset) ?? throw new GvasException("this operator has no effect record a stat can be copied from");
        var magnitudes = same ? new List<double>() : Magnitudes(db, asset);
        var modifier = magnitudes.Count > 0 ? ModifierElement(g) : null;
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        int count = arr.Children?.Count ?? 0;
        g = g.ArrayAppendRaw(arr, raw);
        GvasNode Added() => MedbayOps.EffectsNode(g, guid)!.Children![count];
        if (!same)
        {
            GvasNode? List(string name) => name.Contains('/')
                ? g.Child(g.Child(Added(), name.Split('/')[0]), name.Split('/')[1]) : g.Child(Added(), name);
            foreach (var list in new[] { "Modifiers", "ModifiedAttributes", "CapturedRelevantAttributes/SourceAttributes", "CapturedRelevantAttributes/TargetAttributes" })
                while (List(list) is { Children.Count: > 0 } items) g = g.ArrayRemoveElement(items, items.Children!.Count - 1);
            if (modifier != null && List("Modifiers") is { } mods)
            {
                g = g.ArrayAppendRaw(mods, Enumerable.Range(0, magnitudes.Count).SelectMany(_ => modifier).ToArray(), magnitudes.Count);
                for (int i = 0; i < magnitudes.Count; i++)
                    g.Set(g.Child(List("Modifiers")!.Children![i], "EvaluatedMagnitude") ?? throw new GvasException("a modifier entry has no magnitude"), magnitudes[i]);
            }
        }
        g.Set(g.Child(Added(), "StackCount")!, 1);
        var def = g.Child(Added(), "Def") ?? throw new GvasException("the copied record has no effect class");
        if (g.GetStr(def) != DefPath(asset)) g = g.SetString(def, DefPath(asset));
        cut.StatsGiven.Add((guid, asset));
        return g;
    }

    /// <summary>How many times <paramref name="guid"/> holds <paramref name="asset"/> (the verifier wants exactly one after a give).</summary>
    public static int Count(GvasFile g, string guid, string asset) => Held(g, guid).Count(a => a == asset);
}
