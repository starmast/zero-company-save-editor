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
        foreach (var d in g.Child(t, "DestructibleActorInfos")?.Children ?? new List<GvasNode>())
        {
            var name = g.Child(d, "DestructibleActor") is { } da ? After(g.GetStr(da), (char)46) : "";
            FieldRef? hp = null, max = null;
            foreach (var av in g.Child(g.Child(d, "AttributeSetAndValues"), "AttributeValues")?.Children ?? new List<GvasNode>())
            {
                if (g.Child(av, "Name") is not { } nm) continue;
                var n = g.GetStr(nm);
                if (n == "Health") hp = Scalar(g, g.Child(av, "BaseValue"), 0, 999);
                else if (n == "MaxHealth") max = Scalar(g, g.Child(av, "BaseValue"), 1, 999);
            }
            if (hp == null || max == null) continue;
            view.Destructibles.Add(new DestructibleView { Name = name, Title = SceneryTitle(name), Health = hp, MaxHealth = max });
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
