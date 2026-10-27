using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// An operator's weapon. It is saved as CharacterData.WeaponCustomizationSlots (one object path per part) in the operator's
/// character archive, and as <c>WeaponObjectWrappers</c> next to it: one wrapper per part (type, base, barrel, stock, scope, cell and
/// so on). A healthy part carries <c>FragmentInstanceWrappers</c> (its mesh and variant choices); a part whose game assets could
/// not be loaded when the game last saved (a weapon from a mod that stopped working after a game update) has none, and the Armory
/// then shows no weapon and greys out its menus.
/// <see cref="Copy"/> gives an operator (one with such a weapon, or any other) a copy of the weapon of another operator with the same weapon class.
/// </summary>
public static class WeaponOps
{
    /// <summary>One saved weapon part.</summary>
    public sealed record Part(string Slot, string Name, int Fragments);

    public sealed record Weapon(string Guid, string Class, IReadOnlyList<Part> Parts)
    {
        /// <summary>The part named like a weapon type ("CPD_WP_Type_Rifle_EE-3"), or "".</summary>
        public string Type => Parts.FirstOrDefault(p => p.Name.StartsWith("CPD_WP_Type_", StringComparison.Ordinal))?.Name ?? "";
        /// <summary>Parts that carry no mesh or variant data, so the game cannot build the weapon from them.</summary>
        public IReadOnlyList<Part> Stripped => Parts.Where(p => p.Fragments == 0).ToList();
        public bool Broken => Parts.Count > 0 && Stripped.Count > 0;
    }

    const string SlotObjectClass = "BRC_CustomizationFragmentInstanceSlot";
    // A slot object is a name right after the game instance ("...BP_BrunoGameInstance_C_0.BRC_CustomizationFragmentInstanceSlot_414"); its
    // fragments are named below it ("...Slot_414.BRC_CustomizationFragmentInstanceSlot_2"), with numbers that start again for every slot.
    static readonly Regex ObjectName = new(@"(CharacterCustomizationSlotInstanceWrapper|CharacterCustomizationInstanceWrapper|CharacterCustomizationVariantInstanceWrapper|(?<=BP_BrunoGameInstance_C_0\.)BRC_CustomizationFragmentInstanceSlot)_(\d+)", RegexOptions.CultureInvariant);
    static readonly Regex WeaponSpec = new(@"CPD_WeaponSpec_[A-Za-z0-9]+_[A-Za-z0-9]+", RegexOptions.CultureInvariant);

    static GvasNode? Archive(GvasFile g, string guid) =>
        MedbayOps.Characters(g).TryGetValue(guid, out var w) ? g.Child(w, "ArchiveBytes") : null;

    static GvasNode? WrappersNode(GvasFile g, string guid) => g.Child(Archive(g, guid), "WeaponObjectWrappers");

    static GvasNode? SlotsNode(GvasFile g, string guid) =>
        g.Child(g.Child(Archive(g, guid), "CharacterData"), "WeaponCustomizationSlots");

    /// <summary>Operators that have weapon wrappers (a droid or a Jedi with a lightsaber has none).</summary>
    public static List<string> Operators(GvasFile g) =>
        MedbayOps.Characters(g).Keys.Where(k => (WrappersNode(g, k)?.Children?.Count ?? 0) > 0).ToList();

    /// <summary>
    /// The weapon class the operator's specialization uses ("Blaster_Rifle", "Blaster_Pistol", "Blaster_Longarm", "Blaster_Repeater"),
    /// or "" when the save does not name one. Read from the operator's own character archive.
    /// </summary>
    public static string Class(GvasFile g, string guid)
    {
        var a = Archive(g, guid);
        if (a == null) return "";
        var m = WeaponSpec.Match(Encoding.Latin1.GetString(g.Data, a.ValueOffset, a.Size));
        return m.Success ? m.Value["CPD_WeaponSpec_".Length..] : "";
    }

    static string PartName(GvasFile g, GvasNode el)
    {
        var inst = g.Child(g.Child(g.Child(el, "ArchiveBytes"), "InstanceWrapper"), "ArchiveBytes");
        var id = g.Child(g.Child(inst, "CustomizationPartPrimaryAssetId"), "PrimaryAssetName");
        return id == null ? "" : g.GetStr(id);
    }

