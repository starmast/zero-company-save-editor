using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.GameData.Distill;

/// <summary>What the CUE4Parse reader hands over: raw property bags exactly as the engine serialises them.</summary>
public sealed class RawGameData
{
    /// <summary>Recipe records as produced by <c>RecipeReader</c> (name, tiers[requirements...]).</summary>
    public JArray Upgrades { get; set; } = new();
    /// <summary>{ assetName: { path, objects:[{Type, Name, Template, Properties}] } } per family.</summary>
    public JObject Items { get; set; } = new();
    public JObject Effects { get; set; } = new();
    public JObject Focus { get; set; } = new();
    public JObject Crisis { get; set; } = new();
    /// <summary>namespace -> key -> text from Game.locres.</summary>
    public Dictionary<string, Dictionary<string, string>> Strings { get; set; } = new();

    /// <summary>Load the dev-time <c>gamedata/*.json</c> dumps written by earlier extractor versions (used by tests).</summary>
    public static RawGameData? LoadLegacyDirectory(string dir)
    {
        static JToken? Read(string path) => File.Exists(path) ? JToken.Parse(File.ReadAllText(path)) : null;
        var up = Read(Path.Combine(dir, "upgrades.json")) as JArray;
        if (up == null) return null;
        var raw = new RawGameData { Upgrades = up };
        raw.Items = Read(Path.Combine(dir, "raw_items.json")) as JObject ?? new();
        raw.Effects = Read(Path.Combine(dir, "raw_effects.json")) as JObject ?? new();
        raw.Focus = Read(Path.Combine(dir, "raw_focus.json")) as JObject ?? new();
        raw.Crisis = Read(Path.Combine(dir, "raw_crisis.json")) as JObject ?? new();
        if (Read(Path.Combine(dir, "strings_en.json")) is JObject s)
            foreach (var ns in s.Properties())
                raw.Strings[ns.Name] = ((JObject)ns.Value).Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? "");
        return raw;
    }
}

/// <summary>
/// Turns raw engine property bags into the typed <see cref="GameDatabase"/>. All knowledge of the game's
/// asset layout (UI fragments, modifier shapes, tag formats) lives here, so a game update breaks one place.
/// </summary>
public static class Distiller
{
    // unit code in the crisis tag -> key of its in-game name
    static readonly (string Unit, string Key)[] CrisisUnitKeys =
    {
        ("B1", "Name_First_B1_Coil"), ("B2", "Name_First_B2_Coil"), ("BXM", "Name_First_BXMarauder_Coil"),
        ("BXS", "Name_First_BXSniper_Coil"), ("Brute", "Name_First_CoilBrute"),
        ("Captain", "Name_First_CoilCaptain"), ("Enforcer", "Name_First_CoilEnforcer"),
        ("Guardian", "Name_First_CoilGuardian"), ("Seer", "Name_First_CoilSeer"),
        ("Striker", "Name_First_CoilStriker"),
    };

    static readonly Regex TierRx = new(@"_T(\d+)$", RegexOptions.Compiled);
    static readonly Regex CrisisTagRx = new(@"BitReactor\.Design\.Crisis\.((?:Major|Minor)\.\w+)$", RegexOptions.Compiled);

    public static GameDatabase Distill(RawGameData raw, string gameDir = "")
    {
        var flat = Flatten(raw.Strings);
        return new GameDatabase
        {
            ExtractedUtc = DateTime.UtcNow,
            GameDir = gameDir,
            Upgrades = Upgrades(raw.Upgrades),
            Items = Items(raw.Items),
            Effects = Effects(raw.Effects),
            FocusThresholds = Thresholds(raw.Focus),
            CoilEffects = Crisis(raw.Crisis),
            CoilUnits = CrisisUnitKeys.Where(k => flat.ContainsKey(k.Key)).ToDictionary(k => k.Unit, k => flat[k.Key]),
            NameStrings = flat.Where(kv => kv.Key.EndsWith("_Name", StringComparison.Ordinal))
                              .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                              .ToDictionary(kv => kv.Key, kv => GameDatabase.Plain(kv.Value)),
        };
    }

