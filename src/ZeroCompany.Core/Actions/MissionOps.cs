using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Actions;

/// <summary>One mission the Den offers (StrategyData/AvailableMissions).</summary>
public sealed record MissionEntry(string Id, string Asset, string Folder, string Status, long TurnAdded, long Threat, string SpawnSet, string CrisisEffect)
{
    public bool Available => Status == "Available";
}

/// <summary>
/// What can be changed on a mission offered in the Den: the enemy faction and threat tier it spawns (SelectedSpawnSet), how long it
/// stays on offer (TurnAdded against the strategy turn and the mission's lifespan), and, for a crisis mission, which Coil upgrade it
/// is about (SelectedCrisisEffect).
/// </summary>
public static class MissionOps
{
    static readonly Regex CrisisEffectPath = new(@"^/Game/.*/CrisisEffects/[^.]+/DA_[A-Za-z0-9]+_Crisis_(Major|Minor)_[AB]\.DA_[A-Za-z0-9]+_Crisis_(Major|Minor)_[AB]$", RegexOptions.Compiled);

    static GvasNode? Map(GvasFile g) => g.StrategyDataOrNull() is { } sd ? g.Child(sd, "AvailableMissions") : null;

    static IEnumerable<(GvasNode Entry, GvasNode Value)> Raw(GvasFile g)
    {
        foreach (var e in Map(g)?.Children ?? new List<GvasNode>())
            if (e.Children is { Count: > 1 } && e.Children[1] is { } v) yield return (e, v);
    }

    /// <summary>The strategy turn (cycle) the save is at; missions count their time on offer from it.</summary>
    public static long StrategyTurn(GvasFile g) => g.StrategyDataOrNull() is { } sd && g.Child(sd, "StrategyTurn") is { } t ? g.GetInt(t) : -1;

    public static List<MissionEntry> Available(GvasFile g)
    {
        var list = new List<MissionEntry>();
        foreach (var (e, v) in Raw(g))
        {
            if (g.Child(v, "MissionData") is not { } md) continue;
            var path = g.GetStr(md);
            var parts = path.Split('/');
            string folder = parts.Length >= 2 ? parts[^2] : "";
            string status = g.Child(v, "Status") is { } st ? g.GetStr(st) : "";
            status = status[(status.LastIndexOf(':') + 1)..];
            list.Add(new MissionEntry(ArmoryOps.StructGuid(g, e.Children![0]), Naming.AssetName(path), folder, status,
                g.Child(v, "TurnAdded") is { } t ? g.GetInt(t) : -1, g.Child(v, "ThreatLevelWhenAdded") is { } th ? g.GetInt(th) : -1,
                g.Child(v, "SelectedSpawnSet") is { } ss ? g.GetStr(ss) : "", g.Child(v, "SelectedCrisisEffect") is { } ce ? g.GetStr(ce) : ""));
        }
        return list;
    }

    static GvasNode ValueOf(GvasFile g, string id, bool needAvailable = true)
    {
        var v = Raw(g).Where(x => ArmoryOps.StructGuid(g, x.Entry.Children![0]) == id).Select(x => x.Value).FirstOrDefault()
            ?? throw new GvasException("that mission is not offered any more");
        if (needAvailable && !Available(g).First(m => m.Id == id).Available) throw new GvasException("only a mission that is still available can be changed");
        return v;
    }

    /// <summary>Point an available mission at another known enemy spawn set (both the object and the soft path the save keeps).</summary>
    public static GvasFile SetSpawnSet(GvasFile g, string id, string set, CutInfo cut)
    {
        var v = ValueOf(g, id);
        var current = g.Child(v, "SelectedSpawnSet") is { } cn ? g.GetStr(cn) : "";
        if (current.Length == 0) throw new GvasException("that mission has no enemy spawn set");
        if (!CombatOps.SpawnSetChoices(current).Contains(set)) throw new GvasException("that is not a known spawn set");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(g.Child(v, "SelectedSpawnSet")!)));
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(g.Child(v, "SoftSelectedSpawnSet")!)));
        cut.MissionSets[id] = set;
        if (current == set) return g;
        return SetObjectAndSoft(g, id, "SelectedSpawnSet", "SoftSelectedSpawnSet", set);
    }

    /// <summary>Change which Coil upgrade a crisis mission is about. Only crisis missions have one, and only a known crisis effect is accepted.</summary>
    public static GvasFile SetCrisisEffect(GvasFile g, string id, string effect, CutInfo cut)
    {
        var v = ValueOf(g, id);
        var current = g.Child(v, "SelectedCrisisEffect") is { } cn ? g.GetStr(cn) : "";
        if (current.Length == 0) throw new GvasException("that mission is not a crisis mission");
        if (!CrisisEffectPath.IsMatch(effect)) throw new GvasException("that is not a known crisis effect");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(g.Child(v, "SelectedCrisisEffect")!)));
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(g.Child(v, "SoftSelectedCrisisEffect")!)));
        cut.MissionCrisis[id] = effect;
        if (current == effect) return g;
        return SetObjectAndSoft(g, id, "SelectedCrisisEffect", "SoftSelectedCrisisEffect", effect);
    }

    /// <summary>
    /// Set the cycle a mission was added on. It runs out when TurnAdded plus its lifespan passes the strategy turn, so a later
    /// TurnAdded gives it more time; one in the future is refused.
    /// </summary>
    public static GvasFile SetTurnAdded(GvasFile g, string id, int turn, CutInfo cut)
    {
        var v = ValueOf(g, id);
        long now = StrategyTurn(g);
        if (turn < 0 || now < 0 || turn > now) throw new GvasException("a mission cannot have been added after the current cycle");
        var node = g.Child(v, "TurnAdded") ?? throw new GvasException("that mission has no cycle it was added on");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(node)));
        cut.MissionTurns[id] = turn;
        g.Set(node, turn);
        return g;
    }

    static GvasFile SetObjectAndSoft(GvasFile g, string id, string objectName, string softName, string path)
    {
        GvasNode V() => ValueOf(g, id, needAvailable: false);
        g = g.SetString(g.Child(V(), objectName)!, path);
        return ArmoryOps.SetSoftPath(g, g.Child(V(), softName)!, path[..path.IndexOf('.')], Naming.AssetName(path));
    }
}
