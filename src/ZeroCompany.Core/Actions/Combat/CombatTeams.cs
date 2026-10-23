using System.Text;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

// Teams: who a unit fights for, and the many places the save records it.
public static partial class CombatOps
{
    public static readonly IReadOnlyList<string> Teams = new[] { "Player", "Enemy", "Enemy2", "Civilian" };

    /// <summary>Hostile to the squad. Enemy2 is a second hostile team (e.g. Imperials beside the Coil) that also fights Enemy.</summary>
    public static bool IsEnemyTeam(string? team) => team is "Enemy" or "Enemy2";

    static GvasNode? TeamNode(GvasFile g, int unit) => g.Child(g.Child(UnitAt(g, unit), "TeamTag"), "TagName");

    /// <summary>Last segment of the unit's team tag ("Player", "Enemy", ...), or null.</summary>
    public static string? TeamOf(GvasFile g, int unit) => TeamNode(g, unit) is { } n ? After(g.GetStr(n), '.') : null;

    public static string TeamTagOf(GvasFile g, int unit) => g.GetStr(TeamNode(g, unit)!);

    public static bool IsFriendly(GvasFile g, int unit) => g.GetBool(g.Child(UnitAt(g, unit), "bIsFriendly"));

    static GvasNode? ActorProp(GvasFile g, int unit, string name) =>
        g.Child(g.Child(g.Child(UnitAt(g, unit), "ActorWrapper"), "ArchiveBytes"), name);

    static GvasNode? TagsNode(GvasFile g, int unit) => g.Child(Asc(g, unit), "GameplayTags");

    /// <summary>The attribute value node (e.g. "BitReactorCombatSet", "RefreshActionPoints") of a unit, or null.</summary>
    static GvasNode? AttrNode(GvasFile g, int unit, string set, string name)
    {
        foreach (var s in g.Child(Asc(g, unit), "AttributeSetsAndValues")?.Children ?? new List<GvasNode>())
        {
            var cls = g.Child(s, "AttributeSetClass");
            if (cls == null || !g.GetStr(cls).EndsWith("." + set, StringComparison.Ordinal)) continue;
            foreach (var av in g.Child(s, "AttributeValues")?.Children ?? new List<GvasNode>())
                if (g.Child(av, "Name") is { } n && g.GetStr(n) == name) return g.Child(av, "BaseValue");
        }
        return null;
    }

