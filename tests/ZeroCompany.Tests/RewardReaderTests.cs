using Newtonsoft.Json.Linq;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Views;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Tests;

/// <summary>Which missions (and which player choice) give which items: the data behind the Armory's "Mission choice rewards".</summary>
public class RewardReaderTests
{
    /// <summary>Objects shaped like the game's Jedi Meditation recipe: two choices, each unlocking a pair of utility items.</summary>
    static Dictionary<string, JObject> Meditation() => new()
    {
        ["OT_Tel_012_C"] = JObject.Parse("""{ "Type": "BlueprintGeneratedClass", "Name": "OT_Tel_012_C" }"""),
        ["Default__OT_Tel_012_C"] = JObject.Parse("""
            { "Type": "OT_Tel_012_C", "Name": "Default__OT_Tel_012_C", "Properties": {
                "Name": { "SourceString": "Jedi Meditation I" },
                "RewardTiers": [
                  { "TierRequirements": [], "CustomRewards": [ { "ObjectName": "CustomReward_BondsByTags_C'Default__OT_Tel_012_C:CustomReward_BondsByTags_C_2'" } ] },
                  { "TierRequirements": [ { "ObjectName": "BP_RecipeInput_PlayerChoice_C'Default__OT_Tel_012_C:BP_RecipeInput_PlayerChoice_C_2'" } ],
                    "CustomRewards": [ { "ObjectName": "CustomReward_InventoryItem_C'Default__OT_Tel_012_C:CustomReward_InventoryItem_C_2'" },
                                       { "ObjectName": "CustomReward_InventoryItem_C'Default__OT_Tel_012_C:CustomReward_InventoryItem_C_3'" } ] },
                  { "TierRequirements": [ { "ObjectName": "BP_RecipeInput_PlayerChoice_C'Default__OT_Tel_012_C:BP_RecipeInput_PlayerChoice_C_1'" } ],
                    "CustomRewards": [ { "ObjectName": "CustomReward_InventoryItem_C'Default__OT_Tel_012_C:CustomReward_InventoryItem_C_1'" } ] } ] } }
            """),
        ["BP_RecipeInput_PlayerChoice_C_2"] = JObject.Parse("""{ "Type": "BP_RecipeInput_PlayerChoice_C", "Name": "BP_RecipeInput_PlayerChoice_C_2", "Properties": { "NameFormat": { "SourceString": "<Bold>Meditate on the Past.</>" } } }"""),
        ["BP_RecipeInput_PlayerChoice_C_1"] = JObject.Parse("""{ "Type": "BP_RecipeInput_PlayerChoice_C", "Name": "BP_RecipeInput_PlayerChoice_C_1", "Properties": { "NameFormat": { "SourceString": "<Bold>Meditate on the Present.</> <Description>Stay in the moment.</>" } } }"""),
        ["CustomReward_InventoryItem_C_2"] = Item("CustomReward_InventoryItem_C_2", "UtilityItem_Serenity_T1"),
        ["CustomReward_InventoryItem_C_3"] = Item("CustomReward_InventoryItem_C_3", "UtilityItem_ListenThroughChaos_T1"),
        ["CustomReward_InventoryItem_C_1"] = Item("CustomReward_InventoryItem_C_1", "UtilityItem_ControlPain_T1"),
        ["CustomReward_BondsByTags_C_2"] = JObject.Parse("""{ "Type": "CustomReward_BondsByTags_C", "Name": "CustomReward_BondsByTags_C_2", "Properties": { "Amount": 10 } }"""),
    };

    static JObject Item(string name, string item) => JObject.Parse(
        $$"""{ "Type": "CustomReward_InventoryItem_C", "Name": "{{name}}", "Properties": { "InventoryItem": { "ObjectName": "BP_BrunoUtilityInventoryItem_C'{{item}}'" }, "Amount": 1 } }""");

    [Fact]
    public void A_recipes_item_rewards_are_grouped_by_the_choice_that_unlocks_them_and_other_rewards_are_ignored()
    {
        var r = RewardReader.FromObjects("OT_Tel_012", Meditation())!;
        Assert.Equal("Jedi Meditation I", (string)r["title"]!);
        var tiers = r["tiers"]!.ToList();
        Assert.Equal(2, tiers.Count);                                              // the bond-only tier has no items, so it is dropped
        Assert.Equal(new[] { "Meditate on the Past." }, tiers[0]["choices"]!.Select(c => (string)c!));
        Assert.Equal(new[] { "UtilityItem_Serenity_T1", "UtilityItem_ListenThroughChaos_T1" }, tiers[0]["items"]!.Select(i => (string)i["item"]!));
        Assert.Equal(new[] { "Meditate on the Present." }, tiers[1]["choices"]!.Select(c => (string)c!));   // the description text is dropped
        Assert.Equal(new[] { "UtilityItem_ControlPain_T1" }, tiers[1]["items"]!.Select(i => (string)i["item"]!));
        Assert.Equal(1, (int)tiers[0]["items"]![0]!["amount"]!);
    }

