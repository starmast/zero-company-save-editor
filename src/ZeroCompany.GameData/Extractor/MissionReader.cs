using Newtonsoft.Json.Linq;

namespace ZeroCompany.GameData.Extractor;

/// <summary>
/// What each mission asset (<c>MissionData/.../SK_Brentaal_035</c>) says about itself: its in-game name, summary and objective,
/// where it is, how hard, how many operators it takes and which faction it normally fights. Shown on the Galaxy screen's Den list.
/// </summary>
public static class MissionReader
{
    static string? Tag(JToken? t) => (string?)t?["TagName"];

    static string Text(JToken? t) => Models.GameDatabase.Plain((string?)t?["SourceString"] ?? (string?)t?["LocalizedString"]);

    static string Last(string? tag) => tag == null ? "" : tag[(tag.LastIndexOf('.') + 1)..];

    /// <summary>
    /// One mission's record from its package objects, or null when it is not a mission:
    /// <c>{ asset, name, summary, objective, location, region, difficulty, faction, type, minSquad, maxSquad, lifespan }</c>.
    /// </summary>
    public static JObject? FromObjects(string asset, IReadOnlyDictionary<string, JObject> objs)
    {
        var main = objs.Values.FirstOrDefault(o => ((string?)o["Type"] ?? "").EndsWith("MissionData", StringComparison.Ordinal) && (string?)o["Name"] == asset);
        if (main?["Properties"] is not JObject p) return null;
        var ui = objs.Values.FirstOrDefault(o => (string?)o["Type"] == "BitReactorUIDataFragment")?["Properties"];
        var difficulty = (p["MissionTags"] as JArray)?.Select(t => (string?)t ?? "").FirstOrDefault(t => t.Contains("MissionDifficulty.", StringComparison.Ordinal));
        return new JObject
        {
            ["asset"] = asset,
            ["name"] = Text(ui?["DisplayName"]),
            ["summary"] = Text(ui?["ShortDescription"]),
            ["objective"] = Text((p["ObjectivesText"] as JArray)?.FirstOrDefault()),
            ["location"] = (string?)p["Location"]?["RowName"] ?? "",
            ["region"] = Last(Tag(p["RegionTag"])),
            ["difficulty"] = Last(difficulty),
            ["faction"] = Last(Tag(p["EnemyFactionTag"])),
            ["type"] = Last(Tag(p["MissionTypeTag"])),
            ["minSquad"] = (int?)p["MinSquadSize"] ?? 0,
            ["maxSquad"] = (int?)p["MaxSquadSize"] ?? 0,
            ["lifespan"] = (int?)p["LifeSpan"] ?? 0,
        };
    }

    /// <summary>Every mission asset under MissionData (the mission tables, which only list missions, are skipped).</summary>
    public static JArray Read(PackageReader pkg, IProgress<string> log, CancellationToken ct)
    {
        var @out = new JArray();
        var files = pkg.AssetKeys(k => k.Contains("/GameData/MissionData/", StringComparison.OrdinalIgnoreCase)
                                       && !k.Contains("/MissionTables/", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (FromObjects(name, pkg.Objects(PackageReader.PackagePath(f))) is { } r) @out.Add(r);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Report($"  skipped {f}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        log.Report($"  {@out.Count} missions described (of {files.Count} mission files)");
        return @out;
    }
}
