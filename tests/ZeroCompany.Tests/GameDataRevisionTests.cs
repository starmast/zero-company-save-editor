using System.Text.Json;
using ZeroCompany.GameData.Distill;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Models;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>Game data extracted by an older version still loads, but is flagged so the editor can ask for a new extraction.</summary>
public class GameDataRevisionTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-rev-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    static GameDatabase Small() => new() { Revision = GameDatabase.CurrentRevision, Items = { ["x"] = new ItemInfo() } };

    [Fact]
    public void A_file_from_before_the_revision_was_recorded_loads_but_is_stale()
    {
        Directory.CreateDirectory(_tmp);
        var path = Path.Combine(_tmp, "old.json");
        Small().Save(path);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(node.Remove(node.Select(p => p.Key).Single(k => k.Equals("revision", StringComparison.OrdinalIgnoreCase))));
        File.WriteAllText(path, node.ToJsonString());

        var db = GameDatabase.TryLoad(path);
        Assert.NotNull(db);
        Assert.False(db!.IsEmpty);
        Assert.True(db.IsStale);
    }

    [Fact]
    public void A_current_file_is_not_stale_and_an_empty_database_is_never_stale()
    {
        Directory.CreateDirectory(_tmp);
        var path = Path.Combine(_tmp, "new.json");
        Small().Save(path);
        Assert.False(GameDatabase.TryLoad(path)!.IsStale);
        Assert.False(GameDatabase.Empty.IsStale);
    }

    [SkippableFact]
    public void Extraction_stamps_the_current_revision_and_the_doctor_warns_about_an_older_one()
    {
        var raw = RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        Skip.If(raw == null, "no local game data");
        var db = Distiller.Distill(raw!);
        Assert.Equal(GameDatabase.CurrentRevision, db.Revision);

        var ws = ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false);
        db.Revision = 0;
        db.Save(ws.Env.GameDbPath);
        var check = ZcDoctor.Run(ws).Single(c => c.Name == "game data");
        Assert.Contains("older version", check.Detail);
    }
}
