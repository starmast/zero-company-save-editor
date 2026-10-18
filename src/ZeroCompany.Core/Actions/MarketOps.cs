using System.Text;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// The Black Market (the Armory's shop) restocks by rolling a number of items from each quality table:
/// StrategyData.ShopInventories[Armory].RestockTables maps a table tag (<c>br.ShopTable.Mods.Tier2</c>) to how many items it rolls.
/// The game defines three tiers for weapon mods and for utilities (Tier 1, 2 and 3: Basic, Superior and Exquisite), but a save only lists the
/// tiers the shop has used, so <see cref="Set"/> adds a missing table by copying a listed one and changing its tier digit.
/// </summary>
public static class MarketOps
{
    public static readonly string[] Kinds = { "mods", "utility" };
    public const int MinTier = 1, MaxTier = 3, MaxCount = 12;

    public static string Tag(string kind, int tier) =>
        $"br.ShopTable.{(kind == "mods" ? "Mods" : kind == "utility" ? "Utility" : throw new GvasException("kind must be mods or utility"))}.Tier{tier}";

    public static string Quality(int tier) => tier switch { 1 => "Basic", 2 => "Superior", 3 => "Exquisite", _ => $"tier {tier}" };

    static GvasNode? Tables(GvasFile g)
    {
        var sd = g.StrategyDataOrNull();
        foreach (var shop in (sd == null ? null : g.Child(sd, "ShopInventories"))?.Children ?? new List<GvasNode>())
            if (g.Child(shop.Children![0], "TagName") is { } t && g.GetStr(t).EndsWith(".Armory", StringComparison.Ordinal))
                return g.Child(shop.Children[1], "RestockTables");
        return null;
    }

    static string? TagOf(GvasFile g, GvasNode entry) => g.Child(entry.Children![0], "TagName") is { } t ? g.GetStr(t) : null;

    /// <summary>The count each table in the save rolls (tables the save does not list are absent). Null when the save has no Black Market.</summary>
    public static Dictionary<string, int>? Counts(GvasFile g)
    {
        var map = Tables(g);
        if (map == null) return null;
        var @out = new Dictionary<string, int>();
        foreach (var e in map.Children ?? new List<GvasNode>())
            if (TagOf(g, e) is { } tag) @out[tag] = (int)g.GetInt(e.Children![1]);
        return @out;
    }

    /// <summary>Make the table <paramref name="tag"/> roll <paramref name="count"/> items, adding it to the save if it is not listed.</summary>
    public static GvasFile Set(GvasFile g, string tag, int count, CutInfo cut)
    {
        var map = Tables(g) ?? throw new GvasException("this save has no Black Market to change");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(map)));
        var kids = map.Children ?? new List<GvasNode>();
        var hit = kids.FirstOrDefault(e => TagOf(g, e) == tag);
        if (hit == null)
        {
            // copy an entry of the same kind and rewrite its tier digit: the tags differ by one character, so the bytes keep their size
            var kind = tag[..tag.LastIndexOf('.')];
            var donor = kids.FirstOrDefault(e => TagOf(g, e) is { } t && t.StartsWith(kind + ".Tier", StringComparison.Ordinal) && t.Length == tag.Length)
                        ?? kids.FirstOrDefault(e => TagOf(g, e) is { } t && t.Length == tag.Length)
                        ?? throw new GvasException("no table of this save to copy the layout from");
            var raw = g.Slice(donor.Start, donor.End);
            var from = Encoding.ASCII.GetBytes(TagOf(g, donor)!); var to = Encoding.ASCII.GetBytes(tag);
            int at = IndexOf(raw, from);
            if (at < 0) throw new GvasException("could not find the table name in the entry to copy");
            to.CopyTo(raw, at);
            g = g.MapAppendRaw(map, raw);
            kids = Tables(g)!.Children!;
            hit = kids.First(e => TagOf(g, e) == tag);
        }
        g.Set(hit.Children![1], count);
        cut.MarketTables[tag] = count;
        return g;
    }

    static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    /// <summary>Whether every table in <paramref name="expected"/> is listed with exactly that count.</summary>
    public static bool Holds(GvasFile g, IReadOnlyDictionary<string, int> expected)
    {
        var have = Counts(g);
        return have != null && expected.All(kv => have.TryGetValue(kv.Key, out var n) && n == kv.Value);
    }
}
