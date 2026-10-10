using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core.Model;

/// <summary>
/// One editable value, addressed by the byte offset of its value ("o&lt;offset&gt;"), which is unique within a save.
/// A field may carry *linked* writes (e.g. changing available focus also shifts the total so points spent stay equal).
/// </summary>
public sealed class Field
{
    public const long IntMax = int.MaxValue;

    public string Id { get; init; } = "";
    public string Group { get; init; } = "";
    public string Section { get; init; } = "";
    public string Label { get; init; } = "";
    /// <summary>"int" or "float".</summary>
    public string Kind { get; init; } = "int";
    public double Value { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = IntMax;
    public string Note { get; init; } = "";
    public GvasNode Node { get; init; } = null!;
    public IReadOnlyList<GvasNode> Linked { get; init; } = Array.Empty<GvasNode>();
    public bool IsFloat => Kind == "float";
}

public sealed class Ability
{
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public Field? Level { get; init; }
    public Field? Spent { get; init; }
    public int[]? Thresholds { get; init; }
    public int? BaseLevel { get; init; }
}

public sealed class OperatorEffect
{
    public string Asset { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public Field? Stacks { get; set; }
    public List<Field> Magnitudes { get; } = new();
}

public sealed class Operator
{
    public string Guid { get; init; } = "";
    public string Name { get; set; } = "";
    public string Class { get; init; } = "";
    public bool Dead { get; set; }
    public string Role { get; init; } = "";
    public Field? Focus { get; set; }
    public long? TotalFocus { get; set; }
    public Field? TotalField { get; set; }
    public List<Ability> Abilities { get; set; } = new();
    public List<OperatorEffect> Effects { get; } = new();
    public long Injuries { get; set; }
}

public sealed class BondRow
{
    public string A { get; init; } = "";
    public string B { get; init; } = "";
    public Field? Level { get; set; }
    public Field? Progress { get; set; }
    public Field? Cross { get; set; }
    public Field? Next { get; set; }
    public Field? Highest { get; set; }
}

public sealed class Region
{
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public Field? Influence { get; init; }
    public Field? Contacts { get; init; }
    public Field? Reward { get; init; }
}

public sealed class ResourceEntry
{
    public Field? Field { get; init; }
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class InventoryEntry
{
    public string Asset { get; init; } = "";
    public string Label { get; init; } = "";
    public string Section { get; init; } = "";
    public string Kind { get; init; } = "";
    public int? Tier { get; init; }
    public string Rarity { get; init; } = "";
    public string Description { get; init; } = "";
    public Field? Field { get; init; }
}

/// <summary>Builds the curated field list and structured entities for one loaded save.</summary>
public sealed class SaveModel
{
    public const int IntelCap = 500;      // in-game cap for Intel (matches the reference editor)

    public GvasFile G { get; }
    public GameDatabase Db { get; }
    public List<Field> Fields { get; } = new();
    public Dictionary<string, Field> ById { get; } = new();
    public Dictionary<string, string> Names { get; } = new();
    public Dictionary<string, ResourceEntry> Resources { get; } = new();        // insertion order = save order
    public List<InventoryEntry> Inventory { get; } = new();
    public Dictionary<string, Field?> Prog { get; } = new();
    public Dictionary<string, Operator> Operators { get; } = new();
    public List<string> Roster { get; private set; } = new();
    public HashSet<string> Dead { get; } = new();
    public List<BondRow> BondRows { get; } = new();
    public List<Region> Regions { get; } = new();
    public Dictionary<string, GvasNode> Facts { get; } = new();
    public GvasNode Sd { get; }

    static readonly HashSet<string> FloatTypes = new() { "FloatProperty", "DoubleProperty" };

    public SaveModel(GvasFile g, string? metadataJson, GameDatabase? db = null)
    {
        G = g;
        Db = db ?? GameDatabase.Empty;
        LoadCharNames(metadataJson);
        Sd = g.StrategyData();
        ResourcesAndInventory();
        Progression();
        LoadOperators();
        Bonds();
        LoadRegions();
        FactValues();
    }

