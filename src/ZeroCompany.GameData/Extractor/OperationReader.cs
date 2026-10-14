using Newtonsoft.Json.Linq;

namespace ZeroCompany.GameData.Extractor;

/// <summary>
/// The Holotable's operations (field tasks): each recipe under <c>Recipes/FieldTasks_*</c> with its in-game name, description, how many
/// cycles it stays on offer (<c>Expiration</c>, 0 = never) and the options the player picks between.
/// </summary>
public static class OperationReader
{
    static string Text(JToken? t) => Models.GameDatabase.Plain((string?)t?["SourceString"] ?? (string?)t?["LocalizedString"]);

    /// <summary>One recipe's record, or null when it is not an operation: <c>{ recipe, name, description, expiration, kind, choices:[label] }</c>.</summary>
    public static JObject? FromObjects(string recipe, IReadOnlyDictionary<string, JObject> objs)
    {
        var main = objs.Values.FirstOrDefault(o => ((string?)o["Name"] ?? "").StartsWith("Default__", StringComparison.Ordinal));
        if (main?["Properties"] is not JObject p || p["Name"] == null) return null;
        var kind = (string?)p["RecipeTypeTag"]?["TagName"] ?? "";
        var choices = new JArray(objs.Values.Where(o => ((string?)o["Type"] ?? "").Contains("PlayerChoice", StringComparison.Ordinal))
            .OrderBy(o => (string?)o["Name"], StringComparer.Ordinal)
            .Select(o => RewardReader.ChoiceLabel((string?)o["Properties"]?["NameFormat"]?["SourceString"]))
            .Where(l => l.Length > 0));
        return new JObject
        {
            ["recipe"] = recipe, ["name"] = Text(p["Name"]), ["description"] = Text(p["Description"]),
            ["expiration"] = (int?)p["Expiration"] ?? 0, ["kind"] = kind[(kind.LastIndexOf('.') + 1)..], ["choices"] = choices,
        };
    }

    public static JArray Read(PackageReader pkg, IProgress<string> log, CancellationToken ct)
    {
        var @out = new JArray();
        var files = pkg.AssetKeys(k => k.Contains("/Recipes/FieldTasks_", StringComparison.OrdinalIgnoreCase)
                                       && !k.Contains("/RecipeTables/", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (FromObjects(Path.GetFileNameWithoutExtension(f), pkg.Objects(PackageReader.PackagePath(f))) is { } r) @out.Add(r);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Report($"  skipped {f}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        log.Report($"  {@out.Count} operations described (of {files.Count} files)");
        return @out;
    }
}
