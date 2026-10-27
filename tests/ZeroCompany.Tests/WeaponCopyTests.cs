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
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
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
            foreach (var donor in WeaponOps.Donors(g, target))
            {
                var cut = new CutInfo();
                var (copied, _) = WeaponOps.Copy(g, target, donor, cut);
                Assert.True(WeaponOps.Holds(copied, target, cut.WeaponCopies[target]), $"{target} from {donor}");
                Assert.Equal(WeaponOps.Read(g, donor)!.Parts, WeaponOps.Read(copied, donor)!.Parts);
                copies++;
            }
        Skip.If(copies == 0, "the sample save has no operator that can copy a weapon");
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
