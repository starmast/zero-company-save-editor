using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// The in-combat state of a combat save: <c>TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos[]</c>, one element per unit on
/// the map (squad, enemies and civilians). Health, action points and the like are attribute base values (plain scalars, so they
/// ride the normal field-edit pipeline); the dead flag, effects, teams, objectives and reinforcements have their own actions.
/// Split by topic: units and the view builder here, then <c>CombatTeams</c>, <c>CombatObjectives</c>, <c>CombatReinforcements</c>.
/// </summary>
public static partial class CombatOps
{
    const string Wrapper = "TacticalSaveGameWrapper";
    static readonly Regex UnitSuffix = new(@"_C_(\d+)$", RegexOptions.Compiled);

    public static GvasNode? Tactical(GvasFile g) =>
        g.Child(g.Root.FirstOrDefault(w => w.Name == Wrapper), "ArchiveBytes");

    public static IReadOnlyList<GvasNode> Units(GvasFile g) =>
        g.Child(Tactical(g), "CharacterInfos")?.Children ?? (IReadOnlyList<GvasNode>)Array.Empty<GvasNode>();

    public static bool IsCombatSave(GvasFile g) => Units(g).Count > 0;

    static GvasNode? UnitAt(GvasFile g, int unit) => unit < 0 || unit >= Units(g).Count ? null : Units(g)[unit];

    static GvasNode? Asc(GvasFile g, int unit) => g.Child(UnitAt(g, unit), "ASCInfo");

    static string After(string s, char c) => s[(s.LastIndexOf(c) + 1)..];

    /// <summary>"/Game/.../Map:PersistentLevel.Foo_C_0" -> "Foo_C_0", for the Actor reference inside an ActorWrapper-like struct.</summary>
    static string ObjectName(GvasFile g, GvasNode? wrapper) =>
        g.Child(wrapper, "Actor") is { } a ? After(g.GetStr(a), '.') : "";

    public static string ActorName(GvasFile g, GvasNode unit) => ObjectName(g, g.Child(unit, "ActorWrapper"));

    /// <summary>"BP_TacticalObjective_CriticalInteract_C_4" -> "Critical Interact 5": prefix dropped, class suffix made a 1-based number.</summary>
    static string NumberedTitle(string name, params string[] prefixes)
    {
        var m = UnitSuffix.Match(name);
        var body = m.Success ? name[..m.Index] : name;
        foreach (var p in prefixes)
            if (body.StartsWith(p, StringComparison.Ordinal)) { body = body[p.Length..]; break; }
        var t = Naming.Pretty(body);
        return m.Success ? $"{t} {int.Parse(m.Groups[1].Value) + 1}" : t;
    }

    /// <summary>A readable name: operators by name, everyone else from the actor class ("Imperial Trooper 3").</summary>
    public static string DisplayName(string actor) =>
        actor.StartsWith("Char_Hero_", StringComparison.Ordinal) ? Naming.DisplayName(actor)
            : NumberedTitle(actor, "Char_Enemy_", "Char_VIP_", "Char_");

    // ---- units and effects -----------------------------------------------------------
    public static GvasNode? EffectsNode(GvasFile g, int unit) => g.Child(Asc(g, unit), "ActiveEffects");

    public static string EffectDef(GvasFile g, GvasNode el) =>
        g.Child(g.Child(el, "Spec"), "Def") is { } d ? Naming.AssetName(g.GetStr(d)) : "";

    public static bool IsDead(GvasFile g, int unit) => g.GetBool(g.Child(UnitAt(g, unit), "bIsDead"));

    public static GvasFile SetDead(GvasFile g, int unit, bool dead)
    {
        var n = g.Child(UnitAt(g, unit) ?? throw new GvasException("unit not found"), "bIsDead")
                ?? throw new GvasException("unit has no dead flag");
        g.SetBool(n, dead);
        return g;
    }

    /// <summary>Remove one active effect. <paramref name="def"/> must still match, so a stale index is refused.</summary>
    public static GvasFile RemoveEffect(GvasFile g, int unit, int index, string def, CutInfo cut)
    {
        var arr = EffectsNode(g, unit) ?? throw new GvasException("unit not found");
        if (index < 0 || index >= (arr.Children?.Count ?? 0) || EffectDef(g, arr.Children![index]) != def)
            throw new GvasException("the effect list changed; re-open the save");
        cut.OpaqueRemoved += g.Walk(new[] { arr.Children[index] }).Count(n => n.Opaque);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        return g.ArrayRemoveElement(arr, index);
    }

