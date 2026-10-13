using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Tests;

/// <summary>Finding and ordering items in the Armory's Add items list (about 150 items in the real game).</summary>
public class ArmoryAddFilterTests
{
    static ArmoryAddView Item(string asset, string name, string kind, int? tier, string rarity, int owned = 0, string desc = "") =>
        new() { Asset = asset, Name = name, Kind = kind, Tier = tier, Rarity = rarity, Owned = owned, Description = desc, Slot = ArmoryOps.SlotOf(asset) };

    static readonly ArmoryAddView[] Items =
    {
        Item("UtilityItem_Serenity_T1", "Serenity (T1)", "Utility", 1, "Rare", 0, "Calms the mind"),
        Item("UtilityItem_SmokeBomb_T1", "Smoke Bomb (T1)", "Utility", 1, "Common", 5, "Blocks line of sight"),
        Item("UtilityItem_SmokeBomb_T3", "Smoke Bomb (T3)", "Utility", 3, "Uncommon", 1, "Blocks line of sight"),
        Item("Modification_Scope_LongRange_T2", "Advanced Range Scope (T2)", "Modification", 2, "Uncommon", 2, "Reduces range penalties"),
        Item("Modification_Scope_Tracking_T1", "Tracking Scope (T1)", "Modification", 1, "Common", 0),
        Item("Modification_Barrel_Amplifier_T4", "Amplifier Barrel (T4)", "Modification", 4, "Unique", 0, "More damage"),
        Item("Modification_Cells_IonModulator_T2", "Ion Modulator (T2)", "Modification", 2, "Rare", 3),
    };

    static List<string> Names(AddQuery q, Func<string, int>? queued = null) =>
        ArmoryAddFilter.Run(Items, q, queued ?? (_ => 0)).Select(i => i.Asset).ToList();

    [Fact]
    public void A_mods_slot_is_read_from_its_name_and_utility_items_have_none()
    {
        Assert.Equal("Scope", ArmoryOps.SlotOf("Modification_Scope_LongRange_T2"));
        Assert.Equal("Cells", ArmoryOps.SlotOf("Modification_Cells_IonModulator_T2"));
        Assert.Equal("", ArmoryOps.SlotOf("UtilityItem_Serenity_T1"));
        Assert.Equal("", ArmoryOps.SlotOf("Modification_Odd"));
        Assert.Equal("", ArmoryOps.SlotOf("Resource_Credits"));
    }

    [Fact]
    public void Search_needs_every_word_in_any_order_and_looks_at_the_internal_name_and_description_too()
    {
        Assert.Equal(new[] { "Modification_Scope_LongRange_T2" }, Names(new AddQuery(Text: "range scope")));
        Assert.Equal(new[] { "Modification_Scope_LongRange_T2" }, Names(new AddQuery(Text: "SCOPE longrange")));                    // internal name, any case
        Assert.Equal(new[] { "UtilityItem_SmokeBomb_T1", "UtilityItem_SmokeBomb_T3" }, Names(new AddQuery(Text: "line of sight")));  // description
        Assert.Equal(new[] { "Modification_Barrel_Amplifier_T4" }, Names(new AddQuery(Text: "unique")));                            // rarity
        Assert.Empty(Names(new AddQuery(Text: "scope nonsense")));
        Assert.Equal(Items.Length, Names(new AddQuery(Text: "   ")).Count);
    }

