using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>Moving gear between slots: an operator's slot and the item's equipped set have to change together.</summary>
public class LoadoutOpsTests
{
    static GvasFile Sample()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        return new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
    }

    /// <summary>A held utility item with its operator and slot, and an operator with a free utility slot (both passing <paramref name="usable"/>, when given).</summary>
    static (string Holder, int HolderSlot, string Item, string Free, int FreeSlot) Pick(GvasFile g, Func<string, bool>? usable = null)
    {
        var ops = MedbayOps.Characters(g).Keys.Where(o => usable?.Invoke(o) ?? true).ToList();
        foreach (var h in ops)
            foreach (var s in LoadoutOps.Slots(g, h, GearOps.Utility).Where(s => s.Item != null))
                foreach (var f in ops.Where(o => o != h))
                    if (LoadoutOps.Slots(g, f, GearOps.Utility).FirstOrDefault(x => x.Item == null) is { } free)
                        return (h, s.Index, s.Item!, f, free.Index);
        Skip.If(true, "the sample save has no utility item held by one operator and a free slot on another");
        return default;
    }

    [SkippableFact]
    public void The_save_starts_with_every_slot_and_equipped_set_agreeing()
    {
        var g = Sample();
        Assert.True(LoadoutOps.Consistent(g, LoadoutOps.AllItems(g)));
    }

    [SkippableFact]
    public void Equipping_a_held_item_elsewhere_takes_it_from_its_holder_and_keeps_both_sides_in_step()
    {
        var g = Sample();
        var (holder, hs, item, free, fs) = Pick(g);
        var cut = new CutInfo();
        var ng = LoadoutOps.Equip(g, free, GearOps.Utility, fs, item, cut);

        Assert.True(LoadoutOps.Holds(ng, free, GearOps.Utility, fs, item));
        Assert.Null(LoadoutOps.Slots(ng, holder, GearOps.Utility)[hs].Item);                 // taken from the old holder
        Assert.Equal(new[] { free }, LoadoutOps.Holders(ng, item));
        Assert.True(LoadoutOps.Consistent(ng, LoadoutOps.AllItems(ng)));
        Assert.Equal(cut.Equipped.Single(), (free, GearOps.Utility, fs, (string?)item));

        // nobody else's slots changed
        foreach (var op in MedbayOps.Characters(g).Keys.Where(o => o != holder && o != free))
            Assert.Equal(LoadoutOps.Slots(g, op, GearOps.Utility), LoadoutOps.Slots(ng, op, GearOps.Utility));
    }

    [SkippableFact]
    public void Emptying_a_slot_removes_the_operator_from_the_items_set_and_equipping_again_restores_it()
    {
        var g = Sample();
        var (holder, hs, item, _, _) = Pick(g);
        var emptied = LoadoutOps.Equip(g, holder, GearOps.Utility, hs, null, new CutInfo());
        Assert.True(LoadoutOps.Holds(emptied, holder, GearOps.Utility, hs, null));
        Assert.Empty(LoadoutOps.Holders(emptied, item));
        Assert.True(LoadoutOps.Consistent(emptied, LoadoutOps.AllItems(emptied).Append(item)));

        var back = LoadoutOps.Equip(emptied, holder, GearOps.Utility, hs, item, new CutInfo());
        Assert.True(LoadoutOps.Holds(back, holder, GearOps.Utility, hs, item));
        Assert.Equal(LoadoutOps.Slots(g, holder, GearOps.Utility), LoadoutOps.Slots(back, holder, GearOps.Utility));
    }

    [SkippableFact]
    public void Replacing_the_item_in_a_slot_frees_the_old_one()
    {
        var g = Sample();
        var (holder, hs, item, free, fs) = Pick(g);
        var other = LoadoutOps.Items(g).FirstOrDefault(i => i.Type == GearOps.Utility && i.Guid != item && LoadoutOps.HeldBy(g, i.Guid) == null && i.Count > 0);
        Skip.If(other == null, "no spare utility item in the sample save");
        var ng = LoadoutOps.Equip(g, holder, GearOps.Utility, hs, other!.Guid, new CutInfo());
        Assert.Empty(LoadoutOps.Holders(ng, item));
        Assert.Equal(new[] { holder }, LoadoutOps.Holders(ng, other.Guid));
        Assert.True(LoadoutOps.Consistent(ng, LoadoutOps.AllItems(ng).Append(item)));
    }

    [SkippableFact]
    public void A_wrong_slot_type_a_missing_slot_and_an_unknown_item_are_refused()
    {
        var g = Sample();
        var (holder, _, item, _, _) = Pick(g);
        Assert.Throws<GvasException>(() => LoadoutOps.Equip(g, holder, GearOps.Modification, 0, item, new CutInfo()));        // a utility item in a mod slot
        Assert.Throws<GvasException>(() => LoadoutOps.Equip(g, holder, GearOps.Utility, 99, item, new CutInfo()));
        Assert.Throws<GvasException>(() => LoadoutOps.Equip(g, holder, GearOps.Utility, 0, new string('A', 32), new CutInfo()));
        Assert.Throws<GvasException>(() => LoadoutOps.Equip(g, "00000000000000000000000000000000", GearOps.Utility, 0, item, new CutInfo()));
    }

    [SkippableFact]
    public void A_queued_equip_goes_through_a_verified_write_and_reads_back()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var tmp = Path.Combine(Path.GetTempPath(), "zc-loadout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(tmp, "saves"); Directory.CreateDirectory(dir);
            var name = Path.GetFileName(src!);
            File.Copy(src!, Path.Combine(dir, name));
            var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(tmp, "backups"), gameRunning: () => null);
            var open = svc.Open("t", name);
            var g0 = new GvasFile(SaveContainer.Load(File.ReadAllBytes(Path.Combine(dir, name))).Gvas);
            // a write only changes operators on the roster, so the pair is picked from those
            var (holder, hs, item, free, fs) = Pick(g0, o => open.Model.Operators.TryGetValue(o, out var op) && !op.Dead);

            svc.Apply(Array.Empty<FieldChange>(), force: true, actions: new SaveAction[] { new SaveAction.SetEquipped(free, "utility", fs, item) });

            var g1 = new GvasFile(SaveContainer.Load(File.ReadAllBytes(Path.Combine(dir, name))).Gvas);
            Assert.True(LoadoutOps.Holds(g1, free, GearOps.Utility, fs, item));
            Assert.Null(LoadoutOps.Slots(g1, holder, GearOps.Utility)[hs].Item);
            Assert.True(LoadoutOps.Consistent(g1, LoadoutOps.AllItems(g1)));
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    [SkippableFact]
    public void The_pending_store_queues_an_equip_drops_a_rival_request_for_the_same_item_and_undoes_one_that_matches_the_save()
    {
        var g = Sample();
        var (holder, hs, item, free, fs) = Pick(g);
        var p = new PendingChanges();
        p.SetEquip(free, "utility", fs, item, null);
        Assert.Equal(1, p.ActionCount);
        p.SetEquip(holder, "utility", hs, null, item);                                        // empty the old slot: the other request stays
        Assert.Equal(2, p.ActionCount);
        p.SetEquip(holder, "utility", hs, item, item);                                         // the item back where the save has it: both requests for it go
        Assert.Equal(0, p.ActionCount);

        p.SetEquip(free, "utility", fs, item, null);
        Assert.Equal(new SaveAction.SetEquipped(free, "utility", fs, item), Assert.Single(p.BuildActions(null!).OfType<SaveAction.SetEquipped>()));
        p.SetEquip(free, "utility", fs, null, null);                                           // what the save holds: no request
        Assert.Equal(0, p.ActionCount);
    }
}
