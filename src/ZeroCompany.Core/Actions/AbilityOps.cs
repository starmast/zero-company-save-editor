using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Giving an operator an ability they do not have (for example Hawks the Mandalorian jetpack, <c>br.AbilityID.Identity.CombatJump</c>).
/// An operator's abilities live in two places, both plain data:
/// <list type="bullet">
/// <item>their character data holds a <c>LeveledAbilities</c> array (per ability: its tag, category and one record per tier);</item>
/// <item>StrategyData.CharacterFocusData[guid].FocusPointAllocations holds the focus-tree state (level, focus spent).</item>
/// </list>
/// The tier records are static game data, so <see cref="Grant"/> copies the whole record, byte for byte, from another operator
/// in the same save who holds the ability, into the same kind of slot, and adds a focus allocation copied the same way.
/// </summary>
public static class AbilityOps
{
    /// <summary>One ability as another operator holds it: the bytes to copy and where they came from.</summary>
    public sealed record Donor(string Tag, string Slot, string FromGuid, byte[] Record, byte[] Allocation, int Tiers, string FirstAsset);

    static string? TagOf(GvasFile g, GvasNode entry)
    {
        var t = g.Child(entry, "BaseAbilityTag");
        var n = t == null ? null : g.Child(t, "TagName");
        return n != null ? g.GetStr(n) : null;
    }

    /// <summary>(ability tag, slot signature, the LeveledAbilities array, the element) for every ability record of one character.</summary>
    static IEnumerable<(string Tag, string Slot, GvasNode Array, GvasNode Element)> Records(GvasFile g, GvasNode wrapper)
    {
        int skip = GvasFile.PathOf(wrapper).Length;
        foreach (var n in g.Walk(new[] { wrapper }))
        {
            if (n.Name != "LeveledAbilities") continue;
            var slot = string.Join("/", GvasFile.PathOf(n).Skip(skip));
            foreach (var e in n.Children ?? new List<GvasNode>())
                if (TagOf(g, e) is { Length: > 0 } tag) yield return (tag, slot, n, e);
        }
    }

    static GvasNode? AllocationMap(GvasFile g, string guid)
    {
        var sd = g.StrategyDataOrNull();
        var map = sd == null ? null : g.Child(sd, "CharacterFocusData");
        foreach (var ent in map?.Children ?? new List<GvasNode>())
            if (g.GuidHex(ent.Children![0]) == guid) return g.Child(ent.Children[1], "FocusPointAllocations");
        return null;
    }

    static (int Index, GvasNode Entry)? Allocation(GvasFile g, string guid, string tag)
    {
        var kids = AllocationMap(g, guid)?.Children;
        if (kids == null) return null;
        for (int i = 0; i < kids.Count; i++)
            if (g.Child(kids[i].Children![0], "TagName") is { } t && g.GetStr(t) == tag) return (i, kids[i]);
        return null;
    }

    /// <summary>Tags of the abilities an operator already has.</summary>
    public static HashSet<string> Held(GvasFile g, string guid) =>
        MedbayOps.Characters(g).TryGetValue(guid, out var w) ? Records(g, w).Select(r => r.Tag).ToHashSet() : new HashSet<string>();

    /// <summary>
    /// Every ability some operator on the roster or in the Memorial holds and can pass on, by tag. Where several hold it the
    /// most complete record (most tiers) wins.
    /// </summary>
    public static Dictionary<string, Donor> Donors(GvasFile g)
    {
        var @out = new Dictionary<string, Donor>();
        foreach (var (gid, w) in MedbayOps.Characters(g))
        {
            if (AllocationMap(g, gid) == null) continue;                  // enemies and extras have no focus tree
            foreach (var (tag, slot, _, el) in Records(g, w))
            {
                var tiers = g.Child(el, "LeveledAbilityList_Struct")?.Children ?? new List<GvasNode>();
                if (@out.TryGetValue(tag, out var cur) && cur.Tiers >= tiers.Count) continue;
                if (Allocation(g, gid, tag) is not { } al) continue;       // an ability without a focus entry cannot be copied whole
                var first = tiers.Count > 0 && g.Child(tiers[0], "LeveledAbility") is { } a ? g.GetStr(a) : "";
                @out[tag] = new Donor(tag, slot, gid, g.Slice(el.Start, el.End), g.Slice(al.Entry.Start, al.Entry.End), tiers.Count, first);
            }
        }
        return @out;
    }

    /// <summary>The abilities <paramref name="guid"/> could be given: held by someone else, with a matching slot on this operator.</summary>
    public static List<Donor> Grantable(GvasFile g, string guid)
    {
        if (!MedbayOps.Characters(g).TryGetValue(guid, out var w)) return new List<Donor>();
        var held = new HashSet<string>(); var slots = new HashSet<string>();
        foreach (var r in Records(g, w)) { held.Add(r.Tag); slots.Add(r.Slot); }
        return Donors(g).Values.Where(d => !held.Contains(d.Tag) && slots.Contains(d.Slot)).OrderBy(d => d.Tag, StringComparer.Ordinal).ToList();
    }

    /// <summary>Give <paramref name="guid"/> the ability <paramref name="tag"/> at <paramref name="level"/> with <paramref name="spent"/> focus recorded against it.</summary>
    public static GvasFile Grant(GvasFile g, string guid, string tag, int level, int spent, CutInfo cut)
    {
        var d = Grantable(g, guid).FirstOrDefault(x => x.Tag == tag)
                ?? throw new GvasException(Donors(g).ContainsKey(tag) ? "this operator already has that ability or has no slot for it" : "no operator in this save has that ability to copy");
        var w = MedbayOps.Characters(g)[guid];
        var arr = Records(g, w).First(r => r.Slot == d.Slot).Array;
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        g = g.ArrayAppendRaw(arr, d.Record);

        var map = AllocationMap(g, guid) ?? throw new GvasException("this operator has no focus tree");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(map)));
        g = g.MapAppendRaw(map, d.Allocation);
        var entry = Allocation(g, guid, tag)!.Value.Entry;
        var val = entry.Children![1];
        if (g.Child(val, "Level") is not { } lv || g.Child(val, "AllocatedFocus") is not { } af) throw new GvasException("the copied focus entry has no level");
        g.Set(lv, level);
        g.Set(af, spent);
        cut.Granted.Add((guid, tag, d.Record));
        return g;
    }

    /// <summary>Whether <paramref name="tag"/> is held with exactly <paramref name="record"/> and has a focus entry.</summary>
    public static bool Has(GvasFile g, string guid, string tag, byte[] record)
    {
        if (!MedbayOps.Characters(g).TryGetValue(guid, out var w)) return false;
        return Allocation(g, guid, tag) != null
               && Records(g, w).Any(r => r.Tag == tag && g.Slice(r.Element.Start, r.Element.End).AsSpan().SequenceEqual(record));
    }
}
