using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Healing injured operators. An injury is a persisted gameplay effect (<c>GE_Injured</c>) in the operator's saved
/// effect list. Comparing a game-written save before and after using the bacta tank shows that the heal removes that one
/// array element; the credits, tank charge and history entry are bookkeeping the game adds around it. We perform only
/// the removal (a free, instant heal).
/// </summary>
public static class MedbayOps
{
    public const string Injured = "GE_Injured";
    public const string Effects = "GameplayEffectsToPersist";

    static GvasNode? Blob(GvasFile g)
    {
        var top = g.GameInstance();
        var cdw = top == null ? null : g.Child(g.Child(top, "ArchiveBytes"), "CharacterDataWrapper");
        return cdw == null ? null : g.Child(cdw, "ArchiveBytes");
    }

    /// <summary>GUID -> that character's ObjectWrappers element (whose ArchiveBytes holds CharacterData).</summary>
    public static Dictionary<string, GvasNode> Characters(GvasFile g)
    {
        var @out = new Dictionary<string, GvasNode>();
        var blob = Blob(g);
        if (blob == null) return @out;
        var gl = g.Child(blob, "CharacterGuids")?.Children ?? new List<GvasNode>();
        var wl = g.Child(blob, "ObjectWrappers")?.Children ?? new List<GvasNode>();
        for (int i = 0; i < Math.Min(gl.Count, wl.Count); i++) @out[g.GuidHex(gl[i])] = wl[i];
        return @out;
    }

    /// <summary>
    /// (byte count of the whole character archive, GUID -> byte count of that character's archive). SaveGameMetaData.json
    /// mirrors these as <c>characterDataWrapperSize</c> and each character's <c>wrappedSize</c>.
    /// </summary>
    public static (int Total, Dictionary<string, int> PerCharacter) CharacterSizes(GvasFile g)
    {
        var blob = Blob(g);
        if (blob == null) return (0, new Dictionary<string, int>());
        return (blob.Count ?? 0, Characters(g).ToDictionary(kv => kv.Key, kv => g.Child(kv.Value, "ArchiveBytes")?.Count ?? 0));
    }

    public static GvasNode? EffectsNode(GvasFile g, string guid)
    {
        if (!Characters(g).TryGetValue(guid, out var w)) return null;
        var inner = g.Child(w, "ArchiveBytes");
        var cd = inner == null ? null : g.Child(inner, "CharacterData");
        return cd == null ? null : g.Child(cd, Effects);
    }

    public static string EffectName(GvasFile g, GvasNode el)
    {
        var d = g.Child(el, "Def");
        return d != null ? Naming.AssetName(g.GetStr(d)) : "";
    }

    /// <summary>Stable per-element keys ("GE_Name#occurrence"), so a removal does not shift every later element's path.</summary>
    public static List<string> EffectKeys(GvasFile g, GvasNode arr)
    {
        var seen = new Dictionary<string, int>();
        var @out = new List<string>();
        var kids = arr.Children ?? new List<GvasNode>();
        for (int i = 0; i < kids.Count; i++)
        {
            var n = EffectName(g, kids[i]);
            if (n.Length == 0) n = $"[{i}]";
            seen.TryGetValue(n, out var c);
            @out.Add($"{n}#{c}");
            seen[n] = c + 1;
        }
        return @out;
    }

    public static int InjuryCount(GvasFile g, string guid)
    {
        var arr = EffectsNode(g, guid);
        return arr == null ? 0 : (arr.Children ?? new List<GvasNode>()).Count(el => EffectName(g, el).StartsWith(Injured, StringComparison.Ordinal));
    }

    /// <summary>Remove every <c>GE_Injured</c> effect from one operator.</summary>
    public static GvasFile HealOperator(GvasFile g, string guid, CutInfo cut, ISet<string> touched)
    {
        int removed = 0;
        while (true)
        {
            var arr = EffectsNode(g, guid) ?? throw new GvasException("operator not found");
            var keys = EffectKeys(g, arr);
            var kids = arr.Children ?? new List<GvasNode>();
            int hit = kids.FindIndex(el => EffectName(g, el).StartsWith(Injured, StringComparison.Ordinal));
            if (hit < 0) break;
            cut.OpaqueRemoved += g.Walk(new[] { kids[hit] }).Count(n => n.Opaque);
            var baseKey = GvasFile.PathOf(arr);
            cut.Prefixes.Add(PathKey.Of(baseKey, keys[hit]));
            touched.Add(PathKey.Of(baseKey, "#count"));
            g = g.ArrayRemoveElement(arr, hit);
            removed++;
        }
        if (removed == 0) throw new GvasException("operator is not injured");
        return g;
    }
}
