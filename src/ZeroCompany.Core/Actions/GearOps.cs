using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// How many gear slots every operator has. Two saved pieces decide it, and both must agree:
/// <list type="bullet">
/// <item>StrategyData.EquippedItemData[operator].EquipmentSlots has, per item type, one entry per slot the Armory draws (all-zero ids are empty slots);</item>
/// <item>StrategyData.BonusEquipmentSlots has, per item type, the bonus on top of the one base slot, and the game only lets an item be equipped
/// into a slot when the operator's count (base + bonus) covers it.</item>
/// </list>
/// <see cref="SetSlotCount"/> raises both. The slot limit facts (MaximumModSlotCount and so on) do not matter.
/// </summary>
public static class GearOps
{
    public const string Modification = "BitReactor.ItemType.Modification", Utility = "BitReactor.ItemType.Utility";

    /// <summary>The slots every operator has before any bonus.</summary>
    public const int BaseSlots = 1;

    /// <summary>A weapon takes one mod per part (barrel, cell, scope, stock), so more than four mod slots would never be used.</summary>
    public const int MaxModSlots = 4, MaxUtilitySlots = 12;

    public static string TypeTag(string type) => type switch
    {
        "mod" => Modification,
        "utility" => Utility,
        _ => throw new GvasException("gear type must be mod or utility"),
    };

    internal static List<GvasNode> Records(GvasFile g)
    {
        var sd = g.StrategyDataOrNull();
        return (sd == null ? null : g.Child(sd, "EquippedItemData"))?.Children ?? new List<GvasNode>();
    }

    internal static GvasNode? SlotsNode(GvasFile g, string guid, string type)
    {
        foreach (var ent in Records(g))
        {
            if (g.GuidHex(ent.Children![0]) != guid) continue;
            foreach (var t in g.Child(ent.Children[1], "EquipmentSlots")?.Children ?? new List<GvasNode>())
                if (g.Child(t.Children![0], "TagName") is { } n && g.GetStr(n) == type) return g.Child(t.Children[1], "Slots");
        }
        return null;
    }

    static GvasNode? BonusNode(GvasFile g, string type)
    {
        var sd = g.StrategyDataOrNull();
        foreach (var e in (sd == null ? null : g.Child(sd, "BonusEquipmentSlots"))?.Children ?? new List<GvasNode>())
            if (g.Child(e.Children![0], "TagName") is { } t && g.GetStr(t) == type) return e.Children[1];
        return null;
    }

    /// <summary>Operators that have at least one slot of this type (an operator with none, such as a droid without utility slots, is left alone).</summary>
    public static List<string> Operators(GvasFile g, string type) =>
        Records(g).Select(e => g.GuidHex(e.Children![0])).Where(gid => SlotCount(g, gid, type) > 0).ToList();

    public static int SlotCount(GvasFile g, string guid, string type) => SlotsNode(g, guid, type)?.Children?.Count ?? 0;

    /// <summary>The slots the game gives everyone today (base plus bonus), or 0 when the save has no bonus record.</summary>
    public static int Current(GvasFile g, string type) => BonusNode(g, type) is { } b ? (int)g.GetInt(b) + BaseSlots : 0;

    /// <summary>Give every operator <paramref name="total"/> slots of <paramref name="type"/> (never fewer than they have).</summary>
    public static GvasFile SetSlotCount(GvasFile g, string type, int total, CutInfo cut)
    {
        if (BonusNode(g, type) == null) throw new GvasException("this save has no bonus slot record to raise");
        foreach (var guid in Operators(g, type))
        {
            var node = SlotsNode(g, guid, type)!;
            var kids = node.Children ?? new List<GvasNode>();
            int n = total - kids.Count;
            if (n <= 0 || kids.Count == 0) continue;
            var one = g.Slice(kids[^1].Start, kids[^1].End);            // any slot has the same layout: four ints, zeroed below
            cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(node)));
            g = g.ArrayAppendRaw(node, Enumerable.Range(0, n).SelectMany(_ => one).ToArray(), n);
            foreach (var slot in (SlotsNode(g, guid, type)!.Children ?? new List<GvasNode>()).Skip(total - n))
                foreach (var c in slot.Children ?? new List<GvasNode>())
                    if (GvasFile.Scalars.ContainsKey(c.TName)) g.Set(c, 0);
        }
        var bonus = BonusNode(g, type)!;
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(bonus.Parent)));
        if (g.GetInt(bonus) < total - BaseSlots) g.Set(bonus, total - BaseSlots);
        cut.GearSlots[type] = total;
        return g;
    }

    /// <summary>Whether everyone with a gear record of <paramref name="type"/> has at least <paramref name="total"/> slots and the bonus covers them.</summary>
    public static bool Holds(GvasFile g, string type, int total) =>
        BonusNode(g, type) is { } b && g.GetInt(b) >= total - BaseSlots
        && Operators(g, type).All(gid => SlotCount(g, gid, type) >= total);
}
