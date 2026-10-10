using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Bringing a fallen operator back to the roster. A dead operator differs from a living one in a handful of places:
/// <list type="bullet">
/// <item>StrategyData.DeadCharacters lists them and StrategyData.CharacterIDsToDiedTurns has their death turn;</item>
/// <item>they are absent from StrategyData.Roster and StrategyData.CharacterIDsToRecruitedTurns;</item>
/// <item>their saved effects contain <c>GE_ApplyDead</c> (and usually <c>GE_Injured</c>).</item>
/// </list>
/// Reviving reverses those. Everything else (bonds, focus, equipment, memorial statistics) is left alone.
/// </summary>
public static class ReviveOps
{
    public static readonly string[] DeadEffects = { "GE_ApplyDead", "GE_Injured" };

    static GvasNode Node(GvasFile g, string name)
    {
        var sd = g.StrategyDataOrNull() ?? throw new GvasException("strategy data not found");
        return g.Child(sd, name) ?? throw new GvasException($"{name} not found");
    }

    public static List<string> DeadGuids(GvasFile g) =>
        (Node(g, "DeadCharacters").Children ?? new List<GvasNode>()).Select(e => g.GuidHex(e)).ToList();

    public static Dictionary<string, int> DiedMap(GvasFile g) => TurnMap(g, "CharacterIDsToDiedTurns");
    public static Dictionary<string, int> RecruitedMap(GvasFile g) => TurnMap(g, "CharacterIDsToRecruitedTurns");

    static Dictionary<string, int> TurnMap(GvasFile g, string name)
    {
        var @out = new Dictionary<string, int>();
        foreach (var e in Node(g, name).Children ?? new List<GvasNode>())
            @out[g.GuidHex(e.Children![0])] = (int)g.GetInt(e.Children[1]);
        return @out;
    }

    public static List<string> RosterOrder(GvasFile g) =>
        (Node(g, "Roster").Children ?? new List<GvasNode>()).Select(e => g.GuidHex(e)).ToList();

    public static int StrategyTurn(GvasFile g) => (int)g.GetInt(Node(g, "StrategyTurn"));

    /// <summary>Bring <paramref name="guid"/> back from the fallen.</summary>
    public static GvasFile Revive(GvasFile g, string guid, int? maxRoster, CutInfo cut, ISet<string> touched)
    {
        guid = guid.ToUpperInvariant();
        var dead = DeadGuids(g);
        if (!dead.Contains(guid)) throw new GvasException("operator is not among the fallen");
        if (RosterOrder(g).Contains(guid)) throw new GvasException("operator is already on the roster");
        if (maxRoster != null && RosterOrder(g).Count >= maxRoster) throw new GvasException($"the roster is full ({maxRoster})");
        if (!MedbayOps.Characters(g).ContainsKey(guid)) throw new GvasException("no saved character data for this operator");
        int turn = StrategyTurn(g);

        // 1. DeadCharacters
        var arr = Node(g, "DeadCharacters");
        int idx = dead.IndexOf(guid);
        var raw = g.Slice(arr.Children![idx].Start, arr.Children[idx].End);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        g = g.ArrayRemoveElement(arr, idx);

        // 2. CharacterIDsToDiedTurns
        var mp = Node(g, "CharacterIDsToDiedTurns");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(mp)));
        var kids = mp.Children ?? new List<GvasNode>();
        int hit = kids.FindIndex(e => g.GuidHex(e.Children![0]) == guid);
        if (hit >= 0) g = g.MapRemoveEntry(mp, hit);

        // 3. Roster (append) and 4. CharacterIDsToRecruitedTurns (append guid + turn)
        arr = Node(g, "Roster");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        g = g.ArrayAppendRaw(arr, raw);
        mp = Node(g, "CharacterIDsToRecruitedTurns");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(mp)));
        if (!RecruitedMap(g).ContainsKey(guid))
            g = g.MapAppendRaw(mp, raw.Concat(BitConverter.GetBytes(turn)).ToArray());

        // 5. death / injury effects
        while (true)
        {
            var eff = MedbayOps.EffectsNode(g, guid) ?? throw new GvasException("operator effects not found");
            var keys = MedbayOps.EffectKeys(g, eff);
            var ek = eff.Children ?? new List<GvasNode>();
            int i = ek.FindIndex(el => DeadEffects.Any(d => MedbayOps.EffectName(g, el).StartsWith(d, StringComparison.Ordinal)));
            if (i < 0) break;
            cut.OpaqueRemoved += g.Walk(new[] { ek[i] }).Count(n => n.Opaque);
            var baseKey = GvasFile.PathOf(eff);
            cut.Prefixes.Add(PathKey.Of(baseKey, keys[i]));
            touched.Add(PathKey.Of(baseKey, "#count"));
            g = g.ArrayRemoveElement(eff, i);
        }
        cut.Revived.Add(guid);
        return g;
    }
}
