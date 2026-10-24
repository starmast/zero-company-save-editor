using System.Buffers.Binary;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// The influence rewards of a galaxy zone. Each zone in StrategyData.AvailableRegions keeps its influence, the reward index and a
/// <c>ClaimedRewardTiers</c> set: the tiers (0, 1, 2 ...) whose rewards the player has claimed on the Holotable, which are what the game
/// ticks. The game lets a tier be claimed while the zone's influence is high enough and the tier is not in that set, so taking a tier out
/// makes its reward claimable again (Zone Jenth's Hutt Roulette Prize, for example). The set is stored as int32 removed (0), int32 count,
/// then one int32 per tier; the editor's parser leaves it as raw bytes, so it is read and rewritten here.
/// </summary>
public static class ZoneRewardOps
{
    public const int MaxTier = 9;

    /// <summary>The name the Holotable gives each zone, for saves opened without extracted game data.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["BitReactor.MapRegions.CoreWest"] = "Zone Aurek", ["BitReactor.MapRegions.CoreSouth"] = "Zone Besh",
        ["BitReactor.MapRegions.CoreEast"] = "Zone Cresh", ["BitReactor.MapRegions.MidWest"] = "Zone Dorn",
        ["BitReactor.MapRegions.MidSouthwest"] = "Zone Esk", ["BitReactor.MapRegions.MidSoutheast"] = "Zone Forn",
        ["BitReactor.MapRegions.MidEast"] = "Zone Grek", ["BitReactor.MapRegions.OuterWest"] = "Zone Herf",
        ["BitReactor.MapRegions.OuterSouthwest"] = "Zone Isk", ["BitReactor.MapRegions.OuterSouth"] = "Zone Jenth",
        ["BitReactor.MapRegions.OuterSouthEast"] = "Zone Krill", ["BitReactor.MapRegions.OuterEast"] = "Zone Leth",
    };

    static GvasNode? Set(GvasFile g, string zone)
    {
        foreach (var e in g.Child(g.StrategyDataOrNull(), "AvailableRegions")?.Children ?? new List<GvasNode>())
            if (e.Children is { Count: 2 } kv && g.Child(kv[0], "TagName") is { } t && g.GetStr(t) == zone)
                return g.Child(kv[1], "ClaimedRewardTiers");
        return null;
    }

    /// <summary>The tiers claimed in <paramref name="zone"/> (a tag such as <c>BitReactor.MapRegions.OuterSouth</c>), in save order; null when the save has no such set or it is not in the expected layout.</summary>
    public static List<int>? Claimed(GvasFile g, string zone)
    {
        var n = Set(g, zone);
        if (n == null || n.Children != null || n.Size < 8) return null;
        var d = g.Data.AsSpan(n.ValueOffset, n.Size);
        int removed = BinaryPrimitives.ReadInt32LittleEndian(d), count = BinaryPrimitives.ReadInt32LittleEndian(d[4..]);
        if (removed != 0 || count < 0 || n.Size != 8 + 4 * count) return null;
        var @out = new List<int>();
        for (int i = 0; i < count; i++) @out.Add(BinaryPrimitives.ReadInt32LittleEndian(d[(8 + 4 * i)..]));
        return @out;
    }

    /// <summary>Make <paramref name="tiers"/> the claimed tiers of <paramref name="zone"/>. The expected end state goes into <paramref name="cut"/>.</summary>
    public static GvasFile SetClaimed(GvasFile g, string zone, IReadOnlyCollection<int> tiers, CutInfo cut)
    {
        var n = Set(g, zone) ?? throw new GvasException("this zone has no claimed reward list");
        if (Claimed(g, zone) == null) throw new GvasException("this zone's claimed reward list is not in the expected layout");
        if (tiers.Any(t => t < 0 || t > MaxTier) || tiers.Distinct().Count() != tiers.Count) throw new GvasException($"reward tiers must be distinct numbers from 0 to {MaxTier}");
        var sorted = tiers.OrderBy(t => t).ToList();
        var bytes = new byte[8 + 4 * sorted.Count];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), sorted.Count);
        for (int i = 0; i < sorted.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 + 4 * i), sorted[i]);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));
        cut.ZoneClaims[zone] = sorted;
        return g.ReplaceValue(n, bytes);
    }
}
