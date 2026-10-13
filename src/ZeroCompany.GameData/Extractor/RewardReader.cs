using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ZeroCompany.GameData.Extractor;

/// <summary>
/// Which mission (recipe) gives which inventory items, and for which player choice. A recipe's <c>RewardTiers</c> each list the
/// requirements that unlock them (a <c>BP_RecipeInput_PlayerChoice</c> means "you picked this option") and the custom rewards they
/// grant. Jedi Meditation I, for instance, gives Serenity and Listen Through Chaos for "Meditate on the Past" and two other items for
/// "Meditate on the Present", so whoever picked one choice never had the other pair.
/// </summary>
public static class RewardReader
{
    static readonly Regex Bold = new(@"<bold>(.*?)</>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>The option text of a choice ("&lt;Bold&gt;Meditate on the Past.&lt;/&gt; ..." becomes "Meditate on the Past.").</summary>
    public static string ChoiceLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var m = Bold.Match(text);
        return Models.GameDatabase.Plain(m.Success ? m.Groups[1].Value : text);
    }

    /// <summary>
    /// One recipe's item rewards, or null when it has none:
    /// <c>{ recipe, title, tiers: [ { choices: [label...], items: [ { item, amount } ] } ] }</c> (no choices = always given).
    /// <paramref name="objs"/> are the package's objects by name, as <see cref="PackageReader.Objects"/> returns them.
    /// </summary>
    public static JObject? FromObjects(string recipe, IReadOnlyDictionary<string, JObject> objs)
    {
        var main = objs.Values.FirstOrDefault(o => ((string?)o["Name"] ?? "").StartsWith("Default__", StringComparison.Ordinal));
        if (main?["Properties"] is not JObject props || props["RewardTiers"] is not JArray tiers) return null;
        JObject? Sub(JToken? r) => objs.TryGetValue(PackageReader.InnerName((string?)r?["ObjectName"] ?? ""), out var o) ? o : null;

        var outTiers = new JArray();
        foreach (var tier in tiers)
        {
            var items = new JArray();
            foreach (var reward in ((tier["CustomRewards"] as JArray) ?? new JArray()).Select(Sub))
                if (reward?["Properties"] is JObject rp && rp["InventoryItem"] != null)
                    items.Add(new JObject { ["item"] = PackageReader.Quoted(rp["InventoryItem"]), ["amount"] = (int?)rp["Amount"] ?? 1 });
            if (items.Count == 0) continue;
            var choices = new JArray(((tier["TierRequirements"] as JArray) ?? new JArray()).Select(Sub)
                .Where(o => ((string?)o?["Type"] ?? "").Contains("PlayerChoice", StringComparison.Ordinal))
                .Select(o => ChoiceLabel((string?)o!["Properties"]?["NameFormat"]?["SourceString"]) is { Length: > 0 } l ? l : (string?)o["Properties"]?["ChoiceTag"]?["TagName"] ?? "?"));
            outTiers.Add(new JObject { ["choices"] = choices, ["items"] = items });
        }
        if (outTiers.Count == 0) return null;
        var title = Models.GameDatabase.Plain((string?)props["Name"]?["SourceString"]);
        return new JObject { ["recipe"] = recipe, ["title"] = title.Length > 0 ? title : recipe, ["tiers"] = outTiers };
    }

    /// <summary>Every mission recipe (not the base-upgrade recipes) that grants inventory items.</summary>
    public static JArray Read(PackageReader pkg, IProgress<string> log, CancellationToken ct)
    {
        var @out = new JArray();
        var files = pkg.AssetKeys(k => k.Contains("/Recipes/", StringComparison.OrdinalIgnoreCase)
                                       && !k.Contains("/Upgrade_Facility/", StringComparison.OrdinalIgnoreCase)
                                       && !k.Contains("/Unused/", StringComparison.OrdinalIgnoreCase)).ToList();
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
        log.Report($"  {@out.Count} missions give items (of {files.Count} recipe files)");
        return @out;
    }
}
