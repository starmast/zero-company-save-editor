using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Completing an operator's focus-tree tier data. Every ability in an operator's saved <c>LeveledAbilities</c> carries one
/// record per tier (T1..T6). Those lists are static game data: every operator who holds the same ability has the identical
/// list. The tutorial operator (Aurelio) only has the first tier of each ability, so the game cannot draw the rest of his
/// tree. <see cref="Complete"/> appends the missing tier records, copied byte-for-byte from another operator who holds the
/// same ability, but only where the operator's existing records are exactly the start of that list.
/// </summary>
public static class FocusOps
{
    static string? Tag(GvasFile g, GvasNode entry)
    {
        var t = g.Child(entry, "BaseAbilityTag");
        var n = t == null ? null : g.Child(t, "TagName");
        return n != null ? g.GetStr(n) : null;
    }

    /// <summary>(ability tag, its tier-record array) for every ability of one character.</summary>
    static IEnumerable<(string Tag, GvasNode List)> Lists(GvasFile g, GvasNode wrapper)
    {
        foreach (var n in g.Walk(new[] { wrapper }))
        {
            if (n.Name != "LeveledAbilities") continue;
            foreach (var e in n.Children ?? new List<GvasNode>())
            {
                var lst = g.Child(e, "LeveledAbilityList_Struct");
                var tag = Tag(g, e);
                if (!string.IsNullOrEmpty(tag) && lst?.Children != null) yield return (tag, lst);
            }
        }
    }

    static List<byte[]> Raw(GvasFile g, GvasNode lst) =>
        (lst.Children ?? new List<GvasNode>()).Select(e => g.Slice(e.Start, e.End)).ToList();

    static Dictionary<string, List<byte[]>> Donors(GvasFile g, string skip)
    {
        var best = new Dictionary<string, List<byte[]>>();
        foreach (var (gid, w) in MedbayOps.Characters(g))
        {
            if (gid == skip) continue;
            foreach (var (tag, lst) in Lists(g, w))
            {
                var recs = Raw(g, lst);
                if (recs.Count > (best.TryGetValue(tag, out var cur) ? cur.Count : 0)) best[tag] = recs;
            }
        }
        return best;
    }

    static bool SameRecords(List<byte[]> a, List<byte[]> prefixOf, int n)
    {
        for (int i = 0; i < n; i++) if (!a[i].AsSpan().SequenceEqual(prefixOf[i])) return false;
        return true;
    }

    static IEnumerable<(string Tag, GvasNode List, List<byte[]> Have, List<byte[]> Donor)> Gaps(
        GvasFile g, string guid, Dictionary<string, List<byte[]>> donors)
    {
        if (!MedbayOps.Characters(g).TryGetValue(guid, out var w)) yield break;
        foreach (var (tag, lst) in Lists(g, w))
        {
            var have = Raw(g, lst);
            if (have.Count > 0 && donors.TryGetValue(tag, out var d) && d.Count > have.Count && SameRecords(d, have, have.Count))
                yield return (tag, lst, have, d);
        }
    }

    /// <summary>{operator guid: [ability tags whose tiers are missing]} for operators that <see cref="Complete"/> can fix.</summary>
    public static Dictionary<string, List<string>> Incomplete(GvasFile g)
    {
        var @out = new Dictionary<string, List<string>>();
        foreach (var gid in MedbayOps.Characters(g).Keys)
        {
            var tags = Gaps(g, gid, Donors(g, gid)).Select(x => x.Tag).ToList();
            if (tags.Count > 0) @out[gid] = tags;
        }
        return @out;
    }

    /// <summary>Append the missing tier records of one operator.</summary>
    public static GvasFile Complete(GvasFile g, string guid, CutInfo cut)
    {
        var expected = new Dictionary<string, List<byte[]>>();
        while (true)
        {
            var gap = Gaps(g, guid, Donors(g, guid)).Cast<(string, GvasNode, List<byte[]>, List<byte[]>)?>().FirstOrDefault();
            if (gap == null) break;
            var (tag, lst, have, donor) = gap.Value;
            cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(lst)));
            expected[tag] = donor;
            var raw = donor.Skip(have.Count).SelectMany(x => x).ToArray();
            g = g.ArrayAppendRaw(lst, raw, donor.Count - have.Count);
        }
        if (expected.Count == 0) throw new GvasException("nothing to complete");
        cut.Focus.Add((guid, expected));
        return g;
    }

    public static Dictionary<string, List<byte[]>> Records(GvasFile g, string guid)
    {
        var @out = new Dictionary<string, List<byte[]>>();
        if (!MedbayOps.Characters(g).TryGetValue(guid, out var w)) return @out;
        foreach (var (tag, lst) in Lists(g, w)) @out[tag] = Raw(g, lst);
        return @out;
    }
}
