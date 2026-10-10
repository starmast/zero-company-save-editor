using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// StrategyData.Roster is an array of operator GUIDs (16 bytes each) in the order the Personnel strip shows them (the game
/// appends new recruits). Reordering permutes those fixed-size entries in place: the file size, every size field and the
/// metadata stay exactly as they were.
/// </summary>
public static class RosterOps
{
    public static GvasNode? RosterNode(GvasFile g) => g.Child(g.StrategyDataOrNull(), "Roster");

    public static List<string> Order(GvasFile g)
    {
        var arr = RosterNode(g);
        return (arr?.Children ?? new List<GvasNode>()).Select(e => g.GuidHex(e)).ToList();
    }

    /// <summary>Put the roster in <paramref name="newOrder"/> (the same operators, any order).</summary>
    public static GvasFile Reorder(GvasFile g, IReadOnlyList<string> newOrder, CutInfo cut, ISet<string> touched)
    {
        var arr = RosterNode(g);
        if (arr?.Children == null || arr.Children.Count == 0) throw new GvasException("roster not found");
        var cur = Order(g);
        if (newOrder.Distinct().Count() != newOrder.Count
            || !newOrder.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(cur.OrderBy(x => x, StringComparer.Ordinal)))
            throw new GvasException("the roster changed; re-open the save");
        if (arr.Children.Any(e => e.End - e.Start != 16)) throw new GvasException("unexpected roster layout");
        var raw = new Dictionary<string, byte[]>();
        for (int i = 0; i < cur.Count; i++) raw[cur[i]] = g.Slice(arr.Children[i].Start, arr.Children[i].End);
        var buf = (byte[])g.Data.Clone();
        bool any = false;
        for (int i = 0; i < arr.Children.Count; i++)
        {
            if (cur[i] == newOrder[i]) continue;
            Buffer.BlockCopy(raw[newOrder[i]], 0, buf, arr.Children[i].Start, 16);
            touched.Add(PathKey.Of(GvasFile.PathOf(arr.Children[i])));
            any = true;
        }
        if (!any) throw new GvasException("the roster order is unchanged");
        cut.Roster = newOrder.ToList();
        return new GvasFile(buf);
    }
}