    // -- helpers -----------------------------------------------------------
    Field? Add(GvasNode? node, string group, string section, string label, double lo = 0, double hi = Field.IntMax,
               string note = "", IEnumerable<GvasNode>? linked = null)
    {
        if (node == null || !GvasFile.Scalars.ContainsKey(node.TName)) return null;
        var f = new Field
        {
            Id = Naming.FieldId(node), Group = group, Section = section, Label = label,
            Kind = FloatTypes.Contains(node.TName) ? "float" : "int",
            Value = G.GetDouble(node), Min = lo, Max = hi, Note = note, Node = node,
            Linked = linked?.ToList() ?? new List<GvasNode>(),
        };
        Fields.Add(f);
        ById[f.Id] = f;
        return f;
    }

    void LoadCharNames(string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson)) return;
        JsonNode? meta;
        try { meta = JsonNode.Parse(metadataJson.TrimStart('﻿').TrimEnd('\0', ' ', '\r', '\n', '\t')); }
        catch (System.Text.Json.JsonException) { return; }
        // Fallback names from class names, then overridden by the indexed names.
        if (meta?["GameInstanceMetaData"]?["characterMetaData"] is JsonArray cm)
            foreach (var e in cm.OfType<JsonObject>())
                Names[e["guid"]!.GetValue<string>().ToUpperInvariant()] = Naming.DisplayName(e["className"]?.GetValue<string>() ?? "");
        if (meta?["StrategyMetaData"]?["characterInfoMetaDatas"] is JsonArray ci)
            foreach (var e in ci.OfType<JsonObject>())
                Names[e["guid"]!.GetValue<string>().ToUpperInvariant()] = Naming.DisplayName(e["characterName"]?.GetValue<string>() ?? "");
    }

    public string NameOf(string h) => Names.TryGetValue(h, out var n) ? n : $"Unknown ({h[..8]})";

    // -- resources / inventory --------------------------------------------
    void ResourcesAndInventory()
    {
        var g = G;
        var inv = g.Child(Sd, "InventoryItemData");
        foreach (var ent in inv?.Children ?? new List<GvasNode>())
        {
            var val = ent.Children == null ? g.Child(ent, "value") : ent.Children[1];
            var asset = g.Child(val, "ItemAsset");
            var cnt = g.Child(val, "ItemCount");
            if (asset == null || cnt == null) continue;
            var path = g.GetStr(asset);
            var name = Naming.AssetName(path);
            Db.Items.TryGetValue(name, out var info);
            if (path.Contains("/Resources/"))
            {
                var stripped = name.StartsWith("Resource_", StringComparison.Ordinal) ? name["Resource_".Length..] : name;
                var label = Naming.Pretty(stripped);
                label = label switch { "Intelligence" => "Intel", "Upgrade Facility Resource" => "Facility upgrade resource", _ => label };
                label = Db.ItemLabel(name) ?? label;
                double hi = name == "Resource_Intelligence" ? IntelCap : Field.IntMax;
                var f = Add(cnt, "Resources", "Stockpile", label, hi: hi, note: info?.Description ?? "");
                Resources[name] = new ResourceEntry { Field = f, Label = label, Description = info?.Description ?? "" };
            }
            else
            {
                var section = path.Contains("Mod") ? "Modifications"
                            : path.Contains("Utility") || path.Contains("Grenade") ? "Utility" : "Other";
                var label = Db.ItemLabel(name) ?? Naming.Pretty(name);
                if (info?.Tier is int t and not 0) label += $" (T{t})";
                var f = Add(cnt, "Inventory", section, label, note: info?.Description ?? "");
                Inventory.Add(new InventoryEntry
                {
                    Asset = name, Label = label, Section = section, Kind = info?.Kind ?? section, Tier = info?.Tier,
                    Rarity = info?.Rarity ?? "", Description = info?.Description ?? "", Field = f,
                });
            }
        }
    }

