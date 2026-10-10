using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ZeroCompany.GameData.Models;

/// <summary>
/// The editor's view of the game's own content, produced by the extractor from the user's install
/// (never shipped, never written into saves). Every lookup degrades to "unknown" so the editor still
/// works on a machine with no extracted data.
/// </summary>
public sealed class GameDatabase
{
    /// <summary>Bump when the shape changes; older files are rejected and must be re-extracted.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public DateTime ExtractedUtc { get; set; }
    public string GameDir { get; set; } = "";
    public List<UpgradeRecipe> Upgrades { get; set; } = new();
    public Dictionary<string, ItemInfo> Items { get; set; } = new();
    public Dictionary<string, EffectInfo> Effects { get; set; } = new();
    /// <summary>Ability tag -> cumulative focus needed for level 1..n (e.g. Lethal: [0,2,5,9,15,23]).</summary>
    public Dictionary<string, int[]> FocusThresholds { get; set; } = new();
    /// <summary>Coil upgrade id ('Minor.B1_A') -> in-game name and description.</summary>
    public Dictionary<string, CoilEffectInfo> CoilEffects { get; set; } = new();
    /// <summary>Coil unit code ('Striker') -> in-game unit name.</summary>
    public Dictionary<string, string> CoilUnits { get; set; } = new();
    /// <summary>Every localized "*_Name" string by key (plain text); ability names are looked up here.</summary>
    public Dictionary<string, string> NameStrings { get; set; } = new();

    // ---- derived indexes (not serialized) ------------------------------------------------
    UpgradeIndex? _index;

    [JsonIgnore]
    public UpgradeIndex UpgradeLookup => _index ??= new UpgradeIndex(Upgrades);

    public static GameDatabase Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty => Upgrades.Count == 0 && Items.Count == 0;

    static readonly Regex Markup = new("<[^>]+>", RegexOptions.Compiled);
    static readonly Regex PluralRx = new(@"\{Count\}\|plural\(one=([^,]+),\s*other=([^)]+)\)", RegexOptions.Compiled);
    static readonly Regex PlaceholderRx = new(@"\{(\d+)(%?)\}", RegexOptions.Compiled);

    /// <summary>Strip the game's rich-text markup (&lt;bold&gt;, &lt;Keyword ...&gt;) for display.</summary>
    public static string Plain(string? text) => Markup.Replace(text ?? "", "").Trim();

    /// <summary>In-game display name for an inventory item asset, or null when unknown.</summary>
    public string? ItemLabel(string asset)
    {
        if (!Items.TryGetValue(asset, out var it) || string.IsNullOrEmpty(it.Display)) return null;
        var d = it.Display.Trim();
        var m = PluralRx.Match(d);
        return (m.Success ? m.Groups[2].Value : d).Trim();
    }

    /// <summary>(title, description) of a gameplay effect, with {0}/{1} filled from its modifiers.</summary>
    public (string? Title, string Description) EffectText(string asset)
    {
        if (!Effects.TryGetValue(asset, out var e) || string.IsNullOrEmpty(e.Title)) return (null, "");
        var vals = e.Modifiers.Where(m => m.Value != null).Select(m => m.Value!.Value).ToList();
        var desc = PlaceholderRx.Replace(e.Description, m =>
        {
            int i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (i >= vals.Count) return m.Value;
            double v = vals[i];
            if (m.Groups[2].Length > 0)                       // {1%} -> shown as a percentage
            {
                if (Math.Abs(v) <= 1) v *= 100;
                return Math.Abs(v).ToString("G6", CultureInfo.InvariantCulture) + "%";
            }
            return v == Math.Floor(v) ? ((long)v).ToString(CultureInfo.InvariantCulture)
                                      : v.ToString("G6", CultureInfo.InvariantCulture);
        });
        return (e.Title, desc);
    }