    /// <summary>Append a byte-for-byte copy of another unit's effect to this unit's effect list.</summary>
    public static GvasFile CopyEffect(GvasFile g, int unit, int fromUnit, int fromIndex, string def, CutInfo cut)
    {
        var arr = EffectsNode(g, unit) ?? throw new GvasException("unit not found");
        var src = EffectsNode(g, fromUnit) ?? throw new GvasException("source unit not found");
        if (fromIndex < 0 || fromIndex >= (src.Children?.Count ?? 0) || EffectDef(g, src.Children![fromIndex]) != def)
            throw new GvasException("the effect list changed; re-open the save");
        var el = src.Children[fromIndex];
        cut.OpaqueRemoved -= g.Walk(new[] { el }).Count(n => n.Opaque);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(arr)));
        return g.ArrayAppendRaw(arr, g.Slice(el.Start, el.End));
    }

    // ---- the view of the whole combat state --------------------------------------------
    public static CombatView Build(GvasFile g, string title)
    {
        var view = new CombatView { Title = title };
        var units = Units(g);
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            var actor = ActorName(g, u);
            var cv = new CombatUnitView
            {
                Index = i, Actor = actor, Name = DisplayName(actor),
                Team = TeamOf(g, i) ?? "",
                Dead = IsDead(g, i),
                PlayerBehavior = ActorProp(g, i, "BehaviorClass") != null && BehaviorMatches(g, i, "Player"),
                Advantage = Scalar(g, g.Child(u, "CurrentAdvantage"), 0, 100),
            };
            var tr = g.Child(g.Child(u, "Transform"), "Translation");
            if (tr != null && SaveModel.VectorComponents(tr).ToList() is { Count: 3 } c)
            {
                cv.X = Vec(g, c[0]); cv.Y = Vec(g, c[1]); cv.Z = Vec(g, c[2]);
            }
            foreach (var s in g.Child(Asc(g, i), "AttributeSetsAndValues")?.Children ?? new List<GvasNode>())
            {
                var cls = g.Child(s, "AttributeSetClass");
                var vals = g.Child(s, "AttributeValues")?.Children;
                if (cls == null || vals == null) continue;
                var set = After(g.GetStr(cls), '.');                  // "/Script/BitReactorGame.BitReactorHealthSet" -> "BitReactorHealthSet"
                foreach (var av in vals)
                {
                    if (g.Child(av, "Name") is not { } nm) continue;
                    var name = g.GetStr(nm);
                    var fr = Scalar(g, g.Child(av, "BaseValue"), name is "Health" or "MaxHealth" or "ActionPoints" or "MovementActionPoints" ? 0 : -1e6, 1e6);
                    if (fr != null) cv.Attributes.Add(new AttributeView { Set = set, Name = name, Ref = fr });
                }
            }
            var effects = EffectsNode(g, i)?.Children ?? new List<GvasNode>();
            for (int e = 0; e < effects.Count; e++)
            {
                var spec = g.Child(effects[e], "Spec");
                var def = EffectDef(g, effects[e]);
                cv.Effects.Add(new UnitEffectView
                {
                    Index = e, Def = def, Title = Naming.Pretty(def.StartsWith("GE_", StringComparison.Ordinal) ? def[3..] : def),
                    Stacks = Scalar(g, g.Child(spec, "StackCount"), 0, 999),
                    Duration = g.Child(spec, "Duration") is { } d ? Convert.ToDouble(g.Get(d)) : null,
                });
            }
            view.Units.Add(cv);
        }
        BuildObjectives(g, view);
        BuildTimers(g, view);
        BuildReinforcements(g, view);
        BuildScenery(g, view);
        BuildSpawnSet(g, view);
        BuildTurnStructure(g, view);
        return view;
    }

    static FieldRef? Scalar(GvasFile g, GvasNode? n, double lo, double hi)
    {
        if (n == null || !GvasFile.Scalars.TryGetValue(n.TName, out var sz) || n.Size != sz) return null;
        var kind = n.TName is "FloatProperty" or "DoubleProperty" ? "float" : "int";
        return new FieldRef { Id = Naming.FieldId(n), Value = g.GetDouble(n), Min = lo, Max = hi, Kind = kind };
    }

    static FieldRef Vec(GvasFile g, GvasNode n) =>
        new() { Id = Naming.FieldId(n), Value = g.GetDouble(n), Min = -1e7, Max = 1e7, Kind = "float" };
}