    /// <summary>key -> text; the first namespace (ordinal order) wins on duplicate keys.</summary>
    static Dictionary<string, string> Flatten(Dictionary<string, Dictionary<string, string>> strings)
    {
        var flat = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ns in strings.Keys.OrderBy(k => k, StringComparer.Ordinal))
            foreach (var (k, v) in strings[ns]) flat.TryAdd(k, v);
        return flat;
    }

    // ---- small JSON helpers (mirror Python truthiness / dict.get) ------------------------------
    static bool Truthy(JToken? t) => t switch
    {
        null => false,
        JObject o => o.Count > 0,
        JArray a => a.Count > 0,
        JValue v => v.Type switch
        {
            JTokenType.Null => false,
            JTokenType.String => ((string?)v.Value)?.Length > 0,
            JTokenType.Boolean => (bool)v.Value!,
            JTokenType.Integer or JTokenType.Float => Convert.ToDouble(v.Value) != 0,
            _ => true,
        },
        _ => true,
    };

    /// <summary>Source string of a localised text property ({"SourceString": ...}).</summary>
    static string Text(JToken? tok) =>
        tok is JObject o ? NonEmpty((string?)o["SourceString"]) ?? NonEmpty((string?)o["CultureInvariantString"]) ?? "" : "";

    static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    static JObject Props(JToken obj) => (obj["Properties"] as JObject) ?? new JObject();

    static IEnumerable<JToken> Objects(JToken asset) => asset["objects"] as JArray ?? new JArray();

    static IEnumerable<JProperty> Assets(JObject family) => family.Properties();

    // ---- upgrades -----------------------------------------------------------------------------
    static List<UpgradeRecipe> Upgrades(JArray rows)
    {
        var list = new List<UpgradeRecipe>();
        foreach (var r in rows.OfType<JObject>())
        {
            var rec = new UpgradeRecipe
            {
                Name = (string?)r["name"] ?? "",
                Path = (string?)r["path"] ?? "",
                Unused = (bool?)r["unused"] ?? false,
                Title = (string?)r["title"],
                Description = (string?)r["description"],
                Duration = r["duration"] is JValue { Value: not null } d ? Convert.ToInt32(d.Value) : 0,
                RecipeTags = Strings(r["recipeTags"]),
                CompletedFactTags = Strings(r["completedFactTags"]),
            };
            foreach (var tier in (r["tiers"] as JArray) ?? new JArray())
                foreach (var q in (tier["requirements"] as JArray) ?? new JArray())
                {
                    var req = new RecipeRequirement
                    {
                        Item = (string?)q["item"],
                        Amount = q["amount"] is JValue { Value: not null } a ? Convert.ToInt32(a.Value) : null,
                        RosterLevel = q["rosterLevel"] is JValue { Value: not null } l ? Convert.ToInt32(l.Value) : null,
                        RequireTags = Strings(q["requireTags"]),
                    };
                    rec.Requirements.Add(req);
                }
            list.Add(rec);
        }
        return list;
    }

    static List<string> Strings(JToken? t) =>
        (t as JArray)?.Select(x => (string?)x ?? "").ToList() ?? new List<string>();

    // ---- inventory items ----------------------------------------------------------------------
    static Dictionary<string, ItemInfo> Items(JObject family)
    {
        var @out = new Dictionary<string, ItemInfo>();
        foreach (var a in Assets(family))
        {
            var asset = a.Name;
            string display = "", desc = "";
            var tags = new List<string>();
            foreach (var o in Objects(a.Value))
            {
                var p = Props(o);
                if ((string?)o["Type"] == "BitReactorUIDataFragment" && display.Length == 0)
                {
                    display = Text(p["DisplayName"]);
                    desc = Text(p["ShortDescription"]);
                }
                if (p["ItemTags"] is JArray it)
                    tags.AddRange(it.Where(t => t.Type == JTokenType.String).Select(t => (string)t!));
            }
            var rarityTag = tags.FirstOrDefault(t => t.Contains(".Rarity."));
            var tier = TierRx.Match(asset);
            @out[asset] = new ItemInfo
            {
                Name = asset,
                Display = display,
                Description = GameDatabase.Plain(desc),
                Kind = asset.Contains("Modification") ? "Modification"
                     : asset.Contains("UtilityItem") ? "Utility"
                     : asset.StartsWith("Resource_", StringComparison.Ordinal) ? "Resource" : "Other",
                Rarity = rarityTag == null ? "" : rarityTag[(rarityTag.LastIndexOf('.') + 1)..],
                Tier = tier.Success ? int.Parse(tier.Groups[1].Value) : null,
                Tags = tags.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList(),
            };
        }
        return @out;
    }

    // ---- gameplay effects ---------------------------------------------------------------------
    static Dictionary<string, EffectInfo> Effects(JObject family)
    {
        var @out = new Dictionary<string, EffectInfo>();
        foreach (var a in Assets(family))
        {
            string title = "", desc = "";
            var mods = new List<EffectModifier>();
            foreach (var o in Objects(a.Value))
            {
                var p = Props(o);
                if (title.Length == 0 && (Truthy(p["Title"]) || Truthy(p["DisplayName"]) || Truthy(p["Name"])))
                {
                    title = Text(Truthy(p["Title"]) ? p["Title"] : Truthy(p["DisplayName"]) ? p["DisplayName"] : p["Name"]);
                    desc = Text(Truthy(p["Description"]) ? p["Description"] : p["ShortDescription"]);
                }
                if (((string?)o["Type"] ?? "").EndsWith("_C", StringComparison.Ordinal) && p["Modifiers"] is JArray ma)
                    foreach (var m in ma)
                    {
                        var attr = (string?)m["Attribute"]?["AttributeName"] ?? "";
                        var magTok = m["ModifierMagnitude"]?["ScalableFloatMagnitude"]?["Value"];
                        var opStr = m["ModifierOp"]?.ToString() ?? "";
                        mods.Add(new EffectModifier
                        {
                            Attribute = attr,
                            Op = opStr.Split("::")[^1],
                            Value = magTok is JValue { Value: not null } mv && mv.Type is JTokenType.Integer or JTokenType.Float
                                ? Convert.ToDouble(mv.Value) : null,
                        });
                    }
            }
            @out[a.Name] = new EffectInfo { Name = a.Name, Title = title, Description = GameDatabase.Plain(desc), Modifiers = mods };
        }
        return @out;
    }

    // ---- focus tree costs ---------------------------------------------------------------------
    static Dictionary<string, int[]> Thresholds(JObject family)
    {
        var @out = new Dictionary<string, int[]>();
        foreach (var a in Assets(family))
            foreach (var o in Objects(a.Value))
            {
                if (Props(o)["FocusPointLevelThresholds"] is not JArray list) continue;
                foreach (var e in list)
                {
                    var tag = (string?)e["Key"]?["TagName"];
                    var rows = e["Value"]?["FocusPointLevelThresholds"] as JArray;
                    if (string.IsNullOrEmpty(tag) || rows == null || rows.Count == 0) continue;
                    var pairs = rows.Select(r => (K: Convert.ToInt32(Convert.ToDouble((string?)r["Key"] ?? "0", System.Globalization.CultureInfo.InvariantCulture)),
                                                  V: Convert.ToInt32(Convert.ToDouble((string?)r["Value"] ?? "0", System.Globalization.CultureInfo.InvariantCulture))))
                                    .OrderBy(x => x.K).ThenBy(x => x.V).ToList();
                    @out[tag] = pairs.Select(x => x.V).ToArray();
                }
            }
        return @out;
    }

    // ---- Coil crisis upgrades -----------------------------------------------------------------
    static Dictionary<string, CoilEffectInfo> Crisis(JObject family)
    {
        var @out = new Dictionary<string, CoilEffectInfo>();
        foreach (var a in Assets(family))
        {
            if (!a.Name.StartsWith("GE_CrisisEffect_", StringComparison.Ordinal)) continue;
            string tag = "", title = "", desc = "";
            foreach (var o in Objects(a.Value))
            {
                var p = Props(o);
                if (tag.Length == 0) tag = (string?)p["StatusEffectTag"]?["TagName"] ?? "";
                if (title.Length == 0) title = Text(p["PreviewTitle"]) is { Length: > 0 } t1 ? t1 : Text(p["DisplayableEffectName"]);
                if (desc.Length == 0) desc = Text(p["PreviewDescription"]) is { Length: > 0 } d1 ? d1 : Text(p["GenericDescription"]);
            }
            var m = CrisisTagRx.Match(tag);
            if (m.Success && title.Length > 0)
                @out[m.Groups[1].Value] = new CoilEffectInfo { Title = title, Description = GameDatabase.Plain(desc) };
        }
        return @out;
    }
}
