using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// Giving the squad an item it never had (for example the reward of a Tel meditation you did not choose). Utility items and weapon
/// mods are each their own entry in StrategyData.InventoryItemData (a map from a GUID to a small struct: the same GUID again, the
/// item asset as an object path and a soft path, a count of 1, when the count last changed, who has it equipped and a recipe id),
/// and two of the same item are two entries. So adding an item appends one entry: a copy of an existing entry of the same kind,
/// with a fresh GUID and the new item's asset paths. Resources are one stack each and already have an editable count.
/// </summary>
public static class ArmoryOps
{
    const string ItemRoot = "/Game/Game/GameData/ItemData/";
    static readonly Regex NameRx = new(@"^(UtilityItem|Modification)_[A-Za-z0-9_]+$", RegexOptions.Compiled);

    /// <summary>"UtilityItem" or "Modification" for an addable item name, else null (resources and anything unrecognised).</summary>
    public static string? KindOf(string name) => NameRx.Match(name) is { Success: true } m ? m.Groups[1].Value : null;


    /// <summary>The slot a weapon mod goes in, read from its name ("Modification_Scope_LongRange_T2" is "Scope"); "" for anything else.</summary>
    public static string SlotOf(string name)
    {
        if (KindOf(name) != "Modification") return "";
        var parts = name.Split('_');
        return parts.Length >= 3 ? parts[1] : "";
    }
    /// <summary>The object path and soft path the save stores for an item, e.g. <c>.../UtilityItems/UtilityItem_X.UtilityItem_X</c> and <c>.../UtilityItems/UtilityItem_X</c>.</summary>
    public static (string Object, string Soft) ItemPaths(string name)
    {
        var kind = KindOf(name) ?? throw new GvasException($"'{name}' is not an item that can be added (only utility items and weapon mods)");
        var dir = ItemRoot + (kind == "UtilityItem" ? "UtilityItems/" : "Modifications/");
        return ($"{dir}{name}.{name}", $"{dir}{name}");
    }

    static GvasNode InventoryMap(GvasFile g) =>
        g.Child(g.StrategyDataOrNull() ?? throw new GvasException("strategy data not found"), "InventoryItemData")
        ?? throw new GvasException("the save has no inventory");

    static GvasNode? Value(GvasFile g, GvasNode entry) => entry.Children == null ? g.Child(entry, "value") : entry.Children.Count > 1 ? entry.Children[1] : null;

    /// <summary>Every inventory entry: item name, entry GUID (hex) and count.</summary>
    public static List<(string Asset, string Guid, long Count)> Entries(GvasFile g)
    {
        var list = new List<(string, string, long)>();
        foreach (var e in InventoryMap(g).Children ?? new List<GvasNode>())
        {
            var v = Value(g, e);
            if (v == null || g.Child(v, "ItemAsset") is not { } a || g.Child(v, "ItemCount") is not { } c) continue;
            list.Add((Naming_AssetName(g.GetStr(a)), StructGuid(g, e.Children![0]), g.GetInt(c)));
        }
        return list;
    }

    static string Naming_AssetName(string path)
    {
        var last = path[(path.LastIndexOf('/') + 1)..];
        var dot = last.IndexOf('.');
        return dot < 0 ? last : last[..dot];
    }

    /// <summary>An existing entry to copy: same kind, count 1, nobody has it equipped.</summary>
    static GvasNode? Template(GvasFile g, string kind)
    {
        var folder = kind == "UtilityItem" ? "/UtilityItems/" : "/Modifications/";
        foreach (var e in InventoryMap(g).Children ?? new List<GvasNode>())
        {
            var v = Value(g, e);
            if (v == null || g.Child(v, "ItemAsset") is not { } a || !g.GetStr(a).Contains(folder, StringComparison.Ordinal)) continue;
            if (g.Child(v, "ItemCount") is not { } c || g.GetInt(c) != 1) continue;
            if (g.Child(v, "EquippedCharacterIDs") is not { Size: 8 }) continue;          // an empty set: a count of 0 and nothing in it
            if (g.Child(v, "ItemId") == null || g.Child(v, "SoftItemAsset") == null) continue;
            return e;
        }
        return null;
    }

    static void SetGuid(GvasFile g, GvasNode s, int[] parts)
    {
        string[] names = { "A", "B", "C", "D" };
        for (int i = 0; i < 4; i++)
            g.Set(g.Child(s, names[i]) ?? throw new GvasException("unexpected GUID layout"), parts[i]);
    }