    // ---- the unit's gameplay-tag container ---------------------------------------------
    /// <summary>
    /// A native blob of int32 count, then per tag an int32 length (including the NUL) and the ASCII name with its NUL.
    /// Null when it does not have exactly that layout.
    /// </summary>
    public static List<string>? ParseTags(byte[] data, int off, int size)
    {
        try
        {
            int end = off + size, p = off;
            if (size < 4) return null;
            int count = BitConverter.ToInt32(data, p); p += 4;
            if (count < 0 || count > 4096) return null;
            var tags = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (p + 4 > end) return null;
                int len = BitConverter.ToInt32(data, p);
                if (len < 1 || p + 4 + len > end || data[p + 4 + len - 1] != 0) return null;
                tags.Add(Encoding.ASCII.GetString(data, p + 4, len - 1));
                p += 4 + len;
            }
            return p == end ? tags : null;
        }
        catch (ArgumentException) { return null; }
    }

    static byte[] WriteTags(IReadOnlyList<string> tags)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(tags.Count));
        foreach (var t in tags)
        {
            ms.Write(BitConverter.GetBytes(t.Length + 1));
            ms.Write(Encoding.ASCII.GetBytes(t));
            ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    public static List<string>? UnitTags(GvasFile g, int unit) =>
        TagsNode(g, unit) is { } n ? ParseTags(g.Data, n.ValueOffset, n.Size) : null;

    // ---- behaviour ---------------------------------------------------------------------
    /// <summary>"Behavior_Player" for the player team, "Behavior_Enemy" for everyone else (civilians are AI-driven too).</summary>
    public static string BehaviorToken(string team) => team == "Player" ? "Behavior_Player" : "Behavior_Enemy";

    /// <summary>True when the unit's behaviour class belongs to its team.</summary>
    public static bool BehaviorMatches(GvasFile g, int unit, string team) =>
        ActorProp(g, unit, "BehaviorClass") is not { } n || g.GetStr(n).Contains(BehaviorToken(team), StringComparison.Ordinal);

    /// <summary>Swap <paramref name="from"/> for <paramref name="to"/> in the behaviour class and state-machine strings found by <paramref name="find"/>.</summary>
    static GvasFile PatchBehavior(GvasFile g, Func<GvasFile, string, GvasNode?> find, string from, string to)
    {
        foreach (var name in new[] { "BehaviorClass", "StateMachine" })
        {
            if (find(g, name) is not { } n) continue;
            var v = g.GetStr(n);
            if (v.Contains(from, StringComparison.Ordinal)) g = g.SetString(n, v.Replace(from, to, StringComparison.Ordinal));
        }
        return g;
    }

    // ---- the second copy in SaveGameSpawnedActors --------------------------------------
    static GvasNode? SpawnedElement(GvasFile sg, string actor) =>
        sg.Root.FirstOrDefault(r => r.Name == "TrackedSpawnedActors")?.Children?
            .FirstOrDefault(e => sg.Child(e, "ActorName") is { } n && sg.GetStr(n) == actor);

    /// <summary>Every tracked spawned actor by name (the first one wins if a name repeats).</summary>
    public static Dictionary<string, GvasNode> SpawnedElements(GvasFile sg)
    {
        var d = new Dictionary<string, GvasNode>();
        foreach (var e in sg.Root.FirstOrDefault(r => r.Name == "TrackedSpawnedActors")?.Children ?? new List<GvasNode>())
            if (sg.Child(e, "ActorName") is { } n) d.TryAdd(sg.GetStr(n), e);
        return d;
    }

    static GvasNode? SpawnedProp(GvasFile sg, string actor, string name) =>
        sg.Child(sg.Child(sg.Child(SpawnedElement(sg, actor), "Wrapper"), "ArchiveBytes"), name);

    /// <summary>Behaviour class of a unit's copy in SaveGameSpawnedActors, or null when it has none.</summary>
    public static string? SpawnedBehavior(GvasFile sg, string actor) =>
        SpawnedProp(sg, actor, "BehaviorClass") is { } n ? sg.GetStr(n) : null;

    // ---- the switch --------------------------------------------------------------------
    static (double X, double Y)? XY(GvasFile g, int unit)
    {
        var t = g.Child(g.Child(UnitAt(g, unit), "Transform"), "Translation");
        return t == null || t.Size != 24 ? null : (BitConverter.ToDouble(g.Data, t.ValueOffset), BitConverter.ToDouble(g.Data, t.ValueOffset + 8));
    }

    /// <summary>The living, native (enemy-behaviour) enemy closest to <paramref name="unit"/>, or -1.</summary>
    static int NearestNativeEnemy(GvasFile g, int unit, string team)
    {
        var me = XY(g, unit);
        int best = -1; double bd = double.MaxValue;
        for (int i = 0; i < Units(g).Count; i++)
        {
            if (i == unit || TeamOf(g, i) != team || IsDead(g, i) || !BehaviorMatches(g, i, team)) continue;
            var p = XY(g, i);
            double d = me == null || p == null ? 0 : Math.Pow(me.Value.X - p.Value.X, 2) + Math.Pow(me.Value.Y - p.Value.Y, 2);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    static bool IsAiTag(string t) =>
        t.StartsWith("br.AI.", StringComparison.Ordinal) || t.StartsWith("br.Faction.", StringComparison.Ordinal)
        || t is "BitReactor.Status.Character.DifficultyApplied" or "BitReactor.Status.Character.CanBeConsumed";

    /// <summary>
    /// Move a unit to another team the way the game does when it switches one (a rescued civilian joins your squad): the team
    /// tag and friendly flag, the team tag inside the unit's gameplay-tag container, and the behaviour class and state-machine
    /// name, in the main save and in the spawned-actors copy. Tag text and friendly flag come from a unit already on that team
    /// in this save, so they are the game's own values. Health, position and effects are not touched.
    /// </summary>
    public static GvasFile SetTeam(GvasFile g, int unit, string team, CutInfo cut)
    {
        if (!Teams.Contains(team)) throw new GvasException($"unknown team '{team}'");
        if (team == "Enemy2" && !IsThreeWay(g)) throw new GvasException("Enemy2 only works in three-way battles; the game puts those units back on Enemy in other missions");
        if (UnitAt(g, unit) == null) throw new GvasException("unit not found");
        var tn = TeamNode(g, unit) ?? throw new GvasException("this unit has no team tag, so its team cannot be changed");
        var flag = g.Child(UnitAt(g, unit), "bIsFriendly") ?? throw new GvasException("unit has no friendly flag");
        string oldTag = g.GetStr(tn), oldTeam = After(oldTag, '.');
        if (!Teams.Contains(oldTeam)) throw new GvasException($"this unit is on no side the game fights for ({oldTeam}), so its team cannot be changed");
        string actor = ActorName(g, UnitAt(g, unit)!);
        string tag = "BitReactor.Team." + team;
        bool friendly = !IsEnemyTeam(team);                     // only used when the save has no unit on that team to copy
        for (int i = 0; i < Units(g).Count; i++)
            if (TeamOf(g, i) == team) { tag = TeamTagOf(g, i); friendly = IsFriendly(g, i); break; }
        cut.UnitTeams[unit] = (team, friendly);
        foreach (var n in new[] { tn, ActorProp(g, unit, "BehaviorClass"), ActorProp(g, unit, "StateMachine"), TagsNode(g, unit) })
            if (n != null) cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));

        g.SetBool(flag, friendly);                // in place; every string edit below copies the buffer

        // Becoming an enemy: a unit that never was one lacks what the enemy AI works from, so copy its AI area / faction tags
        // and action-point refresh values from the nearest native enemy. (A best guess; not verified to be enough.)
        List<string> aiTags = new();
        int template = IsEnemyTeam(team) && !IsEnemyTeam(oldTeam) ? NearestNativeEnemy(g, unit, team) : -1;
        if (template < 0 && IsEnemyTeam(team) && !IsEnemyTeam(oldTeam)) template = NearestNativeEnemy(g, unit, team == "Enemy" ? "Enemy2" : "Enemy");
        if (template >= 0)
        {
            aiTags = (UnitTags(g, template) ?? new List<string>()).Where(IsAiTag).ToList();
            foreach (var name in new[] { "RefreshActionPoints", "RefreshMovementActionPoints", "MovementActionPoints" })
            {
                var src = AttrNode(g, template, "BitReactorCombatSet", name); var dst = AttrNode(g, unit, "BitReactorCombatSet", name);
                if (src == null || dst == null) continue;
                cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(dst)));
                g.Set(dst, g.Get(src));
            }
        }

        string from = BehaviorToken(team == "Player" ? "Enemy" : "Player"), to = BehaviorToken(team);
        g = PatchBehavior(g, (f, name) => ActorProp(f, unit, name), from, to);
        if (cut.SpawnedActors != null && actor.Length > 0)
        {
            var patched = PatchBehavior(cut.SpawnedActors, (f, name) => SpawnedProp(f, actor, name), from, to);
            if (!ReferenceEquals(patched, cut.SpawnedActors)) { cut.SpawnedActors = patched; cut.SpawnedActorTeams[actor] = team; }
        }

        if (TagsNode(g, unit) is { } tags && ParseTags(g.Data, tags.ValueOffset, tags.Size) is { } list)
        {
            foreach (var t in aiTags) if (!list.Contains(t)) list.Add(t);
            int at = list.IndexOf(oldTag);
            if (at >= 0) { if (list.Contains(tag)) list.RemoveAt(at); else list[at] = tag; }
            else if (!list.Contains(tag)) list.Add(tag);
            g = g.ReplaceValue(tags, WriteTags(list));
        }
        return g.SetString(TeamNode(g, unit)!, tag);
    }
}
