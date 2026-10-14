using Newtonsoft.Json.Linq;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.Tests;

/// <summary>The Holotable's operations: what the game says about each, and how long each has left on offer in the save.</summary>
public class OperationTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-oper-" + Guid.NewGuid().ToString("N"));
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

    static GvasFile Load(string path) => new(SaveContainer.Load(File.ReadAllBytes(path)).Gvas);
    static void Act(SaveService svc, params SaveAction[] actions) => svc.Apply(Array.Empty<FieldChange>(), force: true, actions: actions);

    [Fact]
    public void An_operation_asset_is_read_into_its_name_expiry_and_options()
    {
        var objs = new Dictionary<string, JObject>
        {
            ["Default__OT_0055_C"] = JObject.Parse("""
                { "Type": "OT_0055_C", "Name": "Default__OT_0055_C", "Properties": {
                    "Name": { "SourceString": "The Iron Vow" }, "Description": { "SourceString": "A garbled <bold>message</>." },
                    "Expiration": 1, "RecipeTypeTag": { "TagName": "BitReactor.Recipe.FieldTask.Opportunity" } } }
                """),
            ["BP_RecipeInput_PlayerChoice_C_0"] = JObject.Parse("""{ "Type": "BP_RecipeInput_PlayerChoice_C", "Name": "BP_RecipeInput_PlayerChoice_C_0", "Properties": { "NameFormat": { "SourceString": "<Bold>Assign an Operator</>" } } }"""),
            ["BP_RecipeInput_PlayerChoice_C_2"] = JObject.Parse("""{ "Type": "BP_RecipeInput_PlayerChoice_C", "Name": "BP_RecipeInput_PlayerChoice_C_2", "Properties": { "NameFormat": { "SourceString": "<Bold>Decline the job.</>" } } }"""),
        };
        var o = OperationReader.FromObjects("OT_0055", objs)!;
        Assert.Equal("The Iron Vow", (string?)o["name"]);
        Assert.Equal("A garbled message.", (string?)o["description"]);
        Assert.Equal(1, (int?)o["expiration"]);
        Assert.Equal("Opportunity", (string?)o["kind"]);
        Assert.Equal(new[] { "Assign an Operator", "Decline the job." }, o["choices"]!.Select(c => (string?)c));
        Assert.Null(OperationReader.FromObjects("X", new Dictionary<string, JObject>()));
    }

    [SkippableFact]
    public void The_real_operation_dump_distills_into_the_database()
    {
        var raw = RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        Skip.If(raw == null || raw.Operations.Count == 0, "no dev dump of operations (zcassets operations --json)");
        var db = Distiller.Distill(raw!);
        Assert.Equal("Listening In", db.Operations["MT_06_006_ListeningIn"].Name);
        Assert.Equal(1, db.Operations["MT_06_006_ListeningIn"].Expiration);
    }

    [SkippableFact]
    public void Resetting_how_long_an_operation_has_been_on_offer_changes_only_that_number()
    {
        var (svc, path) = NewRig();
        var ops = OperationOps.Active(Load(path));
        var target = ops.FirstOrDefault(o => o.Available && o.AvailableTurns > 0);
        Skip.If(target == null, "no operation in the sample save has been on offer for a cycle");

        Act(svc, new SaveAction.SetOperationAge(target!.Id, 0));

        var after = OperationOps.Active(Load(path));
        Assert.Equal(ops.Count, after.Count);
        Assert.Equal(0, after.Single(o => o.Id == target.Id).AvailableTurns);
        foreach (var o in ops.Where(o => o.Id != target.Id)) Assert.Equal(o, after.Single(a => a.Id == o.Id));
    }

    [SkippableFact]
    public void Unknown_operations_and_impossible_ages_are_refused()
    {
        var (svc, path) = NewRig();
        var target = OperationOps.Active(Load(path)).FirstOrDefault(o => o.Available);
        Skip.If(target == null, "no operation on offer in the sample save");
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetOperationAge(new string('0', 32), 0)));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetOperationAge(target!.Id, -1)));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetOperationAge(target.Id, OperationOps.MaxAge + 1)));
        Assert.ThrowsAny<Exception>(() => Act(svc, new SaveAction.SetOperationAge(target.Id, 0), new SaveAction.SetOperationAge(target.Id, 0)));
        Assert.Equal(target.AvailableTurns, OperationOps.Active(Load(path)).Single(o => o.Id == target.Id).AvailableTurns);
    }
}
