using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Sdk;

/// <summary>
/// What in a save points at mods, for <see cref="DebugReport"/>: asset folders outside the game's own, the weapon types in use, and
/// weapons the game could no longer build (a mod weapon that stopped loading leaves its parts without mesh and variant data).
/// Counts and asset names only, never operator names or GUIDs.
/// </summary>
public static class ModChecks
{
    // the game's own assets live under /Game/Game/; anything else under /Game/ comes from a mod
    static readonly Regex ModFolder = new(@"(?<!/Game)/Game/(?!Game/)([A-Za-z0-9_\-]+)/", RegexOptions.CultureInvariant);

    public static List<Check> Run(string savePath) => Run(new GvasFile(SaveContainer.Load(File.ReadAllBytes(savePath)).Gvas));

    public static List<Check> Run(GvasFile g)
    {
        var @out = new List<Check>();

        var folders = ModFolder.Matches(Encoding.Latin1.GetString(g.Data)).GroupBy(m => m.Groups[1].Value).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        @out.Add(new Check("mod content", CheckStatus.Ok, folders.Count == 0 ? "no asset folders outside the game's own"
            : $"{folders.Count} asset folder{(folders.Count == 1 ? "" : "s")} outside the game's own: " + string.Join(", ", folders.Take(8).Select(f => $"/Game/{f.Key} ({f.Count()} references)")) + (folders.Count > 8 ? ", ..." : "")));

        var weapons = WeaponOps.Operators(g).Select(o => WeaponOps.Read(g, o)!).ToList();
        if (weapons.Count == 0) { @out.Add(new Check("weapons", CheckStatus.Ok, "no operator weapons found")); return @out; }

        var types = string.Join(", ", weapons.GroupBy(w => w.Type.Replace("CPD_WP_Type_", "")).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Count() == 1 ? x.Key : $"{x.Key} x{x.Count()}"));
        var damaged = weapons.Where(w => w.Broken).ToList();
        if (damaged.Count == 0)
        {
            @out.Add(new Check("weapons", CheckStatus.Ok, $"{weapons.Count} weapons, none damaged ({types})"));
            return @out;
        }
        @out.Add(new Check("weapons", CheckStatus.Warn,
            $"{damaged.Count} of {weapons.Count} weapons have parts with no mesh or variant data, so the game cannot build them (the Armory shows no weapon and greys out its menus): "
            + string.Join("; ", damaged.Select(w => $"{w.Type} {w.Stripped.Count} of {w.Parts.Count} parts [{string.Join(", ", w.Stripped.Select(p => p.Slot.Replace("br.Customization.Slot.Weapon.", "")))}]"))
            + $". All weapon types: {types}",
            "Weapons screen > Repair weapon, or: zc edit do <save> repair-weapon <operator>; if the weapon comes from a mod, update it for this game version first"));
        return @out;
    }
}