    static string SlotTag(GvasFile g, GvasNode el)
    {
        var inst = g.Child(g.Child(g.Child(el, "ArchiveBytes"), "InstanceWrapper"), "ArchiveBytes");
        var tag = g.Child(g.Child(inst, "SlotNameTag"), "TagName");
        return tag == null ? "" : g.GetStr(tag);
    }

    public static Weapon? Read(GvasFile g, string guid)
    {
        var kids = WrappersNode(g, guid)?.Children;
        if (kids == null || kids.Count == 0) return null;
        var parts = kids.Select(el => new Part(SlotTag(g, el), PartName(g, el),
            g.Child(g.Child(el, "ArchiveBytes"), "FragmentInstanceWrappers")?.Children?.Count ?? 0)).ToList();
        return new Weapon(guid, Class(g, guid), parts);
    }

    /// <summary>Whether the weapon's bytes mention assets of a mod (a donor must not).</summary>
    static bool UsesMod(GvasFile g, string guid)
    {
        var a = WrappersNode(g, guid)!;
        var text = Encoding.Latin1.GetString(g.Data, a.ValueOffset, a.Size);
        return text.Contains("/Game/ZC", StringComparison.Ordinal) || text.Contains("_BF2_", StringComparison.Ordinal);
    }

    static int Number(GvasFile g, GvasNode node, string cls)
    {
        var m = ObjectName.Match(g.GetStr(node));
        if (!m.Success || m.Groups[1].Value != cls) throw new GvasException("unexpected object name in the weapon data");
        return int.Parse(m.Groups[2].Value);
    }

    /// <summary>Whether every slot object the weapon's data mentions is one of the operator's own weapon slots (so nothing in the copy would point elsewhere).</summary>
    static bool OwnSlotsOnly(GvasFile g, string guid)
    {
        var w = WrappersNode(g, guid); var slots = SlotsNode(g, guid)?.Children;
        if (w == null || slots == null) return false;
        var own = slots.Select(n => ObjectName.Match(g.GetStr(n))).Where(m => m.Success).Select(m => m.Groups[2].Value).ToHashSet();
        return ObjectName.Matches(Encoding.Latin1.GetString(g.Data, w.ValueOffset, w.Size))
            .Where(m => m.Groups[1].Value == SlotObjectClass).All(m => own.Contains(m.Groups[2].Value));
    }

    /// <summary>The operators whose weapon could be copied to <paramref name="guid"/>, best first.</summary>
    public static List<string> Donors(GvasFile g, string guid)
    {
        var cls = Class(g, guid);
        var want = WrappersNode(g, guid)?.Children?.Count ?? 0;
        if (cls.Length == 0 || want == 0) return new List<string>();
        return Operators(g).Where(o => o != guid && Class(g, o) == cls
                && Read(g, o) is { Broken: false } w && w.Parts.Count == want && !UsesMod(g, o)
                && OwnSlotsOnly(g, o))
            .ToList();
    }

