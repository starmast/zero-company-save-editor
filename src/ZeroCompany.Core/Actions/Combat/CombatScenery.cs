using System.Text;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

// Scenery: fire hazard zones (turns left) and destructible props (health). Both are plain scalars, so they ride the normal field-edit pipeline.
public static partial class CombatOps
{
    static void BuildScenery(GvasFile g, CombatView view)
    {
        var t = Tactical(g);
        foreach (var e in g.Child(t, "AbilityActorInfos")?.Children ?? new List<GvasNode>())
        {
            var name = ObjectName(g, g.Child(e, "ActorWrapper"));
            if (!name.Contains("HazardZone", StringComparison.Ordinal)) continue;
            var ab = g.Child(g.Child(e, "ActorWrapper"), "ArchiveBytes");
            var left = Scalar(g, g.Child(ab, "HazardTurnsRemaining"), 0, 99999);
            if (left == null) continue;
            var h = new HazardView
            {
                Name = name, Title = NumberedTitle(name, "BP_HazardZone_"), TurnsLeft = left,
                Radius = g.Child(ab, "Radius") is { } r ? g.GetDouble(r) : null,
            };
            if (g.Child(g.Child(e, "Transform"), "Translation") is { Size: 24 } tr)
            {
                h.X = BitConverter.ToDouble(g.Data, tr.ValueOffset);
                h.Y = BitConverter.ToDouble(g.Data, tr.ValueOffset + 8);
            }
            view.Hazards.Add(h);
        }
        var records = ActorRecords(g);
        foreach (var d in g.Child(t, "DestructibleActorInfos")?.Children ?? new List<GvasNode>())
        {
            var actor = g.Child(d, "DestructibleActor") is { } da ? g.GetStr(da) : "";
            var name = After(actor, (char)46);
            FieldRef? hp = null, max = null;
            foreach (var av in g.Child(g.Child(d, "AttributeSetAndValues"), "AttributeValues")?.Children ?? new List<GvasNode>())
            {
                if (g.Child(av, "Name") is not { } nm) continue;
                var n = g.GetStr(nm);
                if (n == "Health") hp = Scalar(g, g.Child(av, "BaseValue"), 0, 999);
                else if (n == "MaxHealth") max = Scalar(g, g.Child(av, "BaseValue"), 1, 999);
            }
            if (hp == null || max == null) continue;
            var condition = records.TryGetValue(actor, out var rec) ? Condition(g, rec) : "";
            view.Destructibles.Add(new DestructibleView { Name = name, Title = SceneryTitle(name), Health = hp, MaxHealth = max, Actor = actor, Condition = condition });
        }
        view.DestroyedProps.AddRange(records.Where(kv => IsTag(g, kv.Value, DestroyedTag)).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal));
        view.WreckedProps.AddRange(WreckedActors(g));
    }

    // ---- destroyed or not: the actor record's tag ------------------------------------------------------------------------
    // The game keeps a destructible prop's state in two places. Its health sits in DestructibleActorInfos, but what rebuilds or wrecks it
    // when the save loads is a gameplay-tag container in its actor record (SaveLoadActorWrappers[].ArchiveBytes): "...Object.Pristine"
    // for an intact prop, "...Object.Destroyed" for a wrecked one. Setting health back is not enough on its own.
    const string DestroyedTag = "BitReactor.Status.Object.Destroyed";
    const string PristineTag = "BitReactor.Status.Object.Pristine";
    const string DamagedTag = "BitReactor.Status.Object.Damaged";

    /// <summary>
    /// The state tag in an actor record, or null when the record is not one. A record is [byte count][three int32 flags][the tag as an
    /// FString]. The flags vary from prop to prop and change when the game destroys one (an intact prop has (1,0,0) or (0,1,0), a
    /// destroyed one (0,0,1) or (0,0,0)) and nothing I found says why, so they are only read around, never interpreted.
    /// </summary>
    static string? RecordTag(GvasFile g, GvasNode record)
    {
        if (record.Size < 24) return null;
        var d = g.Data; int o = record.ValueOffset;
        if (BitConverter.ToInt32(d, o) != record.Size - 4) return null;
        int len = BitConverter.ToInt32(d, o + 16);
        if (len < 2 || 20 + len != record.Size || d[o + 20 + len - 1] != 0) return null;
        return Encoding.ASCII.GetString(d, o + 20, len - 1);
    }

    static bool IsTag(GvasFile g, GvasNode record, string tag) => RecordTag(g, record) == tag;

    /// <summary>
    /// A record in the shape the game writes for a prop in a state: the byte count, the flags, then the tag. Intact props in the game's own
    /// saves use (0,1,0) for two thirds of them and (1,0,0) for most of the rest, so a restored prop gets the common one.
    /// </summary>
    static byte[] TagRecord(string tag, int a = 0, int b = 1, int c = 0)
    {
        var s = Encoding.ASCII.GetBytes(tag + "\0");
        var data = new byte[12 + 4 + s.Length];
        BitConverter.GetBytes(a).CopyTo(data, 0); BitConverter.GetBytes(b).CopyTo(data, 4); BitConverter.GetBytes(c).CopyTo(data, 8);
        BitConverter.GetBytes(s.Length).CopyTo(data, 12);
        s.CopyTo(data, 16);
        return BitConverter.GetBytes(data.Length).Concat(data).ToArray();
    }


    /// <summary>Every saved actor's ArchiveBytes node by full object path (the first one wins if a path repeats).</summary>
    internal static Dictionary<string, GvasNode> ActorRecords(GvasFile g)
    {
        var d = new Dictionary<string, GvasNode>();
        foreach (var w in g.Child(Tactical(g), "SaveLoadActorWrappers")?.Children ?? new List<GvasNode>())
            if (g.Child(w, "Actor") is { } a && g.Child(w, "ArchiveBytes") is { } ab) d.TryAdd(g.GetStr(a), ab);
        return d;
    }

    /// <summary>"Destroyed", "Damaged" or "Pristine" from a record's tag, or "" for anything else.</summary>
    static string Condition(GvasFile g, GvasNode record) => RecordTag(g, record) switch
    {
        DestroyedTag => "Destroyed", DamagedTag => "Damaged", PristineTag => "Pristine", _ => "",
    };

    /// <summary>Full object paths of every prop that is destroyed or damaged (what Restore can bring back to pristine).</summary>
    public static List<string> WreckedActors(GvasFile g) =>
        ActorRecords(g).Where(kv => Condition(g, kv.Value) is "Destroyed" or "Damaged").Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>Full object paths of every prop whose record says Destroyed (used to verify an edit).</summary>
    public static List<string> DestroyedActors(GvasFile g) =>
        ActorRecords(g).Where(kv => IsTag(g, kv.Value, DestroyedTag)).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>True when the prop's record says Pristine (an intact prop).</summary>
    public static bool IsPristine(GvasFile g, string actor) => ActorRecords(g).TryGetValue(actor, out var r) && IsTag(g, r, PristineTag);

    /// <summary>The prop's health and maximum from DestructibleActorInfos, or null when it has no entry.</summary>
    public static (double Health, double Max)? PropHealth(GvasFile g, string actor)
    {
        foreach (var d in g.Child(Tactical(g), "DestructibleActorInfos")?.Children ?? new List<GvasNode>())
        {
            if (g.Child(d, "DestructibleActor") is not { } da || g.GetStr(da) != actor) continue;
            double? hp = null, max = null;
            foreach (var av in g.Child(g.Child(d, "AttributeSetAndValues"), "AttributeValues")?.Children ?? new List<GvasNode>())
            {
                if (g.Child(av, "Name") is not { } nm || g.Child(av, "BaseValue") is not { } bv) continue;
                if (g.GetStr(nm) == "Health") hp = g.GetDouble(bv); else if (g.GetStr(nm) == "MaxHealth") max = g.GetDouble(bv);
            }
            return hp != null && max != null ? (hp.Value, max.Value) : null;
        }
        return null;
    }

    /// <summary>
    /// Bring destroyed props back: each record goes from Destroyed to Pristine (written the way the game writes an intact prop, one
    /// byte shorter for a destroyed one) and its health, if it has an entry, back to its maximum. A prop that is neither destroyed nor damaged is refused (a stale list).
    /// </summary>
    public static GvasFile RestoreScenery(GvasFile g, IReadOnlyList<string> actors, CutInfo cut)
    {
        foreach (var actor in actors.Distinct())
        {
            if (!ActorRecords(g).TryGetValue(actor, out var rec)) throw new GvasException("a prop is no longer in the save; re-open it");
            if (Condition(g, rec) is not ("Destroyed" or "Damaged")) throw new GvasException("a prop is neither destroyed nor damaged; re-open the save");
            cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(rec)));
            g = g.ReplaceValue(rec, TagRecord(PristineTag));
            if (PropHealth(g, actor) is { } h && h.Health != h.Max) SetPropHealth(g, actor, h.Max, cut);
            cut.RestoredScenery.Add(actor);
        }
        return g;
    }

    static void SetPropHealth(GvasFile g, string actor, double value, CutInfo cut)
    {
        foreach (var d in g.Child(Tactical(g), "DestructibleActorInfos")?.Children ?? new List<GvasNode>())
        {
            if (g.Child(d, "DestructibleActor") is not { } da || g.GetStr(da) != actor) continue;
            foreach (var av in g.Child(g.Child(d, "AttributeSetAndValues"), "AttributeValues")?.Children ?? new List<GvasNode>())
                if (g.Child(av, "Name") is { } nm && g.GetStr(nm) == "Health" && g.Child(av, "BaseValue") is { } bv)
                {
                    cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(bv)));
                    g.Set(bv, value);
                    return;
                }
        }
    }

    /// <summary>"BP_SEP_ExtA_RailingB_Strx2_ZUpSnow_A_DS_C_13" -> "Sep Ext A Railing B Strx2 ... 14".</summary>
    static string SceneryTitle(string name)
    {
        var body = name;
        foreach (var p in new[] { "BP_SEP_", "BP_UNI_", "BP_OTR_", "BP_" })
            if (body.StartsWith(p, StringComparison.Ordinal)) { body = body[p.Length..]; break; }
        foreach (var junk in new[] { "_ZUpSnow", "_GEN_VARIABLE", "_DS" }) body = body.Replace(junk, "", StringComparison.Ordinal);
        return NumberedTitle(body, "");
    }
}