    // -- progression -------------------------------------------------------
    void Progression()
    {
        var g = G;
        Prog["roster_level"] = Add(g.Child(Sd, "RosterLevel"), "Progression", "Roster", "Roster level", hi: 1000);
        Prog["roster_xp"] = Add(g.Child(Sd, "RosterXP"), "Progression", "Roster", "Roster XP");
        Prog["turn"] = Add(g.Child(Sd, "StrategyTurn"), "Progression", "Campaign", "Strategy turn",
                           note: "Display copy in the save's info file is not updated.");
        Prog["base_focus"] = Add(g.Child(Sd, "BaseTotalFocusPoints"), "Progression", "Campaign",
                                 "Base total focus points", hi: 1000);
    }

    // -- operators ---------------------------------------------------------
    IEnumerable<(string Guid, GvasNode? CharacterData, GvasNode Wrapper)> Characters()
    {
        var g = G;
        var top = g.GameInstance();
        var ab = g.Child(top, "ArchiveBytes");
        var cdw = g.Child(ab, "CharacterDataWrapper");
        var blob = cdw == null ? null : g.Child(cdw, "ArchiveBytes");
        if (blob == null) yield break;
        var guids = g.Child(blob, "CharacterGuids");
        var wraps = g.Child(blob, "ObjectWrappers");
        var gl = guids?.Children ?? new List<GvasNode>();
        var wl = wraps?.Children ?? new List<GvasNode>();
        for (int i = 0; i < Math.Min(gl.Count, wl.Count); i++)
        {
            var inner = g.Child(wl[i], "ArchiveBytes");
            var cd = inner == null ? null : g.Child(inner, "CharacterData");
            yield return (g.GuidHex(gl[i]), cd, wl[i]);
        }
    }

    static readonly Regex GeStrip = new("^GE_|_C$", RegexOptions.Compiled);
    static readonly Regex HeroRx = new(@"^Char_Hero_(.+?)_C(?:_\d+)?$", RegexOptions.Compiled);