    /// <summary>
    /// Give <paramref name="guid"/> a copy of <paramref name="donor"/>'s weapon (or of the first suitable donor when null).
    /// Every object in the copy gets a number nothing else in the save uses, as long as the donor's own number was (so no size field
    /// inside the copy moves), and the operator's CharacterData.WeaponCustomizationSlots, the only other place that names a weapon slot
    /// object, is pointed at the copy's slot objects. Returns the new file and the donor used.
    /// </summary>
    public static (GvasFile File, string Donor) Copy(GvasFile g, string guid, string? donor, CutInfo cut)
    {
        var dest = WrappersNode(g, guid) ?? throw new GvasException("this operator has no weapon data");
        var have = Read(g, guid)!;
        var candidates = Donors(g, guid);
        if (candidates.Count == 0) throw new GvasException($"no other operator to copy a weapon from: it needs an undamaged vanilla {(have.Class.Length == 0 ? "weapon of the same class" : have.Class.Replace('_', ' ') + " weapon")} (no mod assets)");
        if (donor == null) donor = candidates[0];
        else if (!candidates.Contains(donor)) throw new GvasException("that operator's weapon cannot be copied here (different class, damaged, or from a mod)");

        var src = WrappersNode(g, donor)!;
        var dk = src.Children!; var tk = dest.Children!;
        if (dk.Count != tk.Count) throw new GvasException("the two weapons have a different number of parts");
        var dSlots = SlotsNode(g, donor)?.Children; var tSlots = SlotsNode(g, guid)?.Children;
        if (dSlots == null || tSlots == null || dSlots.Count != dk.Count || tSlots.Count != dk.Count)
            throw new GvasException("the weapon slot list does not match the weapon parts");
        var slotNumbers = dSlots.Select(n => Number(g, n, SlotObjectClass)).ToList();

        // numbers in use anywhere in the save, so a fresh one cannot collide
        var used = new Dictionary<string, HashSet<int>>();
        foreach (Match m in ObjectName.Matches(Encoding.Latin1.GetString(g.Data)))
        {
            if (!used.TryGetValue(m.Groups[1].Value, out var set)) used[m.Groups[1].Value] = set = new HashSet<int>();
            set.Add(int.Parse(m.Groups[2].Value));
        }
        var fresh = new Dictionary<(string, int), string>();

        string Rename(Match m)
        {
            string cls = m.Groups[1].Value; string digits = m.Groups[2].Value; int n = int.Parse(digits);
            if (cls == SlotObjectClass && !slotNumbers.Contains(n))
                throw new GvasException($"the weapon data refers to an object outside the weapon's own parts ({m.Value})");
            if (!fresh.TryGetValue((cls, n), out var to))
            {
                var set = used[cls];
                int lo = digits.Length == 1 ? 0 : (int)Math.Pow(10, digits.Length - 1), hi = (int)Math.Pow(10, digits.Length) - 1;
                int pick = Math.Max(lo, Math.Min(hi, set.Max() + 1));
                while (pick <= hi && set.Contains(pick)) pick++;
                if (pick > hi) { pick = lo; while (pick <= hi && set.Contains(pick)) pick++; }
                if (pick > hi) throw new GvasException("no free object number of the same length in this save");
                set.Add(pick);
                fresh[(cls, n)] = to = pick.ToString(new string('0', digits.Length));
            }
            return cls + "_" + to;
        }

        var from = dk[0].Start; var end = dk[^1].End;
        if (from != src.ValueOffset + 4 || end != src.End || tk[0].Start != dest.ValueOffset + 4 || tk[^1].End != dest.End)
            throw new GvasException("unexpected layout of the weapon data");
        var copy = Encoding.Latin1.GetBytes(ObjectName.Replace(Encoding.Latin1.GetString(g.Data, from, end - from), Rename));
        if (copy.Length != end - from) throw new GvasException("the rewritten weapon changed size");
        var newSlots = slotNumbers.Select(n => fresh.TryGetValue((SlotObjectClass, n), out var t) ? t : throw new GvasException("a weapon part has no slot object")).ToList();

        var value = new byte[4 + copy.Length];
        BitConverter.TryWriteBytes(value.AsSpan(0, 4), dk.Count);
        copy.CopyTo(value, 4);

        cut.OpaqueRemoved -= g.Walk(dk).Count(n => n.Opaque);
        cut.OpaqueRemoved += g.Walk(tk).Count(n => n.Opaque);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(dest)));
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(SlotsNode(g, guid))));
        var names = Read(g, donor)!.Parts.Select(p => p.Name).ToList();
        var ng = g.ReplaceValue(dest, value);

        // point the operator's slot list at the copy's slot objects (each edit re-reads the file, so find the node again)
        for (int i = 0; i < newSlots.Count; i++)
        {
            var node = SlotsNode(ng, guid)!.Children![i];
            var text = ng.GetStr(node);
            var m = ObjectName.Match(text);
            var want = text[..m.Groups[2].Index] + newSlots[i] + text[(m.Groups[2].Index + m.Groups[2].Length)..];
            if (want != text) ng = ng.SetString(node, want);
        }
        cut.WeaponCopies[guid] = names;
        return (ng, donor);
    }

    /// <summary>Whether the operator's weapon has exactly these parts and every part carries its mesh and variant data.</summary>
    public static bool Holds(GvasFile g, string guid, IReadOnlyList<string> names) =>
        Read(g, guid) is { Broken: false } w && w.Parts.Select(p => p.Name).SequenceEqual(names) && OwnSlotsOnly(g, guid);
}
