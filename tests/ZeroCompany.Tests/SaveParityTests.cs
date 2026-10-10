using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZeroCompany.Core.Save;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Tests;

/// <summary>
/// Opens the local sample save with the C# service and compares everything with what the Python editor produced
/// (tools/golden/make_save_golden.py). Skipped without the sample save or its golden files.
/// </summary>
public class SaveParityTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-test-" + Guid.NewGuid().ToString("N"));
    static readonly string GoldenDir = Path.Combine(Repo.Root, "saves", "golden");

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    static GameDatabase LoadDb()
    {
        var raw = RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
        return raw != null ? Distiller.Distill(raw) : GameDatabase.Empty;
    }

    SaveService NewService(out string name)
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        Skip.IfNot(File.Exists(Path.Combine(GoldenDir, "state.json")), "no golden state (run tools/golden/make_save_golden.py)");
        var dir = Path.Combine(_tmp, "saves");
        Directory.CreateDirectory(dir);
        name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        return new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), LoadDb(), () => null);
    }

    static readonly HashSet<string> Ignore = new() { "portrait", "has_portrait" };

    [SkippableFact]
    public void Fields_match_the_python_model()
    {
        var svc = NewService(out var name);
        var open = svc.Open("t", name);
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDir, "state.json")))!;
        var actual = new JsonArray(open.Model.Fields.Select(f =>
        {
            var o = new JsonObject
            {
                ["id"] = f.Id, ["group"] = f.Group, ["section"] = f.Section, ["label"] = f.Label, ["kind"] = f.Kind,
                ["value"] = f.Value, ["min"] = f.Min, ["max"] = f.Max,
            };
            if (f.Note.Length > 0) o["note"] = f.Note;
            return (JsonNode)o;
        }).ToArray());
        Assert.Null(JsonCompare.Diff(golden["fields"], actual));
        Assert.Equal(golden["parsed_nodes"]!.GetValue<int>(), open.ParsedNodes);
        Assert.Equal(golden["opaque_nodes"]!.GetValue<int>(), open.OpaqueNodes);
    }

    [SkippableFact]
    public void Upgrade_list_matches()
    {
        var svc = NewService(out var name);
        var open = svc.Open("t", name);
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDir, "state.json")))!;
        Assert.Null(JsonCompare.Diff(golden["upgrades"], JsonCompare.ToNode(open.Upgrades), Ignore));
    }

    [SkippableTheory]
    [InlineData("header")]
    [InlineData("command")]
    [InlineData("personnel")]
    [InlineData("armory")]
    [InlineData("medbay")]
    [InlineData("galaxy")]
    [InlineData("coil")]
    [InlineData("upgrades")]
    public void Screen_views_match(string screen)
    {
        var svc = NewService(out var name);
        var open = svc.Open("t", name);
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDir, "state.json")))!["view"]![screen];
        var actual = JsonCompare.ToNode(open.View)![screen];
        Assert.Null(JsonCompare.Diff(golden, actual, Ignore, "$." + screen));
    }

    // ---------------------------------------------------------------- scenarios
    static SaveAction ToAction(JsonNode a)
    {
        string S(string k) => a[k]!.GetValue<string>();
        return a["type"]!.GetValue<string>() switch
        {
            "heal_operator" => new SaveAction.HealOperator(S("guid")),
            "complete_focus_tree" => new SaveAction.CompleteFocusTree(S("guid")),
            "revive_operator" => new SaveAction.ReviveOperator(S("guid")),
            "reorder_roster" => new SaveAction.ReorderRoster(a["order"]!.AsArray().Select(x => x!.GetValue<string>()).ToList()),
            "remove_coil_upgrades" => new SaveAction.RemoveCoilUpgrades(a["changes"]!.AsArray()
                .Select(x => new CoilChange(x!["id"]!.GetValue<string>(), x["to"]!.GetValue<string>())).ToList()),
            "start_upgrade" => new SaveAction.StartUpgrade(S("id"), S("name")),
            "expedite_upgrade" => new SaveAction.ExpediteUpgrade(S("id"), S("name")),
            "start_expedite_upgrade" => new SaveAction.StartExpediteUpgrade(S("id"), S("name")),
            var t => throw new InvalidOperationException(t),
        };
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    public static IEnumerable<object[]> ScenarioNames()
    {
        var p = Path.Combine(GoldenDir, "scenarios.json");
        if (!File.Exists(p)) { yield return new object[] { "(none)" }; yield break; }
        foreach (var kv in JsonNode.Parse(File.ReadAllText(p))!.AsObject()) yield return new object[] { kv.Key };
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioNames))]
    public void Scripted_edits_produce_the_same_bytes_as_python(string scenario)
    {
        Skip.If(scenario == "(none)", "no golden scenarios");
        var svc = NewService(out var name);
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDir, "scenarios.json")))![scenario]!;
        svc.Open("t", name);
        var changes = golden["changes"]!.AsArray().Select(c =>
            new FieldChange(c!["id"]!.GetValue<string>(), c["value"]!.GetValue<double>(), c["link"]?.GetValue<bool>() ?? true)).ToList();
        var actions = golden["actions"]!.AsArray().Select(a => ToAction(a!)).ToList();

        if (golden["error"] != null)
        {
            Assert.Throws<EditException>(() => svc.Apply(changes, asCopy: true, actions: actions));
            return;
        }
        var res = svc.Apply(changes, asCopy: true, actions: actions);
        using var z = ZipFile.OpenRead(Path.Combine(_tmp, "saves", res.Written));
        byte[] Read(string entry) { using var s = z.GetEntry(entry)!.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
        var gvas = Read("SaveGame");
        Assert.Equal(golden["gvas_len"]!.GetValue<int>(), gvas.Length);
        Assert.Equal(golden["gvas"]!.GetValue<string>(), Sha(gvas));
        Assert.Equal(golden["meta"]!.GetValue<string>(), Sha(Read("SaveGameMetaData.json")));
        foreach (var kv in golden["others"]!.AsObject()) Assert.Equal(kv.Value!.GetValue<string>(), Sha(Read(kv.Key)));
    }
}
