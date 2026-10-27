using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Which item sits in which of an operator's gear slots. Two saved pieces say it, and the game keeps them in step:
/// <list type="bullet">
/// <item>StrategyData.EquippedItemData[operator].EquipmentSlots[type].Slots: one entry per slot, the item's GUID as four ints (all zero = empty);</item>
/// <item>StrategyData.InventoryItemData[item].EquippedCharacterIDs: a set of the operators holding the item, saved as an int32 count of
/// removed entries (0), an int32 count, then 16 bytes per operator (the same four words <see cref="GvasExtensions.GuidHex"/> reads). Empty is 8 bytes.</item>
/// </list>
/// An item is held by at most one operator in one slot. <see cref="Equip"/> changes both pieces together; <see cref="Consistent"/> says whether they agree.
/// </summary>
public static class LoadoutOps
{
    public const string Zero = "00000000000000000000000000000000";

    /// <summary>One gear slot of an operator: the item tag type, its place in the slot list, and the item GUID in it (null when empty).</summary>
    public sealed record Slot(string Type, int Index, string? Item);

    /// <summary>An inventory entry that can go in a slot: its GUID, asset name, the item type tag it fits, and the count the save holds.</summary>
    public sealed record Item(string Guid, string Asset, string Type, long Count);

    /// <summary>"utility" or "mod" to the item type tag, and back.</summary>
    public static string TypeTag(string type) => GearOps.TypeTag(type);
    public static string TypeName(string tag) => tag == GearOps.Modification ? "mod" : "utility";

    static GvasNode? InventoryMap(GvasFile g) => g.Child(g.StrategyDataOrNull(), "InventoryItemData");

    static IEnumerable<(string Guid, GvasNode Value)> InventoryEntries(GvasFile g)
    {
        foreach (var e in InventoryMap(g)?.Children ?? new List<GvasNode>())
        {
            var v = e.Children is { Count: > 1 } ch ? ch[1] : g.Child(e, "value");
            if (v == null || e.Children == null) continue;
            yield return (ArmoryOps.StructGuid(g, e.Children[0]), v);
        }
    }

    static string? TagOfAsset(string path) =>
        path.Contains("/UtilityItems/", StringComparison.Ordinal) ? GearOps.Utility
        : path.Contains("/Modifications/", StringComparison.Ordinal) ? GearOps.Modification : null;

    /// <summary>Every inventory entry that fits a gear slot (a utility item or a weapon mod).</summary>
    public static List<Item> Items(GvasFile g)
    {
        var @out = new List<Item>();
        foreach (var (guid, v) in InventoryEntries(g))
        {
            if (g.Child(v, "ItemAsset") is not { } a || g.Child(v, "ItemCount") is not { } c) continue;
            var path = g.GetStr(a);
            if (TagOfAsset(path) is { } tag) @out.Add(new Item(guid, Naming.AssetName(path), tag, g.GetInt(c)));
        }
        return @out;
    }

    static string StructGuid(GvasFile g, GvasNode s) => ArmoryOps.StructGuid(g, s);

    /// <summary>The slots of one type the operator has, in order. Empty when the operator has no record of that type.</summary>
    public static List<Slot> Slots(GvasFile g, string guid, string tag)
    {
        var node = GearOps.SlotsNode(g, guid, tag);
        var list = new List<Slot>();
        if (node?.Children == null) return list;
        for (int i = 0; i < node.Children.Count; i++)
        {
            var id = StructGuid(g, node.Children[i]);
            list.Add(new Slot(tag, i, id == Zero ? null : id));
        }
        return list;
    }