    [Fact]
    public void A_recipe_with_no_item_rewards_gives_nothing()
    {
        var objs = new Dictionary<string, JObject>
        {
            ["Default__X_C"] = JObject.Parse("""{ "Name": "Default__X_C", "Properties": { "RewardTiers": [ { "TierRequirements": [], "CustomRewards": [] } ] } }"""),
        };
        Assert.Null(RewardReader.FromObjects("X", objs));
        Assert.Null(RewardReader.FromObjects("X", new Dictionary<string, JObject>()));
    }

    [Fact]
    public void Choice_labels_keep_only_the_bold_option_text()
    {
        Assert.Equal("Scan the supply cache.", RewardReader.ChoiceLabel("<Bold>Scan the supply cache.</> <Description>We have just the droid.</>"));
        Assert.Equal("Plain text", RewardReader.ChoiceLabel("Plain text"));              // no bold markup: the whole text
        Assert.Equal("", RewardReader.ChoiceLabel(null));
    }

    [Fact]
    public void The_database_records_which_mission_and_choice_gives_each_known_item()
    {
        var raw = new RawGameData
        {
            Items = JObject.Parse("""{ "UtilityItem_Serenity_T1": { "path": "x", "objects": [] }, "UtilityItem_ControlPain_T1": { "path": "y", "objects": [] } }"""),
            Rewards = new JArray(RewardReader.FromObjects("OT_Tel_012", Meditation())!),
        };
        var db = Distiller.Distill(raw);
        var serenity = db.Items["UtilityItem_Serenity_T1"];
        var src = Assert.Single(serenity.Sources);
        Assert.Equal("Jedi Meditation I", src.Mission);
        Assert.Equal("Meditate on the Past.", src.Choice);
        Assert.Equal("Jedi Meditation I: Meditate on the Past.", src.Text);
        Assert.Equal("Jedi Meditation I: Meditate on the Present.", Assert.Single(db.Items["UtilityItem_ControlPain_T1"].Sources).Text);
        Assert.DoesNotContain("UtilityItem_ListenThroughChaos_T1", db.Items.Keys);          // items the database does not know are skipped
    }

    [Fact]
    public void Add_items_can_be_filtered_to_choice_rewards_you_never_got_and_searched_by_mission()
    {
        ArmoryAddView V(string asset, string name, int owned, string source = "", bool choice = false) =>
            new() { Asset = asset, Name = name, Kind = "Utility", Owned = owned, Source = source, FromChoice = choice };
        var items = new[]
        {
            V("UtilityItem_Serenity_T1", "Serenity", 0, "Jedi Meditation I: Meditate on the Past.", true),
            V("UtilityItem_ControlPain_T1", "Control Pain", 2, "Jedi Meditation I: Meditate on the Present.", true),
            V("UtilityItem_Medkit_T1", "Medkit", 1, "Uyter Scout"),
            V("UtilityItem_SmokeBomb_T1", "Smoke Bomb", 0),
        };
        List<string> Run(AddQuery q) => ArmoryAddFilter.Run(items, q, _ => 0).Select(i => i.Name).ToList();

        Assert.Equal(new[] { "Control Pain", "Serenity" }, Run(new AddQuery(Group: "Choices")));
        Assert.Equal(new[] { "Serenity" }, Run(new AddQuery(Group: "Choices", NeverOwnedOnly: true)));          // the missed ones
        Assert.Equal(new[] { "Control Pain", "Serenity" }, Run(new AddQuery(Text: "meditation")));              // search finds the mission
        Assert.Equal(new[] { "Serenity" }, Run(new AddQuery(Text: "meditate past")));
        Assert.Equal(new[] { "Medkit" }, Run(new AddQuery(Text: "uyter")));
        Assert.Equal(new[] { "All", "Choices", "Utility" }, ArmoryAddFilter.Groups(items).Select(g => g.Value));
        Assert.DoesNotContain("Choices", ArmoryAddFilter.Groups(items.Where(i => !i.FromChoice)).Select(g => g.Value));
    }
}