    /// <summary>Add one copy of <paramref name="name"/>. Returns a new file; <paramref name="cut"/> records the new entry for the verifier.</summary>
    public static GvasFile AddItem(GvasFile g, string name, CutInfo cut, Guid? guid = null)
    {
        var kind = KindOf(name) ?? throw new GvasException($"'{name}' is not an item that can be added (only utility items and weapon mods)");
        var (objectPath, softPath) = ItemPaths(name);
        var tmpl = Template(g, kind) ?? throw new GvasException($"the save has no {(kind == "UtilityItem" ? "utility item" : "weapon mod")} to copy; add one in the game first");
        var raw = g.Slice(tmpl.Start, tmpl.End);
        cut.OpaqueRemoved -= g.Walk(new[] { tmpl }).Count(n => n.Opaque);          // the copy brings its own unparsed parts (timestamp, equipped set); the verifier counts them

        var existing = Entries(g).Select(e => e.Guid).ToHashSet();
        Guid id;
        do id = guid ?? Guid.NewGuid(); while (existing.Contains(HexOf(id)) && guid == null);
        if (existing.Contains(HexOf(id))) throw new GvasException("that GUID is already in use");
        var b = id.ToByteArray();
        var parts = new[] { BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 4), BitConverter.ToInt32(b, 8), BitConverter.ToInt32(b, 12) };

        var mp = InventoryMap(g);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(mp)));
        g = g.MapAppendRaw(mp, raw);

        GvasNode Last() => InventoryMap(g).Children![^1];
        SetGuid(g, g.Child(Last(), "key") ?? Last().Children![0], parts);
        var value = Value(g, Last())!;
        SetGuid(g, g.Child(value, "ItemId")!, parts);
        g = g.SetString(g.Child(Value(g, Last())!, "ItemAsset")!, objectPath);
        g = SetSoftPath(g, g.Child(Value(g, Last())!, "SoftItemAsset")!, softPath, name);
        cut.AddedItems.Add((name, GuidHexOf(g, Last())));
        return g;
    }


    /// <summary>
    /// A SoftObjectProperty value is three FStrings in a row: the package path, the asset name and a sub-path (empty here, just a zero
    /// length). Replace it, after checking that is the layout the node really has.
    /// </summary>
    internal static GvasFile SetSoftPath(GvasFile g, GvasNode n, string packagePath, string assetName)
    {
        int len1 = BitConverter.ToInt32(g.Data, n.ValueOffset);
        int len2 = len1 >= 1 && 4 + len1 + 4 <= n.Size ? BitConverter.ToInt32(g.Data, n.ValueOffset + 4 + len1) : -1;
        if (len1 < 1 || len2 < 1 || n.Size != 4 + len1 + 4 + len2 + 4 || BitConverter.ToInt32(g.Data, n.ValueOffset + 8 + len1 + len2) != 0)
            throw new GvasException("unexpected soft object path layout");
        byte[] Str(string s) { var raw = System.Text.Encoding.ASCII.GetBytes(s + "\0"); return BitConverter.GetBytes(raw.Length).Concat(raw).ToArray(); }
        var bytes = Str(packagePath).Concat(Str(assetName)).Concat(new byte[4]).ToArray();          // the last four bytes: no sub-path
        return g.ReplaceValue(n, bytes);

    }
    static string GuidHexOf(GvasFile g, GvasNode entry) => StructGuid(g, entry.Children![0]);

    /// <summary>
    /// The GUID in a struct of four tagged IntProperty children A, B, C and D (this map's key and the value's ItemId), as 32 hex chars.
    /// The key is NOT a raw 16-byte GUID here (each part has its own property tag), so <see cref="GvasExtensions.GuidHex"/> does not apply.
    /// </summary>
    internal static string StructGuid(GvasFile g, GvasNode s) =>
        string.Concat(new[] { "A", "B", "C", "D" }.Select(n => unchecked((uint)(int)g.GetInt(g.Child(s, n) ?? throw new GvasException("unexpected GUID layout"))).ToString("X8")));

    /// <summary>Match <see cref="GvasExtensions.GuidHex"/>: the four little-endian parts as upper-case hex in A B C D order.</summary>
    static string HexOf(Guid id)
    {
        var b = id.ToByteArray();
        return string.Concat(Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt32(b, i * 4).ToString("X8")));
    }
}