    /// <summary>Every (operator, type, slot index, item) with an item in it, over the whole save.</summary>
    static List<(string Operator, string Tag, int Index, string Item)> Held(GvasFile g)
    {
        var @out = new List<(string, string, int, string)>();
        foreach (var rec in GearOps.Records(g))
        {
            var op = g.GuidHex(rec.Children![0]);
            foreach (var t in g.Child(rec.Children[1], "EquipmentSlots")?.Children ?? new List<GvasNode>())
            {
                if (g.Child(t.Children![0], "TagName") is not { } tn) continue;
                var tag = g.GetStr(tn);
                var slots = g.Child(t.Children[1], "Slots")?.Children ?? new List<GvasNode>();
                for (int i = 0; i < slots.Count; i++)
                    if (StructGuid(g, slots[i]) is var id && id != Zero) @out.Add((op, tag, i, id));
            }
        }
        return @out;
    }

    /// <summary>The operators an item's equipped set names, as GUID hex.</summary>
    public static List<string> Holders(GvasFile g, string item)
    {
        var @out = new List<string>();
        foreach (var (guid, v) in InventoryEntries(g))
        {
            if (guid != item || g.Child(v, "EquippedCharacterIDs") is not { } eq) continue;
            var d = g.Data;
            if (eq.Size < 8) return @out;
            int n = BitConverter.ToInt32(d, eq.ValueOffset + 4);
            for (int i = 0; i < n && eq.ValueOffset + 8 + 16 * (i + 1) <= eq.End; i++)
                @out.Add(string.Concat(Enumerable.Range(0, 4).Select(w => BitConverter.ToUInt32(d, eq.ValueOffset + 8 + 16 * i + 4 * w).ToString("X8"))));
        }
        return @out;
    }

    /// <summary>The operator holding <paramref name="item"/> according to the slots, if any.</summary>
    public static (string Operator, string Tag, int Index)? HeldBy(GvasFile g, string item) =>
        Held(g).Where(h => h.Item == item).Select(h => ((string Operator, string Tag, int Index)?)(h.Operator, h.Tag, h.Index)).FirstOrDefault();

    /// <summary>The 16 bytes the equipped set stores for an operator.</summary>
    static byte[] OperatorBytes(string guid)
    {
        var raw = new byte[16];
        for (int i = 0; i < 4; i++) BitConverter.GetBytes(Convert.ToUInt32(guid.Substring(i * 8, 8), 16)).CopyTo(raw, i * 4);
        return raw;
    }

    static byte[] SetBytes(IReadOnlyList<string> operators)
    {
        var b = new byte[8 + 16 * operators.Count];
        BitConverter.GetBytes(operators.Count).CopyTo(b, 4);
        for (int i = 0; i < operators.Count; i++) OperatorBytes(operators[i]).CopyTo(b, 8 + 16 * i);
        return b;
    }

    static GvasFile SetHolders(GvasFile g, string item, IReadOnlyList<string> operators)
    {
        var eq = InventoryEntries(g).Where(e => e.Guid == item).Select(e => g.Child(e.Value, "EquippedCharacterIDs")).FirstOrDefault()
                 ?? throw new GvasException("the item has no equipped set");
        var want = SetBytes(operators);
        if (g.Data.AsSpan(eq.ValueOffset, eq.Size).SequenceEqual(want)) return g;
        return g.ReplaceValue(eq, want);
    }

    static GvasNode? InventoryNode(GvasFile g, string item) =>
        InventoryEntries(g).Where(e => e.Guid == item).Select(e => g.Child(e.Value, "EquippedCharacterIDs")).FirstOrDefault();

    static void SetSlot(GvasFile g, GvasNode slot, string? item)
    {
        var parts = item == null ? new uint[4] : Enumerable.Range(0, 4).Select(i => Convert.ToUInt32(item.Substring(i * 8, 8), 16)).ToArray();
        string[] names = { "A", "B", "C", "D" };
        for (int i = 0; i < 4; i++) g.Set(g.Child(slot, names[i]) ?? throw new GvasException("unexpected slot layout"), unchecked((int)parts[i]));
    }

    static IEnumerable<GvasNode> SlotNodes(GvasFile g, string op, string tag, int index)
    {
        var node = GearOps.SlotsNode(g, op, tag);
        if (node?.Children != null && index >= 0 && index < node.Children.Count) yield return node.Children[index];
    }