    [Fact]
    public void Group_tier_rarity_and_owned_filters_combine()
    {
        Assert.Equal(3, Names(new AddQuery(Group: "Utility")).Count);
        Assert.Equal(4, Names(new AddQuery(Group: "Mods")).Count);
        Assert.Equal(2, Names(new AddQuery(Group: "Mods:Scope")).Count);
        Assert.Equal(new[] { "Modification_Cells_IonModulator_T2" }, Names(new AddQuery(Group: "Mods:Cells")));
        Assert.Equal(new[] { "Modification_Scope_LongRange_T2", "Modification_Cells_IonModulator_T2" }.OrderBy(x => x).ToList(),
                     Names(new AddQuery(Tier: 2)).OrderBy(x => x).ToList());
        Assert.Equal(new[] { "UtilityItem_Serenity_T1", "Modification_Cells_IonModulator_T2" }.OrderBy(x => x).ToList(),
                     Names(new AddQuery(Rarity: "Rare")).OrderBy(x => x).ToList());
        Assert.Equal(new[] { "Modification_Barrel_Amplifier_T4", "Modification_Scope_Tracking_T1", "UtilityItem_Serenity_T1" },
                     Names(new AddQuery(NeverOwnedOnly: true)).OrderBy(x => x).ToList());
        Assert.Equal(new[] { "Modification_Scope_Tracking_T1" }, Names(new AddQuery(Group: "Mods", NeverOwnedOnly: true, Tier: 1)));
    }

    [Fact]
    public void Queued_only_shows_just_the_items_with_copies_queued()
    {
        var queued = new Dictionary<string, int> { ["UtilityItem_Serenity_T1"] = 2, ["Modification_Scope_Tracking_T1"] = 1 };
        Func<string, int> q = a => queued.GetValueOrDefault(a);
        Assert.Equal(new[] { "Modification_Scope_Tracking_T1", "UtilityItem_Serenity_T1" }.OrderBy(x => x).ToList(),
                     Names(new AddQuery(QueuedOnly: true), q).OrderBy(x => x).ToList());
        queued.Clear();
        Assert.Empty(Names(new AddQuery(QueuedOnly: true), q));
    }

    [Fact]
    public void Sorting_by_name_tier_rarity_and_fewest_owned()
    {
        var byName = ArmoryAddFilter.Run(Items, new AddQuery(), _ => 0).Select(i => i.Name).ToList();
        Assert.Equal(byName.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), byName);

        var low = ArmoryAddFilter.Run(Items, new AddQuery(Sort: AddSort.TierLow), _ => 0).Select(i => i.Tier).ToList();
        Assert.Equal(new int?[] { 1, 1, 1, 2, 2, 3, 4 }, low);
        var high = ArmoryAddFilter.Run(Items, new AddQuery(Sort: AddSort.TierHigh), _ => 0).Select(i => i.Tier).ToList();
        Assert.Equal(new int?[] { 4, 3, 2, 2, 1, 1, 1 }, high);

        var rarity = ArmoryAddFilter.Run(Items, new AddQuery(Sort: AddSort.Rarity), _ => 0).Select(i => i.Rarity).ToList();
        Assert.Equal(new[] { "Unique", "Rare", "Rare", "Uncommon", "Uncommon", "Common", "Common" }, rarity);

        var fewest = ArmoryAddFilter.Run(Items, new AddQuery(Sort: AddSort.FewestOwned), _ => 0).Select(i => i.Owned).ToList();
        Assert.Equal(fewest.OrderBy(x => x).ToList(), fewest);
        Assert.Equal(0, fewest[0]);
    }

    [Fact]
    public void Equal_items_keep_a_stable_order_so_tiers_of_one_item_stay_together()
    {
        var a = ArmoryAddFilter.Run(Items, new AddQuery(Sort: AddSort.Rarity), _ => 0).Select(i => i.Asset).ToList();
        var b = ArmoryAddFilter.Run(Items.Reverse(), new AddQuery(Sort: AddSort.Rarity), _ => 0).Select(i => i.Asset).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void The_dropdown_choices_come_from_the_data()
    {
        var groups = ArmoryAddFilter.Groups(Items);
        Assert.Equal(new[] { "All", "Utility", "Mods", "Mods:Barrel", "Mods:Cells", "Mods:Scope" }, groups.Select(g => g.Value));
        Assert.Equal("All items", groups[0].Label);
        Assert.Equal(new[] { "Common", "Uncommon", "Rare", "Unique" }, ArmoryAddFilter.Rarities(Items));
        Assert.Equal(new[] { 1, 2, 3, 4 }, ArmoryAddFilter.Tiers(Items));
        Assert.Equal(new[] { "All" }, ArmoryAddFilter.Groups(Array.Empty<ArmoryAddView>()).Select(g => g.Value));
    }
}