    void LoadOperators()
    {
        var g = G;
        var focus = new Dictionary<string, GvasNode>();
        var fmap = g.Child(Sd, "CharacterFocusData");
        foreach (var ent in fmap?.Children ?? new List<GvasNode>())
            focus[g.GuidHex(ent.Children![0])] = ent.Children[1];
        var dc = g.Child(Sd, "DeadCharacters");
        foreach (var e in dc?.Children ?? new List<GvasNode>()) Dead.Add(g.GuidHex(e));
        var ro = g.Child(Sd, "Roster");
        Roster = (ro?.Children ?? new List<GvasNode>()).Select(e => g.GuidHex(e)).ToList();

        foreach (var (h, cd, wrapper) in Characters())
        {
            var clsNode = g.Child(cd, "CharacterClassName");
            var cls = clsNode != null ? g.GetStr(clsNode) : "";
            if (!Names.ContainsKey(h)) Names[h] = Naming.DisplayName(cls);
            if (Names[h].StartsWith("Custom Operator", StringComparison.Ordinal))
                Names[h] = Naming.RealName(g, wrapper) ?? Names[h];
            var name = NameOf(h);
            var sect = name + (Dead.Contains(h) ? " (dead)" : "");
            var m = HeroRx.Match(cls);
            var parts = m.Success ? m.Groups[1].Value.Split('_') : Array.Empty<string>();
            var op = new Operator
            {
                Guid = h, Name = name, Class = cls, Dead = Dead.Contains(h),
                Role = parts.Length > 0 && parts[0] == "Astromech" ? "Astromech" : parts.Length > 1 ? parts[1] : "",
            };
            Operators[h] = op;
            if (focus.TryGetValue(h, out var fv))
            {
                var tot = g.Child(fv, "TotalFocusPoints");
                var av = g.Child(fv, "AvailableFocusPoints");
                op.Focus = Add(av, "Operators", sect, "Available focus points", hi: 500,
                               note: "Total focus moves with it, so points spent stay the same.",
                               linked: tot != null ? new[] { tot } : null);
                op.TotalFocus = tot != null ? g.GetInt(tot) : null;
                op.TotalField = tot != null
                    ? Add(tot, "Operators", sect, "Total focus earned", hi: 5000, note: "Always equals focus spent + unspent.")
                    : null;
                op.Abilities = Abilities(fv, name, Dead.Contains(h));
            }
            var ge = g.Child(cd, "GameplayEffectsToPersist");
            foreach (var e in ge?.Children ?? new List<GvasNode>())
            {
                var defPath = g.GetStr(g.Child(e, "Def")!);
                var d = GeStrip.Replace(Naming.AssetName(defPath).Replace("Default__", ""), "");
                if (d.StartsWith("Injured", StringComparison.Ordinal) || d.StartsWith("ApplyDead", StringComparison.Ordinal))
                {
                    if (d.StartsWith("Injured", StringComparison.Ordinal))
                    {
                        var sc = g.Child(e, "StackCount");
                        op.Injuries = sc != null ? g.GetInt(sc) : 1;
                    }
                    continue;
                }
                var label = Naming.Pretty(d.Replace("CrossTraining_", "Cross-training: ").Replace("Progression_", "Progression: "));
                var (title, edesc) = Db.EffectText(Naming.AssetName(defPath));
                if (!string.IsNullOrEmpty(title)) label = title;
                var eff = new OperatorEffect { Asset = Naming.AssetName(defPath), Title = label, Description = edesc };
                eff.Stacks = Add(g.Child(e, "StackCount"), "Operators", sect, $"{label} - stacks", hi: 100, note: edesc);
                var mods = g.Child(e, "Modifiers");
                var ml = mods?.Children ?? new List<GvasNode>();
                for (int i = 0; i < ml.Count; i++)
                {
                    var mag = g.Child(ml[i], "EvaluatedMagnitude");
                    var sfx = ml.Count > 1 ? $" #{i + 1}" : "";
                    var mf = Add(mag, "Operators", sect, $"{label} - magnitude{sfx}", lo: -1e6, hi: 1e6,
                                 note: "Experimental: stat value the game applies.");
                    if (mf != null) eff.Magnitudes.Add(mf);
                }
                op.Effects.Add(eff);
            }
        }
    }

    /// <summary>Per-ability focus-tree state: Level (1-6) and AllocatedFocus, both plain ints.</summary>
    List<Ability> Abilities(GvasNode fv, string operatorName, bool isDead)
    {
        var g = G;
        var @out = new List<Ability>();
        var alloc = g.Child(fv, "FocusPointAllocations");
        var sect = operatorName + (isDead ? " (dead)" : "");
        foreach (var ent in alloc?.Children ?? new List<GvasNode>())
        {
            var key = ent.Children![0]; var val = ent.Children[1];
            var tagNode = g.Child(key, "TagName");
            if (tagNode == null) continue;
            var tag = g.GetStr(tagNode);
            var name = Db.AbilityName(tag) ?? Naming.Pretty(tag[(tag.LastIndexOf('.') + 1)..]);
            var lvlNode = g.Child(val, "Level");
            var levelable = g.Child(val, "bIsLevelable");
            Field? lf = null;
            var thr = Db.Thresholds(tag);
            if (lvlNode != null && (levelable == null || (bool)g.Get(levelable)))
                lf = Add(lvlNode, "Abilities", sect, $"{name} - level", lo: 1, hi: thr is { Length: > 0 } ? thr.Length : 6,
                         note: "Allocated focus and unspent focus are NOT changed automatically.");
            var sf = Add(g.Child(val, "AllocatedFocus"), "Abilities", sect, $"{name} - focus spent", hi: 500);
            var dots = tag.Count(c => c == '.');
            @out.Add(new Ability
            {
                Tag = tag, Name = name, Kind = dots >= 3 ? tag.Split('.')[2] : "", Level = lf, Spent = sf, Thresholds = thr,
                BaseLevel = lvlNode != null ? (int)g.GetInt(lvlNode) : null,
            });
        }
        return @out;
    }

