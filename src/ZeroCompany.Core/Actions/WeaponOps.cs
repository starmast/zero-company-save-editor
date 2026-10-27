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
/// The character also keeps a <b>gear kit</b> (<c>br.Customization.Slot.Character.Specializations.Weapon.GearKit</c>, "CPD_GK_Rifle_Auromae"):
/// the part that tells the game which weapon the operator carries. A mod's gear kit loses its fragments the same way, and until it is
/// replaced too the game still shows no weapon, so <see cref="Read"/> lists it as a last part (slot "GearKit") and <see cref="Copy"/> copies it with the weapon.
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
    static readonly Regex Wrappers = new(@"(CharacterCustomizationSlotInstanceWrapper|CharacterCustomizationInstanceWrapper|CharacterCustomizationVariantInstanceWrapper)_(\d+)", RegexOptions.CultureInvariant);
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

    public const string KitSlot = "GearKit";
    const string KitTag = "br.Customization.Slot.Character.Specializations.Weapon.GearKit";
    static readonly Regex KitTop = new(@"BP_BrunoGameInstance_C_0\.BRC_CustomizationFragmentInstanceSlot_(\d+)", RegexOptions.CultureInvariant);

    /// <summary>The operator's gear kit: the element of a FragmentInstanceWrappers array in their character data whose slot is the weapon gear kit slot.</summary>
    static GvasNode? KitElement(GvasFile g, string guid)
    {
        var arch = Archive(g, guid);
        if (arch == null) return null;
        foreach (var n in g.Walk(new[] { arch }))
        {
            if (n.Name != "SlotNameTag" || g.Child(n, "TagName") is not { } tag || g.GetStr(tag) != KitTag) continue;
            var el = n.Parent;
            while (el != null && !(el.Parent != null && el.Parent.Name == "FragmentInstanceWrappers" && el.Parent.TName == "ArrayProperty")) el = el.Parent;
            return el;
        }
        return null;
    }

    static int KitFragments(GvasFile g, GvasNode el) =>
        g.Child(g.Child(el, "ArchiveBytes"), "FragmentInstanceWrappers")?.Children?.Count ?? 0;

    /// <summary>The number of the operator's own top-level slot object the gear kit's object names hang under ("...BP_BrunoGameInstance_C_0.BRC_CustomizationFragmentInstanceSlot_411.").</summary>
    static string? KitTopOf(string text) => KitTop.Match(text) is { Success: true } m ? m.Groups[1].Value : null;

    public static Weapon? Read(GvasFile g, string guid)
    {
        var kids = WrappersNode(g, guid)?.Children;
        if (kids == null || kids.Count == 0) return null;
        var parts = kids.Select(el => new Part(SlotTag(g, el), PartName(g, el),
            g.Child(g.Child(el, "ArchiveBytes"), "FragmentInstanceWrappers")?.Children?.Count ?? 0)).ToList();
        if (KitElement(g, guid) is { } kit) parts.Add(new Part(KitSlot, PartName(g, kit), KitFragments(g, kit)));
        return new Weapon(guid, Class(g, guid), parts);
    }

    /// <summary>Asset folders under /Game/ that the game itself uses besides /Game/Game/ (its Wwise audio switches, for one).</summary>
    public static readonly IReadOnlySet<string> GameRoots = new HashSet<string>(StringComparer.Ordinal) { "Game", "WwiseAudio" };

    /// <summary>Whether an asset path or part name comes from a mod: an asset folder the game does not use, or a part named like a mod's ("..._BF2_...").</summary>
    public static bool IsMod(string s)
    {
        if (s.Contains("_BF2_", StringComparison.Ordinal)) return true;
        if (!s.StartsWith("/Game/", StringComparison.Ordinal)) return false;
        var root = s.AsSpan(6); int slash = root.IndexOf('/');
        return !GameRoots.Contains((slash < 0 ? root : root[..slash]).ToString());
    }

    /// <summary>
    /// Whether the weapon's parts come from a mod: a part name or an asset path outside the game's own. The lists of meshes a part could
    /// be swapped for (<c>SkeletalMeshOptions</c>) are left out: a mod adds its meshes to every weapon's list, and they do no harm there.
    /// </summary>
    static bool UsesMod(GvasFile g, string guid)
    {
        var w = WrappersNode(g, guid)!;
        var roots = w.Children!.AsEnumerable();
        if (KitElement(g, guid) is { } kit) roots = roots.Append(kit);
        foreach (var n in g.Walk(roots))
            if (n.Name != "SkeletalMesh" && GvasFile.StringTypes.Contains(n.TName) && IsMod(g.GetStr(n))) return true;
        return false;
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
        var want = Read(g, guid)?.Parts.Count ?? 0;
        if (cls.Length == 0 || want == 0) return new List<string>();
        return Operators(g).Where(o => o != guid && Class(g, o) == cls
                && Read(g, o) is { Broken: false } w && w.Parts.Count == want && !UsesMod(g, o)
                && OwnSlotsOnly(g, o))
            .ToList();
    }

    // ---- bundled generic weapons ----------------------------------------------------------------------

    /// <summary>
    /// A plain vanilla weapon of one class, captured from a save with <see cref="Capture"/> and shipped inside the editor, for an operator
    /// no other operator in the save can serve. <see cref="Elements"/> is the saved bytes of the weapon's wrappers and <see cref="KitElement"/> the
    /// operator's gear kit (<see cref="Kit"/> is its part name, <see cref="KitTop"/> the number of the slot object its names hang under); the
    /// object names in them are renamed when they are copied, like a donor's.
    /// </summary>
    public sealed record WeaponTemplate(string Name, string Class, string Type, IReadOnlyList<string> Parts, IReadOnlyList<int> SlotNumbers, int Changelist, byte[] Elements,
        string Kit, string KitTop, byte[] KitElement)
    {
        /// <summary>The part names the weapon has once copied, the gear kit last.</summary>
        public IReadOnlyList<string> AllParts => Kit.Length > 0 ? Parts.Append(Kit).ToList() : Parts;
        public string Key => "template:" + Name;
        public string Label => $"Generic {Name} weapon ({Type.Replace("CPD_WP_Type_", "")})";
    }

    const string TemplatePrefix = "template:";
    static readonly byte[] Magic = "ZCW2"u8.ToArray();

    public static bool IsTemplate(string? source) => source != null && source.StartsWith(TemplatePrefix, StringComparison.Ordinal);

    /// <summary>The weapons shipped with the editor, one per class.</summary>
    public static IReadOnlyList<WeaponTemplate> Templates => _templates.Value;
    static readonly Lazy<IReadOnlyList<WeaponTemplate>> _templates = new(LoadTemplates);

    public static WeaponTemplate? Template(string key)
    {
        var name = IsTemplate(key) ? key[TemplatePrefix.Length..] : key;
        return Templates.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The template whose class is <paramref name="cls"/> ("Blaster_Rifle"), if the editor ships one.</summary>
    public static WeaponTemplate? TemplateFor(string cls) => Templates.FirstOrDefault(t => t.Class == cls);

    static IReadOnlyList<WeaponTemplate> LoadTemplates()
    {
        var asm = typeof(WeaponOps).Assembly;
        var list = new List<WeaponTemplate>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("weapons/", StringComparison.Ordinal) && n.EndsWith(".zcw", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream(); s.CopyTo(ms);
            list.Add(ReadTemplate(ms.ToArray()));
        }
        return list;
    }

    sealed record Manifest(string Name, string Class, string Type, List<string> Parts, List<int> SlotNumbers, int Changelist, string Kit, string KitTop, int ElementsLength);

    public static byte[] WriteTemplate(WeaponTemplate t)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Manifest(t.Name, t.Class, t.Type, t.Parts.ToList(), t.SlotNumbers.ToList(), t.Changelist, t.Kit, t.KitTop, t.Elements.Length));
        using var raw = new MemoryStream();
        raw.Write(BitConverter.GetBytes(json.Length)); raw.Write(json); raw.Write(t.Elements); raw.Write(t.KitElement);
        using var @out = new MemoryStream();
        @out.Write(Magic);
        using (var z = new System.IO.Compression.DeflateStream(@out, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw.ToArray());
        return @out.ToArray();
    }

    public static WeaponTemplate ReadTemplate(byte[] file)
    {
        if (file.Length < 8 || !file.AsSpan(0, 4).SequenceEqual(Magic)) throw new GvasException("not a weapon template");
        using var z = new System.IO.Compression.DeflateStream(new MemoryStream(file, 4, file.Length - 4), System.IO.Compression.CompressionMode.Decompress);
        using var all = new MemoryStream(); z.CopyTo(all);
        var b = all.ToArray();
        int n = BitConverter.ToInt32(b, 0);
        var m = System.Text.Json.JsonSerializer.Deserialize<Manifest>(b.AsSpan(4, n)) ?? throw new GvasException("bad weapon template");
        return new WeaponTemplate(m.Name, m.Class, m.Type, m.Parts, m.SlotNumbers, m.Changelist, b[(4 + n)..(4 + n + m.ElementsLength)], m.Kit, m.KitTop, b[(4 + n + m.ElementsLength)..]);
    }

    /// <summary>
    /// Capture <paramref name="guid"/>'s weapon as a template (a developer tool: the editor ships the results). The weapon has to be the
    /// game's own and undamaged, and nothing in it may name an operator. Meshes a mod added to the part lists are cut off the end of each list
    /// (they come after the game's own, so no saved choice moves).
    /// </summary>
    public static WeaponTemplate Capture(GvasFile g, string guid, string name, int changelist)
    {
        var w = Read(g, guid) ?? throw new GvasException("this operator has no weapon");
        if (w.Broken) throw new GvasException("this weapon is damaged");
        if (UsesMod(g, guid)) throw new GvasException("this weapon uses mod parts");
        if (w.Class.Length == 0) throw new GvasException("the operator's weapon class is not in the save");

        // cut mod meshes off the end of each option list
        while (true)
        {
            var node = WrappersNode(g, guid)!;
            GvasNode? list = null; int cut = -1;
            foreach (var n in g.Walk(node.Children!).Where(n => n.Name == "SkeletalMeshOptions" && n.Children is { Count: > 0 }))
            {
                var mods = n.Children!.Select((el, i) => (el, i)).Where(x => g.Child(x.el, "SkeletalMesh") is { } sm && IsMod(g.GetStr(sm))).Select(x => x.i).ToList();
                if (mods.Count == 0) continue;
                if (mods[^1] - mods[0] != mods.Count - 1 || mods[^1] != n.Children.Count - 1) throw new GvasException("mod meshes are not at the end of a part list");
                var sel = g.Child(n.Parent, "SelectedIndex");                       // not saved when it is 0
                if (sel != null && g.GetInt(sel) >= mods[0]) throw new GvasException("a part uses a mod mesh");
                list = n; cut = n.Children.Count - 1; break;
            }
            if (list == null) break;
            g = g.ArrayRemoveElement(list, cut);
        }

        var wrappers = WrappersNode(g, guid)!;
        var kids = wrappers.Children!;
        var slots = SlotsNode(g, guid)?.Children ?? throw new GvasException("the weapon slot list is missing");
        if (slots.Count != kids.Count) throw new GvasException("the weapon slot list does not match the weapon parts");
        if (!OwnSlotsOnly(g, guid)) throw new GvasException("the weapon data refers to objects that are not its own slots");
        var elements = g.Slice(kids[0].Start, kids[^1].End);
        var kit = KitElement(g, guid);
        byte[] kitBytes = Array.Empty<byte>(); string kitTop = "", kitName = "";
        if (kit != null)
        {
            kitBytes = g.Slice(kit.Start, kit.End);
            kitName = PartName(g, kit);
            kitTop = KitTopOf(Encoding.Latin1.GetString(kitBytes)) ?? throw new GvasException("the gear kit names no slot object");
        }
        foreach (var other in MedbayOps.Characters(g).Keys)
        {
            var raw = new byte[16];
            for (int i = 0; i < 4; i++) BitConverter.GetBytes(Convert.ToUInt32(other.Substring(i * 8, 8), 16)).CopyTo(raw, i * 4);
            if (elements.AsSpan().IndexOf(raw) >= 0 || kitBytes.AsSpan().IndexOf(raw) >= 0) throw new GvasException("the weapon data names an operator");
        }
        var read = Read(g, guid)!;
        return new WeaponTemplate(name, w.Class, w.Type, read.Parts.Where(p => p.Slot != KitSlot).Select(p => p.Name).ToList(),
            slots.Select(n => Number(g, n, SlotObjectClass)).ToList(), changelist, elements, kitName, kitTop, kitBytes);
    }

    // ---- copying ---------------------------------------------------------------------------------------

    /// <summary>
    /// Give <paramref name="guid"/> a copy of another operator's weapon, or of a bundled template. <paramref name="source"/> is a donor's GUID, a
    /// template key ("template:Rifle"), or null to take the first suitable donor and, when there is none, the template for the operator's
    /// own class. Returns the new file and what was used (a donor GUID or a template key).
    /// </summary>
    public static (GvasFile File, string Source) Copy(GvasFile g, string guid, string? source, CutInfo cut)
    {
        var dest = WrappersNode(g, guid) ?? throw new GvasException("this operator has no weapon data");
        var have = Read(g, guid)!;
        var candidates = Donors(g, guid);
        if (source == null)
        {
            if (candidates.Count > 0) source = candidates[0];
            else source = TemplateFor(have.Class)?.Key
                ?? throw new GvasException($"no other operator to copy a weapon from, and no generic weapon for {(have.Class.Length == 0 ? "this operator's class" : have.Class.Replace('_', ' '))}: pick one of the generic weapons ({string.Join(", ", Templates.Select(t => t.Name))})");
        }

        if (IsTemplate(source))
        {
            var t = Template(source) ?? throw new GvasException($"there is no generic weapon '{source[TemplatePrefix.Length..]}'");
            return (Place(g, guid, t.Elements, t.Parts.Count, t.SlotNumbers, t.AllParts, t.KitElement, t.KitTop, cut), t.Key);
        }
        if (!candidates.Contains(source)) throw new GvasException("that operator's weapon cannot be copied here (different class, damaged, or from a mod)");

        var src = WrappersNode(g, source)!;
        var dk = src.Children!;
        var dSlots = SlotsNode(g, source)?.Children;
        if (dSlots == null || dSlots.Count != dk.Count) throw new GvasException("the weapon slot list does not match the weapon parts");
        if (dk[0].Start != src.ValueOffset + 4 || dk[^1].End != src.End) throw new GvasException("unexpected layout of the weapon data");
        var parts = Read(g, source)!.Parts.Select(p => p.Name).ToList();
        var dKit = KitElement(g, source);
        var kitBytes = dKit == null ? Array.Empty<byte>() : g.Slice(dKit.Start, dKit.End);
        var kitTop = dKit == null ? "" : KitTopOf(Encoding.Latin1.GetString(kitBytes)) ?? throw new GvasException("the donor's gear kit names no slot object");
        return (Place(g, guid, g.Slice(dk[0].Start, dk[^1].End), dk.Count, dSlots.Select(n => Number(g, n, SlotObjectClass)).ToList(), parts, kitBytes, kitTop, cut), source);
    }

    /// <summary>
    /// Replace the operator's weapon wrappers with <paramref name="elements"/> (count <paramref name="count"/>). Every object in the copy gets a
    /// number nothing else in the save uses, as long as its own was (so no size field inside the copy moves), and the operator's
    /// CharacterData.WeaponCustomizationSlots, the only other place that names a weapon slot object, is pointed at the copy's slot objects.
    /// </summary>
    static GvasFile Place(GvasFile g, string guid, byte[] elements, int count, IReadOnlyList<int> slotNumbers, IReadOnlyList<string> parts, byte[] kitBytes, string kitTop, CutInfo cut)
    {
        var dest = WrappersNode(g, guid)!;
        var tk = dest.Children!;
        if (count != tk.Count) throw new GvasException("the two weapons have a different number of parts");
        var tSlots = SlotsNode(g, guid)?.Children;
        if (tSlots == null || tSlots.Count != count || slotNumbers.Count != count) throw new GvasException("the weapon slot list does not match the weapon parts");
        if (tk[0].Start != dest.ValueOffset + 4 || tk[^1].End != dest.End) throw new GvasException("unexpected layout of the weapon data");

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
                if (!used.TryGetValue(cls, out var set)) used[cls] = set = new HashSet<int>();
                int lo = digits.Length == 1 ? 0 : (int)Math.Pow(10, digits.Length - 1), hi = (int)Math.Pow(10, digits.Length) - 1;
                int pick = Math.Max(lo, Math.Min(hi, (set.Count == 0 ? 0 : set.Max()) + 1));
                while (pick <= hi && set.Contains(pick)) pick++;
                if (pick > hi) { pick = lo; while (pick <= hi && set.Contains(pick)) pick++; }
                if (pick > hi) throw new GvasException("no free object number of the same length in this save");
                set.Add(pick);
                fresh[(cls, n)] = to = pick.ToString(new string('0', digits.Length));
            }
            return cls + "_" + to;
        }

        var copy = Encoding.Latin1.GetBytes(ObjectName.Replace(Encoding.Latin1.GetString(elements), Rename));
        if (copy.Length != elements.Length) throw new GvasException("the rewritten weapon changed size");
        var newSlots = slotNumbers.Select(n => fresh.TryGetValue((SlotObjectClass, n), out var t) ? t : throw new GvasException("a weapon part has no slot object")).ToList();

        var value = new byte[4 + copy.Length];
        BitConverter.TryWriteBytes(value.AsSpan(0, 4), count);
        copy.CopyTo(value, 4);

        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(dest)));
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(SlotsNode(g, guid))));
        var ng = g.ReplaceValue(dest, value);
        cut.OpaqueRemoved += g.OpaqueCount - ng.OpaqueCount;                        // the old weapon's raw nodes go and the copy's come in

        // point the operator's slot list at the copy's slot objects (each edit re-reads the file, so find the node again)
        for (int i = 0; i < newSlots.Count; i++)
        {
            var node = SlotsNode(ng, guid)!.Children![i];
            var text = ng.GetStr(node);
            var m = ObjectName.Match(text);
            var want = text[..m.Groups[2].Index] + newSlots[i] + text[(m.Groups[2].Index + m.Groups[2].Length)..];
            if (want != text) ng = ng.SetString(node, want);
        }
        if (kitBytes.Length > 0 && KitElement(ng, guid) != null) ng = PlaceKit(ng, guid, kitBytes, kitTop, cut);
        cut.WeaponCopies[guid] = parts.ToList();
        return ng;
    }

    /// <summary>
    /// Replace the operator's gear kit with the donor's (<paramref name="kitBytes"/>, whose object names hang under the slot object
    /// <paramref name="kitTop"/>). The wrapper objects in it get unused numbers of their own length, like the weapon's; the names under the
    /// donor's slot object are then rewritten to the operator's own slot object (a different length, so each string is rewritten with its
    /// enclosing sizes).
    /// </summary>
    static GvasFile PlaceKit(GvasFile g, string guid, byte[] kitBytes, string kitTop, CutInfo cut)
    {
        var kit = KitElement(g, guid)!;
        var myTop = KitTopOf(Encoding.Latin1.GetString(g.Data, kit.Start, kit.End - kit.Start))
                    ?? throw new GvasException("the operator's gear kit names no slot object");

        var used = new Dictionary<string, HashSet<int>>();
        foreach (Match m in Wrappers.Matches(Encoding.Latin1.GetString(g.Data)))
        {
            if (!used.TryGetValue(m.Groups[1].Value, out var set)) used[m.Groups[1].Value] = set = new HashSet<int>();
            set.Add(int.Parse(m.Groups[2].Value));
        }
        var fresh = new Dictionary<(string, int), string>();
        string Rename(Match m)
        {
            string cls = m.Groups[1].Value; string digits = m.Groups[2].Value; int n = int.Parse(digits);
            if (!fresh.TryGetValue((cls, n), out var to))
            {
                if (!used.TryGetValue(cls, out var set)) used[cls] = set = new HashSet<int>();
                int lo = digits.Length == 1 ? 0 : (int)Math.Pow(10, digits.Length - 1), hi = (int)Math.Pow(10, digits.Length) - 1;
                int pick = Math.Max(lo, Math.Min(hi, (set.Count == 0 ? 0 : set.Max()) + 1));
                while (pick <= hi && set.Contains(pick)) pick++;
                if (pick > hi) { pick = lo; while (pick <= hi && set.Contains(pick)) pick++; }
                if (pick > hi) throw new GvasException("no free object number of the same length in this save");
                set.Add(pick);
                fresh[(cls, n)] = to = pick.ToString(new string('0', digits.Length));
            }
            return cls + "_" + to;
        }
        var copy = Encoding.Latin1.GetBytes(Wrappers.Replace(Encoding.Latin1.GetString(kitBytes), Rename));
        if (copy.Length != kitBytes.Length) throw new GvasException("the rewritten gear kit changed size");

        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(kit)));
        var ng = g.ArrayReplaceElement(kit.Parent!, kit.Parent!.Children!.IndexOf(kit), copy);
        cut.OpaqueRemoved += g.OpaqueCount - ng.OpaqueCount;

        // names under the donor's slot object now go under the operator's
        if (kitTop != myTop)
        {
            var from = $"BP_BrunoGameInstance_C_0.BRC_CustomizationFragmentInstanceSlot_{kitTop}";
            var to = $"BP_BrunoGameInstance_C_0.BRC_CustomizationFragmentInstanceSlot_{myTop}";
            static bool Names(string text, string from) => Regex.IsMatch(text, Regex.Escape(from) + @"(?!\d)");
            for (int guard = 0; ; guard++)
            {
                if (guard > 500) throw new GvasException("the gear kit still names the donor's slot object");
                var el = KitElement(ng, guid)!;
                var node = ng.Walk(new[] { el }).FirstOrDefault(n => GvasFile.StringTypes.Contains(n.TName) && Names(ng.GetStr(n), from));
                if (node == null) break;
                ng = ng.SetString(node, Regex.Replace(ng.GetStr(node), Regex.Escape(from) + @"(?!\d)", to));
            }
        }
        return ng;
    }

    /// <summary>Whether the operator's weapon has exactly these parts and every part carries its mesh and variant data.</summary>
    public static bool Holds(GvasFile g, string guid, IReadOnlyList<string> names) =>
        Read(g, guid) is { Broken: false } w && w.Parts.Select(p => p.Name).SequenceEqual(names) && OwnSlotsOnly(g, guid);
}
