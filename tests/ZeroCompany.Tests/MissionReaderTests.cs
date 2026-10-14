using Newtonsoft.Json.Linq;
using ZeroCompany.Core.Views;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Extractor;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Tests;

/// <summary>What the game's own mission assets say about each mission: the data behind the Galaxy screen's Den list.</summary>
public class MissionReaderTests
{
    static Dictionary<string, JObject> Brentaal() => new()
    {
        ["BitReactorUIDataFragment_0"] = JObject.Parse("""
            { "Type": "BitReactorUIDataFragment", "Name": "BitReactorUIDataFragment_0", "Properties": {
                "DisplayName": { "SourceString": "Imperial Riches" },
                "ShortDescription": { "SourceString": "Folks will pay for <bold>Imperial</> supply shipments." } } }
            """),
        ["SK_Brentaal_035"] = JObject.Parse("""
            { "Type": "BrunoMissionData", "Name": "SK_Brentaal_035", "Properties": {
                "RegionTag": { "TagName": "BitReactor.MapRegions.CoreEast" },
                "Location": { "RowName": "BrentaalIV" }, "LifeSpan": 4, "MinSquadSize": 4, "MaxSquadSize": 4,
                "ObjectivesText": [ { "SourceString": "Seize the Imperial Data" } ],
                "MissionTags": [ "BitReactor.Design.MissionDifficulty.Medium" ],
                "EnemyFactionTag": { "TagName": "br.Faction.Imperial" }, "MissionTypeTag": { "TagName": "br.Mission.Skirmish.Capture" } } }
            """),
    };

    [Fact]
    public void A_mission_asset_is_read_into_its_name_place_difficulty_and_faction()
    {
        var m = MissionReader.FromObjects("SK_Brentaal_035", Brentaal())!;
        Assert.Equal("Imperial Riches", (string?)m["name"]);
        Assert.Equal("Folks will pay for Imperial supply shipments.", (string?)m["summary"]);
        Assert.Equal("Seize the Imperial Data", (string?)m["objective"]);
        Assert.Equal("BrentaalIV", (string?)m["location"]);
        Assert.Equal("CoreEast", (string?)m["region"]);
        Assert.Equal("Medium", (string?)m["difficulty"]);
        Assert.Equal("Imperial", (string?)m["faction"]);
        Assert.Equal(4, (int?)m["lifespan"]);
        Assert.Null(MissionReader.FromObjects("SK_Other", Brentaal()));              // no mission object by that name
    }

    [Fact]
    public void A_mission_title_is_its_in_game_name_else_its_name_string_else_the_asset_name()
    {
        var db = new GameDatabase();
        Assert.Equal("SK Brentaal 035", ViewBuilder.MissionTitle(db, "SK_Brentaal_035"));
        db.NameStrings["SK_Brentaal_035_Name"] = "From strings";
        Assert.Equal("From strings", ViewBuilder.MissionTitle(db, "SK_Brentaal_035"));
        db.Missions["SK_Brentaal_035"] = new MissionInfo { Name = "" };
        Assert.Equal("From strings", ViewBuilder.MissionTitle(db, "SK_Brentaal_035"));
        db.Missions["SK_Brentaal_035"] = new MissionInfo { Name = "Imperial Riches" };
        Assert.Equal("Imperial Riches", ViewBuilder.MissionTitle(db, "SK_Brentaal_035"));
    }

    [SkippableFact]
    public void The_real_mission_dump_distills_into_the_database()
    {
        var raw = RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        Skip.If(raw == null || raw.Missions.Count == 0, "no dev dump of missions (zcassets missions --json)");
        var db = Distiller.Distill(raw!);
        var m = db.Missions["SK_Brentaal_035"];
        Assert.Equal("Imperial Riches", m.Name);
        Assert.Equal("Imperial", m.Faction);
        Assert.Equal("BrentaalIV", m.Location);
        Assert.Equal("No Kings, No Masters", db.Missions["HM_TrickLuco_020"].Name);
    }
}