    // -- bonds -------------------------------------------------------------
    void Bonds()
    {
        var g = G;
        var bonds = g.Child(Sd, "Bonds");
        const string scale = "In-game scale 0-8 = this + 4 (0 = Very low, 4 = Neutral, 8 = Very high).";
        foreach (var b in bonds?.Children ?? new List<GvasNode>())
        {
            var ga = g.GuidHex(g.Child(b, "BondCharacterAID")!);
            var gb = g.GuidHex(g.Child(b, "BondCharacterBID")!);
            var sect = $"{NameOf(ga)} & {NameOf(gb)}";
            var row = new BondRow { A = ga, B = gb };
            BondRows.Add(row);
            row.Level = Add(g.Child(b, "BondLevel"), "Bonds", sect, "Bond level", -4, 4, scale);
            row.Progress = Add(g.Child(b, "BondProgress"), "Bonds", sect, "Bond progress", 0, 1000);
            row.Cross = Add(g.Child(b, "AvailableCrossTrainings"), "Bonds", sect, "Available cross-trainings", 0, 1000);
            row.Next = Add(g.Child(b, "NextHighestCrossTrainingLevel"), "Bonds", sect, "Next cross-training level", -4, 8);
            row.Highest = Add(g.Child(b, "HighestBondLevelReached"), "Bonds", sect, "Highest level reached", -4, 4, scale);
        }
    }

    // -- galaxy ------------------------------------------------------------
    void LoadRegions()
    {
        var g = G;
        var regs = g.Child(Sd, "AvailableRegions");
        foreach (var ent in regs?.Children ?? new List<GvasNode>())
        {
            var key = ent.Children![0]; var val = ent.Children[1];
            var tn = g.Child(key, "TagName");
            var tag = tn != null ? g.GetStr(tn) : "?";
            var region = Naming.Pretty(tag[(tag.LastIndexOf('.') + 1)..]);
            Regions.Add(new Region
            {
                Tag = tag, Name = region,
                Influence = Add(g.Child(val, "Influence"), "Galaxy", region, "Influence", hi: 1000),
                Contacts = Add(g.Child(val, "Contacts"), "Galaxy", region, "Contacts", hi: 1000),
                Reward = Add(g.Child(val, "InfluenceRewardIndex"), "Galaxy", region, "Influence reward index", lo: -1, hi: 100),
            });
        }
    }

    void FactValues()
    {
        var g = G;
        var lv = g.Child(Sd, "LiteralFactValues");
        foreach (var e in lv?.Children ?? new List<GvasNode>())
        {
            var tag = g.Child(e.Children![0], "TagName");
            if (tag != null) Facts[g.GetStr(tag)] = e.Children[1];
        }
    }

    /// <summary>A literal fact value ("Facts.Values.X") or <paramref name="dflt"/> when absent/unreadable.</summary>
    public double Fact(string name, double dflt = 0)
    {
        if (!Facts.TryGetValue(name, out var node)) return dflt;
        try { return G.GetDouble(node); }
        catch (Exception e) when (e is GvasException or InvalidCastException or FormatException) { return dflt; }
    }

    public bool HasFact(string name) => Facts.ContainsKey(name);

    // -- editing support ---------------------------------------------------
    /// <summary>All in-place-editable scalar nodes, keyed by field id (for the raw editor).</summary>
    public static Dictionary<string, GvasNode> ScalarNodes(GvasFile g)
    {
        var @out = new Dictionary<string, GvasNode>();
        foreach (var n in g.Walk())
            if (GvasFile.Scalars.TryGetValue(n.TName, out var sz) && n.Size == sz) @out[Naming.FieldId(n)] = n;
        return @out;
    }
}
