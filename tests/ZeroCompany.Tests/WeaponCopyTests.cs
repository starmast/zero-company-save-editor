using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>An operator whose weapon lost its mesh and variant data (a mod weapon the game could no longer load) gets a vanilla weapon back.</summary>
public class WeaponCopyTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-weapon-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    internal static GvasFile Sample()
    {
        // weapon objects are named the way the game has written them since its 1.2 update, so the early-campaign sample (an older save) is not used
        var src = Repo.CombatSaveNamed("Hub_Traits.sav");
        Skip.If(src == null, "no sample save from the current game");
        return new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
    }

    /// <summary>Strip every part of an operator's weapon of its fragments, as the game did to the reporter's mod weapons.</summary>
    internal static GvasFile Damage(GvasFile g, string guid)
    {
        for (int i = 0; ; i++)
        {
            var kids = g.Child(g.Child(MedbayOps.Characters(g)[guid], "ArchiveBytes"), "WeaponObjectWrappers")!.Children!;
            if (i >= kids.Count) return g;
            var archive = g.Child(kids[i], "ArchiveBytes")!;
            if (g.Child(archive, "FragmentInstanceWrappers") is { } frag) g = g.RemoveFromArchive(archive, frag.Start, frag.End);
        }
    }

    /// <summary>Two operators with intact weapons of one class, so one can be damaged and the other can serve as the donor.</summary>
    internal static (string Victim, string Donor) Pair(GvasFile g)
    {
        var ok = WeaponOps.Operators(g).Select(o => WeaponOps.Read(g, o)!).Where(w => !w.Broken && w.Class.Length > 0).ToList();
        foreach (var w in ok)
            foreach (var d in WeaponOps.Donors(g, w.Guid)) return (w.Guid, d);
        Skip.If(true, "the sample save has no two operators with a vanilla weapon of one class");
        return default;
    }

    [SkippableFact]
    public void Healthy_weapons_are_read_with_their_parts_and_are_not_reported_damaged()
    {
        var g = Sample();
        var weapons = WeaponOps.Operators(g).Select(o => WeaponOps.Read(g, o)!).ToList();
        Skip.If(weapons.Count == 0, "no operator has a weapon in the sample save");
        Assert.All(weapons, w => Assert.NotEmpty(w.Parts));
        Assert.All(weapons.Where(w => !w.Type.Contains("BF2_")), w => Assert.False(w.Broken, $"{w.Guid} {w.Type}"));
    }

    [SkippableFact]
    public void A_weapon_stripped_of_its_data_is_reported_and_copy_gives_a_donors_weapon_without_touching_anyone_else()
    {
        var g0 = Sample();
        var (victim, donor) = Pair(g0);
        var g = Damage(g0, victim);
        Assert.True(WeaponOps.Read(g, victim)!.Broken);

        var cut = new CutInfo();
        var (fixedFile, used) = WeaponOps.Copy(g, victim, donor, cut);
        Assert.Equal(donor, used);

        var after = WeaponOps.Read(fixedFile, victim)!;
        var donorParts = WeaponOps.Read(g0, donor)!.Parts;
        Assert.False(after.Broken);
        Assert.Equal(donorParts.Select(p => (p.Slot, p.Name, p.Fragments)), after.Parts.Select(p => (p.Slot, p.Name, p.Fragments)));
        Assert.True(WeaponOps.Holds(fixedFile, victim, cut.WeaponCopies[victim]));

        // everyone else's weapon is byte for byte what it was, and the donor still has its own
        foreach (var other in WeaponOps.Operators(g).Where(o => o != victim))
        {
            var was = WeaponOps.Read(g, other)!; var now = WeaponOps.Read(fixedFile, other)!;
            Assert.Equal(was.Parts, now.Parts);
        }
        Assert.Contains(victim, cut.WeaponCopies.Keys);
    }

    [SkippableFact]
    public void The_copy_gets_slot_objects_of_its_own_and_the_operators_slot_list_points_at_them()
    {
        var g0 = Sample();
        var (victim, donor) = Pair(g0);
        var g = Damage(g0, victim);
        string Slots(GvasFile f, string guid) => string.Join("|", f.Child(f.Child(f.Child(MedbayOps.Characters(f)[guid], "ArchiveBytes"), "CharacterData"), "WeaponCustomizationSlots")!
            .Children!.Select(n => f.GetStr(n)));
        string WeaponText(GvasFile f, string guid)
        {
            var n = f.Child(f.Child(MedbayOps.Characters(f)[guid], "ArchiveBytes"), "WeaponObjectWrappers")!;
            return System.Text.Encoding.Latin1.GetString(f.Data, n.ValueOffset, n.Size);
        }
        var (fixedFile, _) = WeaponOps.Copy(g, victim, null, new CutInfo());

        // the operator's slot list names objects the copied weapon contains, and not the donor's
        var mine = Slots(fixedFile, victim).Split('|');
        Assert.All(mine, s => Assert.Contains(s[(s.LastIndexOf('.') + 1)..], WeaponText(fixedFile, victim)));
        Assert.Empty(mine.Intersect(Slots(fixedFile, donor).Split('|')));
        Assert.Equal(Slots(g0, donor), Slots(fixedFile, donor));                                           // the donor is untouched

        // no wrapper object name occurs in two operators' weapons
        var re = new System.Text.RegularExpressions.Regex(@"CharacterCustomization(Variant|Slot)?InstanceWrapper_\d+");
        var a = re.Matches(WeaponText(fixedFile, victim)).Select(m => m.Value).ToHashSet();
        var b = re.Matches(WeaponText(fixedFile, donor)).Select(m => m.Value).ToHashSet();
        Assert.Empty(a.Intersect(b));
    }

    [SkippableFact]
    public void A_working_weapon_can_be_replaced_by_a_donors_too()
    {
        var g = Sample();
        var (target, donor) = Pair(g);
        var cut = new CutInfo();
        var (copied, used) = WeaponOps.Copy(g, target, donor, cut);
        Assert.Equal(donor, used);
        Assert.Equal(WeaponOps.Read(g, donor)!.Parts.Select(p => (p.Slot, p.Name, p.Fragments)), WeaponOps.Read(copied, target)!.Parts.Select(p => (p.Slot, p.Name, p.Fragments)));
        Assert.True(WeaponOps.Holds(copied, target, cut.WeaponCopies[target]));
        Assert.Equal(WeaponOps.Read(g, donor)!.Parts, WeaponOps.Read(copied, donor)!.Parts);                 // the donor keeps its own
    }

    [SkippableFact]
    public void Every_listed_donor_can_be_copied_including_weapons_that_name_fragments_below_their_slots()
    {
        var g = Sample();
        int copies = 0;
        foreach (var target in WeaponOps.Operators(g))
            foreach (var donor in WeaponOps.Donors(g, target).Take(1))                                  // one per operator covers every layout without minutes of copying
            {
                var cut = new CutInfo();
                var (copied, _) = WeaponOps.Copy(g, target, donor, cut);
                Assert.True(WeaponOps.Holds(copied, target, cut.WeaponCopies[target]), $"{target} from {donor}");
                Assert.Equal(WeaponOps.Read(g, donor)!.Parts, WeaponOps.Read(copied, donor)!.Parts);
                copies++;
            }
        Skip.If(copies == 0, "the sample save has no operator that can copy a weapon");
    }

    [Fact]
    public void The_editor_ships_a_plain_vanilla_generic_weapon_for_each_common_class()
    {
        var names = WeaponOps.Templates.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Longarm", "Pistol", "Repeater", "Rifle" }, names);
        foreach (var t in WeaponOps.Templates)
        {
            Assert.Equal("Blaster_" + t.Name, t.Class);
            Assert.Equal(9, t.Parts.Count);
            Assert.StartsWith("CPD_GK_", t.Kit);                              // the operator's gear kit comes with the weapon
            Assert.NotEmpty(t.KitElement);
            Assert.False(string.IsNullOrEmpty(t.KitTop));
            Assert.Equal(t.Parts.Append(t.Kit).ToList(), t.AllParts.ToList());
            Assert.Equal(t.Parts.Count, t.SlotNumbers.Count);
            Assert.StartsWith("CPD_WP_Type_", t.Type);
            Assert.True(t.Changelist > 0);
            var text = System.Text.Encoding.Latin1.GetString(t.Elements);
            var kitText = System.Text.Encoding.Latin1.GetString(t.KitElement);
            Assert.DoesNotContain("BF2_", kitText);
            Assert.DoesNotContain("/Game/ZC", kitText);
            Assert.DoesNotContain("/Game/ZC", text);                         // nothing from a mod
            Assert.DoesNotContain("_BF2_", text);
            Assert.All(t.Parts, p => Assert.DoesNotContain("BF2_", p));
            var again = WeaponOps.ReadTemplate(WeaponOps.WriteTemplate(t));   // the file format round-trips
            Assert.Equal(t.Elements, again.Elements);
            Assert.Equal(t.Parts, again.Parts);
            Assert.Equal(t.SlotNumbers, again.SlotNumbers);
            Assert.Equal(t.Kit, again.Kit);
            Assert.Equal(t.KitTop, again.KitTop);
            Assert.Equal(t.KitElement, again.KitElement);
        }
        Assert.Same(WeaponOps.Templates[0], WeaponOps.Template(WeaponOps.Templates[0].Key));
        Assert.Equal(WeaponOps.Template("rifle"), WeaponOps.Template("template:Rifle"));
        Assert.Null(WeaponOps.Template("template:Nope"));
    }

    [SkippableFact]
    public void A_generic_weapon_can_be_copied_onto_every_operator_of_its_class_and_onto_a_damaged_weapon()
    {
        var g0 = Sample();
        int copies = 0;
        foreach (var target in WeaponOps.Operators(g0))
        {
            if (WeaponOps.TemplateFor(WeaponOps.Class(g0, target)) is not { } t) continue;
            var cut = new CutInfo();
            var (copied, used) = WeaponOps.Copy(g0, target, t.Key, cut);
            Assert.Equal(t.Key, used);
            Assert.True(WeaponOps.Holds(copied, target, cut.WeaponCopies[target]), $"{target} <- {t.Name}");
            Assert.Equal(t.AllParts, WeaponOps.Read(copied, target)!.Parts.Select(p => p.Name).ToList());
            foreach (var other in WeaponOps.Operators(g0).Where(o => o != target))                          // nobody else changed
                Assert.Equal(WeaponOps.Read(g0, other)!.Parts, WeaponOps.Read(copied, other)!.Parts);
            copies++;
        }
        Skip.If(copies == 0, "no operator in the sample save has a class with a generic weapon");
    }

    [SkippableFact]
    public void With_no_donor_the_generic_weapon_of_the_operators_class_is_used_and_an_unknown_class_asks_for_a_choice()
    {
        var g0 = Sample();
        // an operator nobody else can donate to: take away every other operator's weapon wrappers of the class by copying onto a class of one
        var lone = WeaponOps.Operators(g0).FirstOrDefault(o => WeaponOps.Donors(g0, o).Count == 0 && WeaponOps.TemplateFor(WeaponOps.Class(g0, o)) != null);
        if (lone != null)
        {
            var (copied, used) = WeaponOps.Copy(g0, lone, null, new CutInfo());
            Assert.True(WeaponOps.IsTemplate(used));
            Assert.True(WeaponOps.Holds(copied, lone, WeaponOps.Template(used)!.AllParts));
        }
        var noClass = WeaponOps.Operators(g0).FirstOrDefault(o => WeaponOps.TemplateFor(WeaponOps.Class(g0, o)) == null && WeaponOps.Donors(g0, o).Count == 0);
        Skip.If(noClass == null && lone == null, "the sample save has no operator without a donor");
        if (noClass != null)
        {
            var e = Assert.Throws<GvasException>(() => WeaponOps.Copy(g0, noClass, null, new CutInfo()));
            Assert.Contains("Rifle", e.Message);                                                                // names the generic weapons to pick from
            var (copied, _) = WeaponOps.Copy(g0, noClass, "template:Rifle", new CutInfo());                    // an explicit choice always works
            Assert.Equal(WeaponOps.Template("Rifle")!.AllParts, WeaponOps.Read(copied, noClass)!.Parts.Select(p => p.Name).ToList());
        }
    }

    /// <summary>Strip the operator's gear kit of its fragments, as the game did to the mod operators in the reporter's save.</summary>
    static GvasFile DamageKit(GvasFile g, string guid)
    {
        var arch = g.Child(MedbayOps.Characters(g)[guid], "ArchiveBytes")!;
        var tag = g.Walk(new[] { arch }).First(n => n.Name == "SlotNameTag" && g.Child(n, "TagName") is { } t && g.GetStr(t).EndsWith("Weapon.GearKit"));
        var el = tag.Parent!; while (!(el.Parent != null && el.Parent.Name == "FragmentInstanceWrappers" && el.Parent.TName == "ArrayProperty")) el = el.Parent!;
        var inner = g.Child(el, "ArchiveBytes")!;
        var frag = g.Child(inner, "FragmentInstanceWrappers")!;
        return g.RemoveFromArchive(inner, frag.Start, frag.End);
    }

    [SkippableFact]
    public void A_weapon_is_damaged_when_only_the_gear_kit_lost_its_data_and_copying_gives_back_a_working_kit_that_hangs_under_the_operators_own_slot()
    {
        var g0 = Sample();
        var (victim, donor) = Pair(g0);
        var g = DamageKit(g0, victim);
        var damaged = WeaponOps.Read(g, victim)!;
        Assert.True(damaged.Broken);
        Assert.Equal(new[] { WeaponOps.KitSlot }, damaged.Stripped.Select(p => p.Slot).ToArray());          // the weapon parts are fine, the kit is not

        var (copied, _) = WeaponOps.Copy(g, victim, donor, new CutInfo());
        var after = WeaponOps.Read(copied, victim)!;
        Assert.False(after.Broken);
        Assert.Equal(WeaponOps.Read(g0, donor)!.Parts.Select(p => p.Name), after.Parts.Select(p => p.Name));
        Assert.Equal(WeaponOps.KitSlot, after.Parts[^1].Slot);

        // every object name of the kit hangs under the operator's own slot object, not the donor's
        string KitText(GvasFile f, string guid)
        {
            var arch = f.Child(MedbayOps.Characters(f)[guid], "ArchiveBytes")!;
            var tag = f.Walk(new[] { arch }).First(n => n.Name == "SlotNameTag" && f.Child(n, "TagName") is { } t && f.GetStr(t).EndsWith("Weapon.GearKit"));
            var el = tag.Parent!; while (!(el.Parent != null && el.Parent.Name == "FragmentInstanceWrappers" && el.Parent.TName == "ArrayProperty")) el = el.Parent!;
            return System.Text.Encoding.Latin1.GetString(f.Data, el.Start, el.End - el.Start);
        }
        var rx = new System.Text.RegularExpressions.Regex(@"BP_BrunoGameInstance_C_0\.BRC_CustomizationFragmentInstanceSlot_(\d+)");
        var mine = rx.Matches(KitText(g, victim)).Select(m => m.Groups[1].Value).Distinct().ToList();      // the operator's own top slot, from the stripped kit
        var now = rx.Matches(KitText(copied, victim)).Select(m => m.Groups[1].Value).Distinct().ToList();
        Skip.If(mine.Count != 1, "the stripped kit names no slot object");
        Assert.Equal(mine, now);
        Assert.NotEqual(rx.Match(KitText(copied, donor)).Groups[1].Value, now[0]);                          // and not the donor's
        Assert.Equal(KitText(g0, donor), KitText(copied, donor));                                           // the donor's own kit is untouched
    }

    [SkippableFact]
    public void Copy_refuses_a_donor_of_another_class_and_an_operator_without_a_weapon()
    {
        var g = Sample();
        var (victim, _) = Pair(g);
        var damaged = Damage(g, victim);
        var other = WeaponOps.Operators(damaged).FirstOrDefault(o => o != victim && WeaponOps.Class(damaged, o) != WeaponOps.Class(damaged, victim));
        if (other != null) Assert.Throws<GvasException>(() => WeaponOps.Copy(damaged, victim, other, new CutInfo()));
        Assert.Throws<GvasException>(() => WeaponOps.Copy(damaged, "00000000000000000000000000000000", null, new CutInfo()));
    }

    [SkippableFact]
    public void The_pending_store_turns_a_queued_copy_into_an_action_and_a_description()
    {
        var g = Sample();
        var (victim, _) = Pair(g);
        var p = new PendingChanges();
        p.SetWeaponCopy(victim, true);
        Assert.Equal(1, p.ActionCount);
        Assert.Equal(new SaveAction.CopyWeapon(victim, null), Assert.Single(p.WeaponCopies.Select(kv => new SaveAction.CopyWeapon(kv.Key, kv.Value))));
        p.SetWeaponCopy(victim, false);
        Assert.Equal(0, p.ActionCount);
    }
}
