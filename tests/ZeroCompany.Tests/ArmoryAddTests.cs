using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>Adding items the squad never had (the missed reward of a Tel meditation, say) as new inventory entries.</summary>
public class ArmoryAddTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-armory-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (SaveService Svc, string Path) NewRig()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), gameRunning: () => null);
        svc.Open("t", name);
        return (svc, Path.Combine(dir, name));
    }

    static List<(string Asset, string Guid, long Count)> Inventory(string path) =>
        ArmoryOps.Entries(new GvasFile(SaveContainer.Load(File.ReadAllBytes(path)).Gvas));

    static void Act(SaveService svc, params SaveAction[] actions) => svc.Apply(Array.Empty<FieldChange>(), force: true, actions: actions);

    /// <summary>An item name the sample save has no entry of, so a test can tell what was added.</summary>
    static string Missing(string path, string prefix)
    {
        var have = Inventory(path).Select(e => e.Asset).ToHashSet();
        var pick = new[] { "Serenity", "ListenThroughChaos", "PresenceInTheForce", "ControlPain", "VisionsOfTheFuture", "SurgingHarmony" }
            .Select(n => $"{prefix}_{n}_T1").FirstOrDefault(n => !have.Contains(n));
        Skip.If(pick == null, "the sample save already has every candidate item");
        return pick!;
    }

    [Fact]
    public void Item_names_map_to_the_games_asset_paths_and_only_addable_kinds_are_accepted()
    {
        var (obj, soft) = ArmoryOps.ItemPaths("UtilityItem_Serenity_T1");
        Assert.Equal("/Game/Game/GameData/ItemData/UtilityItems/UtilityItem_Serenity_T1.UtilityItem_Serenity_T1", obj);
        Assert.Equal("/Game/Game/GameData/ItemData/UtilityItems/UtilityItem_Serenity_T1", soft);
        Assert.StartsWith("/Game/Game/GameData/ItemData/Modifications/", ArmoryOps.ItemPaths("Modification_Scope_LongRange_T2").Object);
        foreach (var bad in new[] { "Resource_Intelligence", "Serenity", "UtilityItem_", "UtilityItem_../x", "UtilityItem_a b", "" })
            Assert.ThrowsAny<GvasException>(() => ArmoryOps.ItemPaths(bad));
    }

    [SkippableFact]
    public void A_missing_utility_item_is_added_as_one_new_entry_and_nothing_else_changes()
    {
        var (svc, path) = NewRig();
        var item = Missing(path, "UtilityItem");
        var before = Inventory(path);
        Act(svc, new SaveAction.AddInventoryItem(item, 1));
        var after = Inventory(path);

        Assert.Equal(before.Count + 1, after.Count);
        var added = Assert.Single(after, e => e.Asset == item);
        Assert.Equal(1, added.Count);
        Assert.DoesNotContain(added.Guid, before.Select(e => e.Guid));
        Assert.Equal(before.OrderBy(e => e.Guid), after.Where(e => e.Guid != added.Guid).OrderBy(e => e.Guid));       // every old entry intact

        var reopened = svc.Open("t", Path.GetFileName(path));                                                          // the file reads back
        Assert.Contains(reopened.Model.Inventory, i => i.Asset == item);
        var gv = new GvasFile(SaveContainer.Load(File.ReadAllBytes(path)).Gvas);
        var entry = ArmoryOps.Entries(gv);
        Assert.Equal(added.Guid, entry.Single(e => e.Asset == item).Guid);
    }

    [SkippableFact]
    public void Several_copies_are_separate_entries_with_their_own_guids()
    {
        var (svc, path) = NewRig();
        var item = Missing(path, "UtilityItem");
        Act(svc, new SaveAction.AddInventoryItem(item, 3));
        var copies = Inventory(path).Where(e => e.Asset == item).ToList();
        Assert.Equal(3, copies.Count);
        Assert.Equal(3, copies.Select(c => c.Guid).Distinct().Count());
        Assert.All(copies, c => Assert.Equal(1, c.Count));
    }

    [SkippableFact]
    public void Bad_requests_are_refused_and_leave_the_file_alone()
    {
        var (svc, path) = NewRig();
        var before = File.ReadAllBytes(path);
        foreach (var bad in new SaveAction[]
        {
            new SaveAction.AddInventoryItem("Resource_Intelligence", 1),            // a stack, not an entry
            new SaveAction.AddInventoryItem("UtilityItem_Serenity_T1", 0),
            new SaveAction.AddInventoryItem("UtilityItem_Serenity_T1", SaveService.MaxItemCopies + 1),
        })
            Assert.ThrowsAny<Exception>(() => Act(svc, bad));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void The_pending_store_turns_queued_items_into_actions_and_a_description()
    {
        var (svc, path) = NewRig();
        var item = Missing(path, "UtilityItem");
        var p = new PendingChanges();
        p.SetItemAdd(item, 2);
        Assert.Equal(1, p.ActionCount);
        Assert.Equal(new SaveAction.AddInventoryItem(item, 2), Assert.Single(p.BuildActions(svc.Current!.Upgrades)));
        Assert.Contains(p.Describe(svc.Current!), l => l.Text != null && l.Text.StartsWith("Add 2 x "));
        p.SetItemAdd(item, 0);
        Assert.Equal(0, p.ActionCount);
    }
}
