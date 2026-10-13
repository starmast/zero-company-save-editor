using Newtonsoft.Json.Linq;
using ZeroCompany.AssetsCli;

namespace ZeroCompany.Tests;

/// <summary>The game-free parts of zcassets: turning file keys into package paths, picking one asset by name, and searching a dump.</summary>
public class AssetToolsTests
{
    static readonly string[] Keys =
    {
        "SWZeroCompany/Content/Game/Core/Blueprints/Objects/TurnStructures/BP_TurnStructure_Default.uasset",
        "SWZeroCompany/Content/Game/Core/Blueprints/Objects/TurnStructures/BP_TurnStructure_ThreeWayBattle.uasset",
        "SWZeroCompany/Content/Game/Core/Blueprints/Objects/TurnStructures/BP_TurnStructure_ThreeWayBattle.uexp",
        "SWZeroCompany/Content/Game/GameData/Teams/Enemy2Team.uasset",
        "SWZeroCompany/Content/Game/GameData/Teams/EnemyTeam.uasset",
        "Engine/Content/Foo.uasset",
    };

    [Fact]
    public void File_keys_become_sorted_package_paths_and_only_game_assets_count()
    {
        var p = AssetTools.PackagePaths(Keys);
        Assert.Equal(4, p.Count);
        Assert.Equal("/Game/Game/Core/Blueprints/Objects/TurnStructures/BP_TurnStructure_Default", p[0]);
        Assert.DoesNotContain(p, x => x.Contains("Foo") || x.EndsWith(".uexp"));
    }

    [Fact]
    public void An_asset_is_picked_by_exact_path_by_file_name_or_by_a_unique_part()
    {
        var p = AssetTools.PackagePaths(Keys);
        Assert.EndsWith("Enemy2Team", AssetTools.Pick(p, "/Game/Game/GameData/Teams/Enemy2Team"));
        Assert.EndsWith("EnemyTeam", AssetTools.Pick(p, "EnemyTeam"));               // a name wins over the longer match
        Assert.EndsWith("ThreeWayBattle", AssetTools.Pick(p, "threeway"));
        var ambiguous = Assert.Throws<ArgumentException>(() => AssetTools.Pick(p, "TurnStructure"));
        Assert.Contains("matches 2 assets", ambiguous.Message);
        Assert.Contains("no asset matches", Assert.Throws<ArgumentException>(() => AssetTools.Pick(p, "Nope")).Message);
    }

    [Fact]
    public void Grep_finds_property_names_and_string_values_with_their_json_paths()
    {
        var j = JObject.Parse("""
            { "objects": [ { "Name": "X", "Properties": {
                "FriendlyTeamTags": [ "BitReactor.Team.Enemy2" ],
                "TurnTag": { "TagName": "BitReactor.Team.Enemy2" },
                "Ref": { "ObjectName": "Foo", "ObjectPath": "/Game/Enemy2/Path" } } } ] }
            """);
        var hits = AssetTools.Grep(j, "enemy2");
        Assert.Contains(hits, h => h.Path == "objects[0].Properties.FriendlyTeamTags[0]");
        Assert.Contains(hits, h => h.Path == "objects[0].Properties.TurnTag.TagName");
        Assert.DoesNotContain(hits, h => h.Path.EndsWith("ObjectPath"));                // path noise is never a hit
        Assert.Contains(AssetTools.Grep(j, "friendly"), h => h.Path.EndsWith("FriendlyTeamTags"));   // property names match too
    }

    [Fact]
    public void Dumps_lose_the_long_object_paths()
    {
        var j = JObject.Parse("""{ "a": { "ObjectName": "N", "ObjectPath": "/very/long" }, "b": [ { "ObjectPath": "x", "k": 1 } ] }""");
        AssetTools.StripNoise(j);
        Assert.Empty(j.Descendants().OfType<JProperty>().Where(p => p.Name == "ObjectPath"));
        Assert.Equal(1, (int)j["b"]![0]!["k"]!);
    }
}
