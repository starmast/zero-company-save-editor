using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

// The squad of the mission in progress. The game spawns the heroes of a fight from MissionSquadCharacterIDs (the GUIDs of roster
// characters, in GameInstanceSaveGameWrapper) when it loads the level, then applies each saved unit's state by actor name. Adding a
// roster GUID there makes the game spawn that operator as well; they arrive with their saved roster state (health, injuries, focus).
public static partial class CombatOps
{
    const string HeroPrefix = "Char_Hero_";

    static GvasNode? GameInstanceArchive(GvasFile g) => g.Child(g.GameInstance(), "ArchiveBytes");

    static GvasNode? SquadNode(GvasFile g) => g.Child(GameInstanceArchive(g), "MissionSquadCharacterIDs");

    /// <summary>The squad list of the mission that is in progress (StrategyData/AvailableMissions), or null.</summary>
    static GvasNode? InProgressMissionSquad(GvasFile g)
    {
        foreach (var m in g.Child(g.StrategyData(), "AvailableMissions")?.Children ?? new List<GvasNode>())
        {
            var v = g.Child(m, "value");
            if (g.Child(v, "Status") is { } st && g.GetStr(st).EndsWith("InProgress", StringComparison.Ordinal))
                return g.Child(v, "SquadCharacterIDs");
        }
        return null;
    }

    /// <summary>The GUIDs (hex) in the mission squad, in order.</summary>
    public static List<string> SquadGuids(GvasFile g) =>
        (SquadNode(g)?.Children ?? new List<GvasNode>()).Select(n => g.GuidHex(n)).ToList();

    /// <summary>The in-progress mission's own squad list (hex), or null when no mission is in progress.</summary>
    public static List<string>? MissionSquadGuids(GvasFile g) =>
        InProgressMissionSquad(g)?.Children?.Select(n => g.GuidHex(n)).ToList();

    static GvasNode? RosterGuidNode(GvasFile g, string guid)
    {
        var blob = g.Child(g.Child(GameInstanceArchive(g), "CharacterDataWrapper"), "ArchiveBytes");
        return g.Child(blob, "CharacterGuids")?.Children?.FirstOrDefault(n => g.GuidHex(n) == guid);
    }

    static void BuildSquad(GvasFile g, CombatView view)
    {
        if (SquadNode(g) == null) return;
        var inSquad = SquadGuids(g).ToHashSet();
        var dead = new HashSet<string>();
        foreach (var e in g.Child(g.StrategyData(), "DeadCharacters")?.Children ?? new List<GvasNode>()) dead.Add(g.GuidHex(e));
        foreach (var (guid, wrapper) in MedbayOps.Characters(g))
        {
            var cd = g.Child(g.Child(wrapper, "ArchiveBytes"), "CharacterData");
            var cls = g.Child(cd, "CharacterClassName") is { } c ? g.GetStr(c) : "";
            if (!cls.StartsWith(HeroPrefix, StringComparison.Ordinal)) continue;           // recruited civilians and soldiers are not operators
            var name = Naming.DisplayName(cls);
            if (name.StartsWith("Custom Operator", StringComparison.Ordinal)) name = Naming.RealName(g, wrapper) ?? name;
            view.Squad.Add(new SquadMemberView { Guid = guid, Name = name, Class = cls, Dead = dead.Contains(guid), InSquad = inSquad.Contains(guid), Injuries = MedbayOps.InjuryCount(g, guid) });
        }
    }

    /// <summary>Put a roster operator into the fight: their GUID goes on the mission squad (and on the in-progress mission's own squad list).</summary>
    public static GvasFile AddSquadMember(GvasFile g, string guid, CutInfo cut)
    {
        var view = new CombatView();
        BuildSquad(g, view);
        var who = view.Squad.FirstOrDefault(m => m.Guid == guid) ?? throw new GvasException("that operator is not on the roster");
        if (who.Dead) throw new GvasException($"{who.Name} has fallen and cannot join a fight");
        if (who.InSquad || cut.SquadAdded.Contains(guid)) throw new GvasException($"{who.Name} is already in this fight");
        var raw = g.Slice(RosterGuidNode(g, guid)!.Start, RosterGuidNode(g, guid)!.End);

        var arr = SquadNode(g)!;
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        g = g.ArrayAppendRaw(arr, raw);
        if (InProgressMissionSquad(g) is { } own)
        {
            cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(own)));
            if (!own.Children!.Any(n => g.GuidHex(n) == guid)) g = g.ArrayAppendRaw(own, raw);
            cut.MissionSquadTouched = true;
        }
        cut.SquadAdded.Add(guid);
        return g;
    }
}
