using System.Text.Json;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Tests;

/// <summary>
/// Runs the C# distiller over the local raw dumps and compares every derived value with what the Python
/// editor computed from the same files (tools/golden/make_gamedata_golden.py). Skipped without local game data.
/// </summary>
public class GameDataParityTests
{
    static readonly string Dir = Path.Combine(Repo.Root, "gamedata");

    static (GameDatabase Db, JsonElement Golden)? Load()
    {
        var gp = Path.Combine(Dir, "golden", "gamedata.json");
        if (!File.Exists(gp)) return null;
        var raw = RawGameData.LoadLegacyDirectory(Dir);
        if (raw == null) return null;
        return (Distiller.Distill(raw), JsonDocument.Parse(File.ReadAllText(gp)).RootElement);
    }

    [SkippableFact]
    public void Counts_match()
    {
        var l = Load(); Skip.If(l == null, "no local game data / golden");
        var (db, g) = l!.Value;
        var c = g.GetProperty("counts");
        Assert.Equal(c.GetProperty("upgrades").GetInt32(), db.Upgrades.Count);
        Assert.Equal(c.GetProperty("items").GetInt32(), db.Items.Count);
        Assert.Equal(c.GetProperty("effects").GetInt32(), db.Effects.Count);
        Assert.Equal(c.GetProperty("thresholds").GetInt32(), db.FocusThresholds.Count);
        Assert.Equal(c.GetProperty("crisis").GetInt32(), db.CoilEffects.Count);
    }

    [SkippableFact]
    public void Upgrade_summaries_match()
    {
        var l = Load(); Skip.If(l == null, "no local game data / golden");
        var (db, g) = l!.Value;
        foreach (var p in g.GetProperty("upgrades").EnumerateObject())
        {
            var r = db.UpgradeLookup.Get(p.Name);
            Assert.NotNull(r);
            var s = r!.Summary();
            var e = p.Value;
            Assert.Equal(e.GetProperty("duration").GetInt32(), s.Duration);
            Assert.Equal(e.GetProperty("description").GetString(), s.Description);
            Assert.Equal(e.GetProperty("title").GetString(), s.Title);
            Assert.Equal(e.TryGetProperty("den_level", out var dl) && dl.ValueKind == JsonValueKind.Number ? dl.GetInt32() : (int?)null, s.DenLevel);
            Assert.Equal(e.GetProperty("cost").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetInt32()), s.Cost);
            Assert.Equal(e.GetProperty("require_tags").EnumerateArray().Select(x => x.GetString()), s.RequireTags);
            Assert.Equal(e.GetProperty("recipeTags").EnumerateArray().Select(x => x.GetString()), r.RecipeTags);
        }
        foreach (var p in g.GetProperty("by_tag").EnumerateObject())
            Assert.Equal(p.Value.GetString(), db.UpgradeLookup.ByTag[p.Name].Name);
        Assert.Equal(g.GetProperty("by_tag").EnumerateObject().Count(), db.UpgradeLookup.ByTag.Count);
    }

    [SkippableFact]
    public void Items_and_labels_match()
    {
        var l = Load(); Skip.If(l == null, "no local game data / golden");
        var (db, g) = l!.Value;
        foreach (var p in g.GetProperty("item_labels").EnumerateObject())
            Assert.Equal(p.Value.GetString(), db.ItemLabel(p.Name));
        foreach (var p in g.GetProperty("item_info").EnumerateObject())
        {
            var i = db.Items[p.Name]; var e = p.Value;
            Assert.Equal(e.GetProperty("display").GetString(), i.Display);
            Assert.Equal(e.GetProperty("description").GetString(), i.Description);
            Assert.Equal(e.GetProperty("kind").GetString(), i.Kind);
            Assert.Equal(e.GetProperty("rarity").GetString(), i.Rarity);
            Assert.Equal(e.GetProperty("tier").ValueKind == JsonValueKind.Number ? e.GetProperty("tier").GetInt32() : (int?)null, i.Tier);
            Assert.Equal(e.GetProperty("tags").EnumerateArray().Select(x => x.GetString()), i.Tags);
        }
    }

    [SkippableFact]
    public void Effect_text_matches()
    {
        var l = Load(); Skip.If(l == null, "no local game data / golden");
        var (db, g) = l!.Value;
        foreach (var p in g.GetProperty("effect_text").EnumerateObject())
        {
            var (title, desc) = db.EffectText(p.Name);
            var arr = p.Value;
            Assert.Equal(arr[0].ValueKind == JsonValueKind.Null ? null : arr[0].GetString(), title);
            Assert.Equal(arr[1].GetString(), desc);
        }
    }

    [SkippableFact]
    public void Focus_abilities_coil_and_units_match()
    {
        var l = Load(); Skip.If(l == null, "no local game data / golden");
        var (db, g) = l!.Value;
        foreach (var p in g.GetProperty("thresholds").EnumerateObject())
            Assert.Equal(p.Value.EnumerateArray().Select(x => x.GetInt32()), db.Thresholds(p.Name));
        foreach (var p in g.GetProperty("abilities").EnumerateObject())
            Assert.Equal(p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetString(), db.AbilityName(p.Name));
        foreach (var p in g.GetProperty("coil").EnumerateObject())
        {
            var c = db.CoilEffect(p.Name);
            Assert.NotNull(c);
            Assert.Equal(p.Value.GetProperty("title").GetString(), c!.Title);
            Assert.Equal(p.Value.GetProperty("description").GetString(), c.Description);
        }
        foreach (var p in g.GetProperty("units").EnumerateObject())
            Assert.Equal(p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetString(), db.CoilUnit(p.Name));
    }

    [SkippableFact]
    public void Database_round_trips_through_its_json_file()
    {
        var l = Load(); Skip.If(l == null, "no local game data / golden");
        var path = Path.Combine(Path.GetTempPath(), $"zc-db-{Guid.NewGuid():N}.json");
        try
        {
            l!.Value.Db.Save(path);
            var back = GameDatabase.TryLoad(path);
            Assert.NotNull(back);
            Assert.Equal(l.Value.Db.Upgrades.Count, back!.Upgrades.Count);
            Assert.Equal(l.Value.Db.ItemLabel("Resource_Credits"), back.ItemLabel("Resource_Credits"));
            Assert.Equal(l.Value.Db.FocusThresholds["br.AbilityID.Class.Lethal"], back.FocusThresholds["br.AbilityID.Class.Lethal"]);
            new FileInfo(path).Length.ToString().ToString();    // size is informational
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_or_foreign_database_files_load_as_null()
    {
        Assert.Null(GameDatabase.TryLoad(Path.Combine(Path.GetTempPath(), "does-not-exist.json")));
        var p = Path.Combine(Path.GetTempPath(), $"zc-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(p, "{\"schema\": 999}");
        try { Assert.Null(GameDatabase.TryLoad(p)); } finally { File.Delete(p); }
        File.WriteAllText(p, "not json");
        try { Assert.Null(GameDatabase.TryLoad(p)); } finally { File.Delete(p); }
        Assert.True(GameDatabase.Empty.IsEmpty);
        Assert.Null(GameDatabase.Empty.ItemLabel("x"));
        Assert.Null(GameDatabase.Empty.AbilityName("a.b"));
    }
}
