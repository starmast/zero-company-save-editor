using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Actions;

/// <summary>One mission the Den offers (StrategyData/AvailableMissions).</summary>
public sealed record MissionEntry(string Id, string Asset, string Folder, string Status, long TurnAdded, long Threat, string SpawnSet)
{
    public bool Available => Status == "Available";
}

/// <summary>
/// The enemy faction and threat tier of each mission offered in the Den (SelectedSpawnSet). The game reads it when the mission's
/// encounter starts, so it is the one place the enemy composition can be chosen before launching.
/// </summary>
public static class MissionOps
{
    static GvasNode? Map(GvasFile g) => g.StrategyDataOrNull() is { } sd ? g.Child(sd, "AvailableMissions") : null;

    static IEnumerable<(GvasNode Entry, GvasNode Value)> Raw(GvasFile g)
    {
        foreach (var e in Map(g)?.Children ?? new List<GvasNode>())
            if (e.Children is { Count: > 1 } && e.Children[1] is { } v) yield return (e, v);
    }

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
                g.Child(v, "SelectedSpawnSet") is { } ss ? g.GetStr(ss) : ""));
        }
        return list;
    }

    /// <summary>Point an available mission at another known enemy spawn set (both the object and the soft path the save keeps).</summary>
    public static GvasFile SetSpawnSet(GvasFile g, string id, string set, CutInfo cut)
    {
        var (e, v) = Raw(g).FirstOrDefault(x => ArmoryOps.StructGuid(g, x.Entry.Children![0]) == id);
        if (v == null) throw new GvasException("that mission is not offered any more");
        var current = g.Child(v, "SelectedSpawnSet") is { } cn ? g.GetStr(cn) : "";
        if (current.Length == 0) throw new GvasException("that mission has no enemy spawn set");
        if (!Available(g).First(m => m.Id == id).Available) throw new GvasException("only a mission that is still available can be changed");
        if (!CombatOps.SpawnSetChoices(current).Contains(set)) throw new GvasException("that is not a known spawn set");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(g.Child(v, "SelectedSpawnSet")!)));
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(g.Child(v, "SoftSelectedSpawnSet")!)));
        cut.MissionSets[id] = set;
        if (current == set) return g;
        var soft = set[..set.IndexOf('.')];
        g = g.SetString(g.Child(Raw(g).First(x => ArmoryOps.StructGuid(g, x.Entry.Children![0]) == id).Value, "SelectedSpawnSet")!, set);
        var v2 = Raw(g).First(x => ArmoryOps.StructGuid(g, x.Entry.Children![0]) == id).Value;
        return ArmoryOps.SetSoftPath(g, g.Child(v2, "SoftSelectedSpawnSet")!, soft, Naming.AssetName(set));
    }
}