    /// <summary>Display name for an ability tag like 'br.AbilityID.Class.PrecisionShot', or null.</summary>
    public string? AbilityName(string tag)
    {
        if (NameStrings.Count == 0) return null;
        var seg = tag[(tag.LastIndexOf('.') + 1)..];
        foreach (var key in new[] { $"GA_{seg}_T1_Name", $"GA_{seg}_Name", $"{seg}_Name", $"GA_{seg}_T2_Name",
                                    $"Passive_{seg}_Name", $"Aspect_{seg}_Name" })
            if (NameStrings.TryGetValue(key, out var v)) return v;
        return null;
    }

    public CoilEffectInfo? CoilEffect(string id) => CoilEffects.GetValueOrDefault(id);
    public string? CoilUnit(string unit) => CoilUnits.GetValueOrDefault(unit);
    public int[]? Thresholds(string tag) => FocusThresholds.GetValueOrDefault(tag);

    // ---- persistence ----------------------------------------------------------------------
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Load a database file; returns null when it is missing, unreadable or from another schema.</summary>
    public static GameDatabase? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var db = JsonSerializer.Deserialize<GameDatabase>(File.ReadAllText(path), JsonOpts);
            return db is { Schema: CurrentSchema } ? db : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}

public sealed class UpgradeRecipe
{
    /// <summary>Asset name; the same key the save's SoftRecipeClass ends with.</summary>
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Unused { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int Duration { get; set; }
    public List<string> RecipeTags { get; set; } = new();
    public List<string> CompletedFactTags { get; set; } = new();
    public List<RecipeRequirement> Requirements { get; set; } = new();

    public bool IsMajor => RecipeTags.Any(t => t.Contains("UpgradeMajor"));

    /// <summary>Cost, duration, Den Level and required tags in the shape the Upgrades screen wants.</summary>
    public UpgradeSummary Summary()
    {
        var cost = new Dictionary<string, int>();
        int? den = null;
        var tags = new List<string>();
        foreach (var q in Requirements)
        {
            if (q.Item != null && q.Amount != null) cost[q.Item.Replace("Resource_", "")] = q.Amount.Value;
            if (q.RosterLevel != null) den = q.RosterLevel;
            tags.AddRange(q.RequireTags);
        }
        return new UpgradeSummary(Duration > 0 ? Duration : 1, cost, den, tags, GameDatabase.Plain(Description), Title);
    }
}

public sealed record UpgradeSummary(int Duration, IReadOnlyDictionary<string, int> Cost, int? DenLevel,
    IReadOnlyList<string> RequireTags, string Description, string? Title);

public sealed class RecipeRequirement
{
    public string? Item { get; set; }
    public int? Amount { get; set; }
    public int? RosterLevel { get; set; }
    public List<string> RequireTags { get; set; } = new();
}

/// <summary>Upgrade recipes keyed by asset name, plus the fact tag -> recipe that grants it.</summary>
public sealed class UpgradeIndex
{
    public IReadOnlyDictionary<string, UpgradeRecipe> ByName { get; }
    public IReadOnlyDictionary<string, UpgradeRecipe> ByTag { get; }

    public UpgradeIndex(IEnumerable<UpgradeRecipe> rows)
    {
        var byName = new Dictionary<string, UpgradeRecipe>();
        var byTag = new Dictionary<string, UpgradeRecipe>();
        foreach (var r in rows)
        {
            byName[r.Name] = r;
            foreach (var t in r.CompletedFactTags) byTag.TryAdd(t, r);
        }
        ByName = byName; ByTag = byTag;
    }

    public UpgradeRecipe? Get(string name) => ByName.GetValueOrDefault(name);
}

public sealed class ItemInfo
{
    public string Name { get; set; } = "";
    public string Display { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Modification, Utility, Resource or Other.</summary>
    public string Kind { get; set; } = "Other";
    public string Rarity { get; set; } = "";
    public int? Tier { get; set; }
    public List<string> Tags { get; set; } = new();
}

public sealed class EffectInfo
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<EffectModifier> Modifiers { get; set; } = new();
}

public sealed class EffectModifier
{
    public string Attribute { get; set; } = "";
    public string Op { get; set; } = "";
    public double? Value { get; set; }
}

public sealed class CoilEffectInfo
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}