    /// <summary>
    /// Put <paramref name="item"/> (an inventory entry's GUID; null empties the slot) into slot <paramref name="index"/> of the operator's
    /// <paramref name="tag"/> gear. An item another slot holds is taken out of that slot first. Both the slots and the items' equipped sets are
    /// written, and <paramref name="cut"/> records the paths that differ. Returns the new file.
    /// </summary>
    public static GvasFile Equip(GvasFile g, string op, string tag, int index, string? item, CutInfo cut)
    {
        if (GearOps.SlotsNode(g, op, tag) is not { Children: { } kids }) throw new GvasException("this operator has no gear slots of that type");
        if (index < 0 || index >= kids.Count) throw new GvasException($"slot {index + 1} does not exist ({kids.Count} slots)");
        var before = Held(g);
        if (item != null)
        {
            var it = Items(g).FirstOrDefault(x => x.Guid == item) ?? throw new GvasException("that item is not in the inventory");
            if (it.Type != tag) throw new GvasException($"that item does not fit a {TypeName(tag)} slot");
            if (it.Count < 1) throw new GvasException("the squad has none of that item");
        }
        var old = before.FirstOrDefault(h => h.Operator == op && h.Tag == tag && h.Index == index).Item;
        if (old == item) return g;

        // the items whose equipped sets change: the one leaving this slot and the one coming in
        var affected = new HashSet<string>();
        if (old != null) affected.Add(old);
        if (item != null) affected.Add(item);

        // take the item out of wherever it is held (another slot of this operator, or another operator)
        var cleared = new List<(string Op, string Tag, int Index)>();
        if (item != null) foreach (var h in before.Where(h => h.Item == item && !(h.Operator == op && h.Tag == tag && h.Index == index))) cleared.Add((h.Operator, h.Tag, h.Index));

        // the new holders of each affected item, from the slots as they will be
        var after = before.Where(h => !cleared.Contains((h.Operator, h.Tag, h.Index))).Where(h => !(h.Operator == op && h.Tag == tag && h.Index == index)).ToList();
        if (item != null) after.Add((op, tag, index, item));
        var holders = affected.ToDictionary(a => a, a => after.Where(h => h.Item == a).Select(h => h.Operator).Distinct().ToList());

        foreach (var a in affected)
        {
            var node = InventoryNode(g, a) ?? throw new GvasException("the item has no equipped set");
            cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(node)));
            g = SetHolders(g, a, holders[a]);
        }
        foreach (var (o, t, i) in cleared)
            foreach (var s in SlotNodes(g, o, t, i)) { cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(s))); SetSlot(g, s, null); }
        foreach (var s in SlotNodes(g, op, tag, index)) { cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(s))); SetSlot(g, s, item); }
        cut.Equipped.Add((op, tag, index, item));
        return g;
    }

    /// <summary>Whether the slot holds <paramref name="item"/> (null = empty) and the item's equipped set names the operator.</summary>
    public static bool Holds(GvasFile g, string op, string tag, int index, string? item)
    {
        var slots = Slots(g, op, tag);
        if (index >= slots.Count || slots[index].Item != item) return false;
        return item == null || Holders(g, item).SequenceEqual(new[] { op });
    }

    /// <summary>Whether, for the given items, every set names exactly the operators whose slots hold the item.</summary>
    public static bool Consistent(GvasFile g, IEnumerable<string> items)
    {
        var held = Held(g);
        foreach (var item in items.Distinct())
        {
            var want = held.Where(h => h.Item == item).Select(h => h.Operator).Distinct().OrderBy(x => x, StringComparer.Ordinal);
            if (!Holders(g, item).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(want)) return false;
        }
        return true;
    }

    /// <summary>Every item that is held in a slot or named in a set, so a check can cover them all.</summary>
    public static List<string> AllItems(GvasFile g) =>
        Held(g).Select(h => h.Item).Concat(InventoryEntries(g).Where(e => Holders(g, e.Guid).Count > 0).Select(e => e.Guid)).Distinct().ToList();
}
