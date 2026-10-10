using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Coil crisis upgrades. When a Crisis mission or operation expires or fails, the game makes that enemy upgrade permanent by
/// switching the crisis's state fact tag to <c>.Selected</c> (a crisis starts as <c>.Available</c>; winning it makes it
/// <c>.Prevented</c>). The tags live in StrategyData.FactTags, a native array: int32 count, then one FString per tag.
/// Taking an upgrade away renames <c>&lt;crisis&gt;.Selected</c> to one of the two other states the game itself uses.
/// </summary>
public static class CoilOps
{
    public const string Prefix = "BitReactor.Design.Crisis.";
    public static readonly string[] States = { "Available", "Prevented" };
    public static readonly Regex Selected = new(@"^BitReactor\.Design\.Crisis\.(Major|Minor)\.([A-Za-z0-9]+_[A-Z])\.Selected$", RegexOptions.Compiled);
    public static readonly Regex IdRx = new(@"^(Major|Minor)\.[A-Za-z0-9]+_[A-Z]$", RegexOptions.Compiled);

    public sealed record ActiveUpgrade(string Id, string Tier, string Unit, string Variant);

    public static GvasNode? FactTagsNode(GvasFile g) => g.Child(g.StrategyDataOrNull(), "FactTags");

    /// <summary>The FactTags array as text. Throws if the bytes are not exactly count + FStrings.</summary>
    public static List<string> ReadTags(GvasFile g, GvasNode node)
    {
        var d = g.Data; int pos = node.ValueOffset;
        if (pos + 4 > d.Length) throw new GvasException("unexpected FactTags layout");
        int n = BitConverter.ToInt32(d, pos);
        pos += 4;
        var @out = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (pos + 4 > d.Length) throw new GvasException("unexpected FactTags layout");
            int ln = BitConverter.ToInt32(d, pos);
            if (ln <= 0 || pos + 4 + ln > node.End || d[pos + 3 + ln] != 0) throw new GvasException("unexpected FactTags layout");
            @out.Add(Encoding.ASCII.GetString(d, pos + 4, ln - 1));
            pos += 4 + ln;
        }
        if (pos != node.End) throw new GvasException("unexpected FactTags layout");
        return @out;
    }

    public static List<string> Tags(GvasFile g)
    {
        var node = FactTagsNode(g);
        return node != null ? ReadTags(g, node) : new List<string>();
    }

    /// <summary>Upgrades the Coil currently hold.</summary>
    public static List<ActiveUpgrade> Active(GvasFile g)
    {
        var @out = new List<ActiveUpgrade>();
        foreach (var t in Tags(g))
        {
            var m = Selected.Match(t);
            if (!m.Success) continue;
            var body = m.Groups[2].Value;
            int u = body.LastIndexOf('_');
            @out.Add(new ActiveUpgrade($"{m.Groups[1].Value}.{body}", m.Groups[1].Value, body[..u], body[(u + 1)..]));
        }
        return @out;
    }

    /// <summary>Take upgrades away: <paramref name="changes"/> maps an upgrade id ("Major.Striker_A") to its new state.</summary>
    public static GvasFile Remove(GvasFile g, IReadOnlyDictionary<string, string> changes, CutInfo cut, ISet<string> touched)
    {
        var node = FactTagsNode(g) ?? throw new GvasException("fact tags not found");
        if (changes.Count == 0 || changes.Values.Any(s => !States.Contains(s))) throw new GvasException("unknown Coil upgrade state");
        var old = ReadTags(g, node);
        var have = new HashSet<string>(old);
        var target = changes.ToDictionary(kv => $"{Prefix}{kv.Key}.Selected", kv => kv.Value);
        if (!target.Keys.All(have.Contains)) throw new GvasException("that Coil upgrade is not active");
        var neu = new List<string>();
        foreach (var t in old)
        {
            if (target.TryGetValue(t, out var state))
            {
                var moved = t[..^"Selected".Length] + state;
                if (have.Contains(moved)) continue;                // never write a duplicate tag
                neu.Add(moved);
            }
            else neu.Add(t);
        }
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(neu.Count));
        foreach (var t in neu)
        {
            ms.Write(BitConverter.GetBytes(t.Length + 1));
            ms.Write(Encoding.ASCII.GetBytes(t));
            ms.WriteByte(0);
        }
        touched.Add(PathKey.Of(GvasFile.PathOf(node)));
        cut.FactTags = neu;
        return g.ReplaceValue(node, ms.ToArray());
    }
}
