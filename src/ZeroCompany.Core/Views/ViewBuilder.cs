using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core.Views;

/// <summary>
/// Regroups the flat field list the way the game presents it (header resource bar, Personnel, Command ...). Every editable
/// value is a <see cref="FieldRef"/> carrying the existing field id.
/// </summary>
public static class ViewBuilder
{
    public const int BondOffset = 4;          // in-game bond scale 0-8 = save level (-4..4) + 4
    public const int LevelOffset = 1;         // in-game "LV n" = save RosterLevel + 1

    public static readonly string[] BondWords = { "Very low", "Low", "Low", "Poor", "Neutral", "Good", "High", "High", "Very high" };

    // Header order and display names (the game's own wording).
    static readonly (string Asset, string Default)[] ResourceOrder =
    {
        ("Resource_Credits", "Credits"), ("Resource_Intelligence", "Intel"),
        ("Resource_UpgradeFacilityResource", "Capacitors"), ("Resource_Contacts", "Contacts"),
    };

    static readonly StringComparer Ord = StringComparer.Ordinal;

    public static SaveView Build(SaveModel m, JsonObject? info, IEnumerable<string> portraitGuids, UpgradeList? upgrades)
    {
        var portraits = portraitGuids.Select(p => p.ToUpperInvariant()).ToHashSet();
        return new SaveView
        {
            Header = Header(m),
            Command = Command(m, info),
            Personnel = Personnel(m, portraits),
            Armory = Armory(m),
            Medbay = Medbay(m, portraits),
            Galaxy = Galaxy(m),
            Coil = CoilUpgrades(m),
            Upgrades = upgrades != null ? UpgradesScreen(m, upgrades) : null,
        };
    }

    public static HeaderView Header(SaveModel m)
    {
        var res = new List<ResourceView>();
        foreach (var (asset, dflt) in ResourceOrder)
        {
            if (!m.Resources.TryGetValue(asset, out var r) || r.Field == null) continue;
            res.Add(new ResourceView
            {
                Key = asset.Replace("Resource_", ""), Label = string.IsNullOrEmpty(r.Label) ? dflt : r.Label,
                Description = r.Description,
                Id = r.Field.Id, Value = r.Field.Value, Min = r.Field.Min, Max = r.Field.Max, Kind = r.Field.Kind,
            });
        }
        var lvl = m.Prog.GetValueOrDefault("roster_level");
        return new HeaderView
        {
            Resources = res,
            Level = lvl == null ? null : new LevelView { Id = lvl.Id, Value = lvl.Value, Min = lvl.Min, Max = lvl.Max, Kind = lvl.Kind, Offset = LevelOffset },
            Xp = FieldRef.From(m.Prog.GetValueOrDefault("roster_xp")),
            Turn = FieldRef.From(m.Prog.GetValueOrDefault("turn")),
        };
    }

    static JsonNode? Info(JsonObject? o, string key) => o?[key]?.DeepClone();

    public static CommandView Command(SaveModel m, JsonObject? info) => new()
    {
        Save = new SaveInfoView
        {
            Title = Info(info, "comment"), World = Info(info, "worldName"), Created = Info(info, "creationTime"),
            Mode = Info(info, "gameMode"), Type = Info(info, "saveGameType"), Autosave = Info(info, "autoSaveType"),
            Difficulty = Info(info, "currentDifficultyLevel"), Permadeath = Info(info, "bPermaDeath"),
        },
        Progression = m.Prog.ToDictionary(kv => kv.Key, kv => FieldRef.From(kv.Value)),
    };

    static OperatorView Operator(SaveModel m, Operator op, HashSet<string> portraits)
    {
        var g = op.Guid;
        var bonds = new List<BondView>();
        foreach (var b in m.BondRows)
        {
            if ((g != b.A && g != b.B) || b.Level == null) continue;
            var other = b.A == g ? b.B : b.A;
            bonds.Add(new BondView
            {
                Partner = other, PartnerName = m.NameOf(other),
                PartnerInRoster = m.Roster.Contains(other) || m.Dead.Contains(other),
                Level = FieldRef.From(b.Level), Progress = FieldRef.From(b.Progress), Cross = FieldRef.From(b.Cross),
                Highest = FieldRef.From(b.Highest), GameLevel = b.Level.Value + BondOffset,
            });
        }
        bonds = bonds.OrderBy(x => -x.GameLevel).ThenBy(x => x.PartnerName, Ord).ToList();
        FocusView? focus = null;
        if (op.Focus != null)
            focus = new FocusView
            {
                Id = op.Focus.Id, Value = op.Focus.Value, Min = op.Focus.Min, Max = op.Focus.Max, Kind = op.Focus.Kind,
                Total = op.TotalFocus, TotalRef = FieldRef.From(op.TotalField),
            };
        return new OperatorView
        {
            Guid = g, Name = op.Name, Role = op.Role, Dead = op.Dead, Injuries = op.Injuries,
            HasPortrait = portraits.Contains(g),
            Focus = focus,
            Abilities = op.Abilities.Select(a => new AbilityView
            {
                Tag = a.Tag, Name = a.Name, Kind = a.Kind, Level = FieldRef.From(a.Level), Spent = FieldRef.From(a.Spent),
                CurrentLevel = a.BaseLevel, Thresholds = a.Thresholds,
            }).ToList(),
            Effects = op.Effects.Select(e => new EffectView
            {
                Asset = e.Asset, Title = e.Title, Description = e.Description, Stacks = FieldRef.From(e.Stacks),
                Magnitudes = e.Magnitudes.Select(FieldRef.From).ToList(),
            }).ToList(),
            Bonds = bonds,
        };
    }

    public static PersonnelView Personnel(SaveModel m, HashSet<string> portraits)
    {
        var ops = m.Roster.Where(g => m.Operators.ContainsKey(g) && !m.Dead.Contains(g))
                          .Select(g => Operator(m, m.Operators[g], portraits)).ToList();
        var memorial = m.Operators.Keys.Where(m.Dead.Contains).Select(g => Operator(m, m.Operators[g], portraits)).ToList();
        var gaps = FocusOps.Incomplete(m.G);
        foreach (var o in ops.Concat(memorial)) o.TreeIncomplete = gaps.ContainsKey(o.Guid);
        double crossTotal = m.BondRows.Where(b => b.Cross != null).Sum(b => b.Cross!.Value);
        return new PersonnelView { Roster = ops, Memorial = memorial, CrossTrainingTotal = crossTotal, BondWords = BondWords };
    }

    // -------------------------------------------------------------------- medbay
    static readonly (string Key, string Label)[] MedbaySlots =
    {
        ("BedOne", "Bed 1"), ("BedTwo", "Bed 2"), ("BedThree", "Bed 3"), ("BedFour", "Bed 4"), ("BactaTank", "Bacta tank"),
    };

    /// <summary>
    /// Beds, the bacta tank, costs, who is injured and who is currently being treated. Values are the game's own:
    /// <c>Facts.Values.Medbay.*</c> (slot counts) and <c>MedbayCost.*</c> (credits).
    /// </summary>
    public static MedbayView Medbay(SaveModel m, HashSet<string> portraits)
    {
        var g = m.G;
        int F(string name) => (int)Math.Round(m.Fact(name, 0));
        var injured = m.Operators.Values.Where(op => op.Injuries != 0 && !op.Dead)
            .Select(op => new InjuredView { Guid = op.Guid, Name = op.Name, Injuries = op.Injuries, HasPortrait = portraits.Contains(op.Guid) })
            .OrderBy(x => x.Name, Ord).ToList();

        var treating = new List<TreatingView>();
        foreach (var e in g.Child(m.Sd, "ActiveRecipes")?.Children ?? new List<GvasNode>())
        {
            var val = e.Children![1];
            var cls = g.Child(val, "SoftRecipeClass");
            var name = (cls != null ? g.GetStr(cls) : "");
            name = name[(name.LastIndexOf('/') + 1)..];
            if (!name.Contains("InjuryRecover") || g.GetStr(g.Child(val, "Status")!).EndsWith("Available", StringComparison.Ordinal)) continue;
            var ids = g.Child(g.Child(val, "InProgressRecipeContext"), "AssignedCharacterIDs");
            var who = (ids?.Children ?? new List<GvasNode>()).Select(x => m.NameOf(g.GuidHex(x))).ToList();
            var slot = MedbaySlots.FirstOrDefault(s => name.Contains($"_{s.Key}_")).Label ?? name;
            var started = g.Child(val, "TurnStarted");
            treating.Add(new TreatingView { Slot = slot, Operators = who, Started = started != null ? g.GetInt(started) : null });
        }
        return new MedbayView
        {
            Beds = F("Facts.Values.Medbay.TotalBeds"), Tanks = F("Facts.Values.Medbay.TotalTanks"),
            Cost = new MedbayCostView
            {
                Bed = F("Facts.Values.MedbayCost.BedSingle"), Tank = F("Facts.Values.MedbayCost.TankSingle"),
                BedDouble = F("Facts.Values.MedbayCost.BedDouble"), TankDouble = F("Facts.Values.MedbayCost.TankDouble"),
            },
            Injured = injured, Treating = treating,
        };
    }

    // --------------------------------------------------------------- armory / galaxy
    static int KindOrder(string kind) => kind switch { "Utility" => 0, "Modification" => 1, _ => 2 };

    public static ArmoryView Armory(SaveModel m)
    {
        var items = new List<ArmoryItemView>();
        foreach (var it in m.Inventory)
        {
            if (it.Field == null) continue;
            var kind = it.Section == "Utility" ? "Utility" : it.Section == "Modifications" ? "Modification" : "Other";
            items.Add(new ArmoryItemView
            {
                Asset = it.Asset, Name = it.Label, Kind = kind, Tier = it.Tier, Rarity = it.Rarity,
                Description = it.Description, Count = FieldRef.From(it.Field),
            });
        }
        items = items.OrderBy(x => KindOrder(x.Kind)).ThenBy(x => x.Name, Ord).ToList();
        var totals = items.GroupBy(x => x.Asset).ToDictionary(x => x.Key, x => x.Count());
        var seen = new Dictionary<string, int>();
        foreach (var it in items)                 // the save keeps each item instance separately
        {
            if (totals[it.Asset] <= 1) continue;
            seen[it.Asset] = seen.GetValueOrDefault(it.Asset) + 1;
            it.Name = $"{it.Name} #{seen[it.Asset]}";
        }
        return new ArmoryView { Items = items };
    }

    public static GalaxyView Galaxy(SaveModel m) => new()
    {
        Regions = m.Regions.Select(r => new RegionView
        {
            Tag = r.Tag, Name = r.Name, Influence = FieldRef.From(r.Influence), Contacts = FieldRef.From(r.Contacts),
            Reward = FieldRef.From(r.Reward),
        }).ToList(),
    };

    /// <summary>Permanent Coil enemy upgrades (gained by failing Crisis missions); each can be removed.</summary>
    public static CoilView CoilUpgrades(SaveModel m)
    {
        var rows = new List<CoilRowView>();
        foreach (var u in CoilOps.Active(m.G))
        {
            var fx = m.Db.CoilEffect(u.Id);
            rows.Add(new CoilRowView
            {
                Id = u.Id, Tier = u.Tier, Unit = m.Db.CoilUnit(u.Unit) ?? "Coil " + u.Unit,
                Name = !string.IsNullOrEmpty(fx?.Title) ? fx!.Title : u.Id.Replace(".", " ").Replace("_", " "),
                Description = fx?.Description ?? "",
            });
        }
        return new CoilView
        {
            Active = rows.OrderBy(r => r.Unit, Ord).ThenBy(r => r.Tier != "Major").ThenBy(r => r.Name, Ord).ToList(),
        };
    }

    // ------------------------------------------------------------------ upgrades
    const string TagPrefix = "BitReactor.Strategy.Facilities.Upgrade.";
    public static readonly string[] Tabs = { "Facilities", "Crew", "Weapons" };

    // Row labels as the game's upgrade screen shows them (key = tag after the tab name).
    static readonly Dictionary<string, string> RowLabels = new()
    {
        ["Crew.Cantina"] = "Teamwork", ["Crew.Combat"] = "Utility Items", ["Crew.CombatReadyness"] = "Combat Readiness",
        ["Crew.CrewQuarters"] = "Crew Quarters", ["Crew.FocusPoint"] = "Crew Focus", ["Crew.Mods"] = "Weapon Mods",
        ["Facilities.Shop"] = "Black Market", ["Facilities.ShopQuality"] = "Black Market Quality",
        ["Facilities.DroidRepair"] = "Droid Repair Bay", ["Facilities.Medbay"] = "Medbay",
        ["Facilities.Networking"] = "Networking", ["Facilities.RegionInfluenceCore"] = "Core Zone Influence",
        ["Facilities.RegionInfluenceMid"] = "Mid Zone Influence", ["Facilities.RegionInfluenceOuter"] = "Outer Zone Influence",
    };
    static readonly Dictionary<string, string[]> RowOrder = new()
    {
        ["Facilities"] = new[] { "Shop", "ShopQuality", "DroidRepair", "Medbay", "Networking", "RegionInfluenceCore", "RegionInfluenceMid", "RegionInfluenceOuter" },
        ["Crew"] = new[] { "Cantina", "Combat", "CombatReadyness", "CrewQuarters", "FocusPoint", "Mods" },
    };
    static readonly string[] WeaponOrder = { "Rifle", "Pistol", "Longarm", "Repeater" };
    static readonly string[] StatOrder = { "Damage", "CritChance", "Range", "MovementRange" };
    static readonly Dictionary<string, string> StatLabels = new()
    {
        ["CritChance"] = "Critical Chance", ["MovementRange"] = "Movement", ["Range"] = "Range & Overwatch",
    };

    static string PrettyOne(string s) => Regex.Replace(s, "([a-z0-9])([A-Z])", "$1 $2").Trim();

    static (string Tab, string Key, string Label) RowOf(UpgradeRecipe? rec, UpgradeRow row)
    {
        var tags = (rec?.RecipeTags ?? new List<string>())
            .Where(t => t.StartsWith(TagPrefix, StringComparison.Ordinal) && !t.Contains("UpgradeMajor")).ToList();
        if (tags.Count > 0)
        {
            var rest = tags[0][TagPrefix.Length..];                // e.g. Crew.FocusPoint / Weapons.Rifle.Damage
            int dot = rest.IndexOf('.');
            var tab = dot < 0 ? rest : rest[..dot];
            var key = dot < 0 ? rest : rest[(dot + 1)..];
            if (tab == "Weapons")
            {
                int d2 = key.IndexOf('.');
                var weapon = d2 < 0 ? key : key[..d2];
                var stat = d2 < 0 ? "" : key[(d2 + 1)..];
                return (tab, key, $"{weapon} {(StatLabels.TryGetValue(stat, out var sl) ? sl : PrettyOne(stat))}".Trim());
            }
            return (tab, key, RowLabels.TryGetValue(rest, out var rl) ? rl : PrettyOne(key));
        }
        var cat = string.IsNullOrEmpty(row.Category) ? "Facilities" : row.Category;
        var t2 = Tabs.Contains(cat) ? cat : "Facilities";
        var grp = string.IsNullOrEmpty(row.Group) ? cat : row.Group;
        return (t2, grp, grp);
    }

    static (int, int, string) RowSort(string tab, string key)
    {
        if (tab == "Weapons")
        {
            int d = key.IndexOf('.');
            var weapon = d < 0 ? key : key[..d];
            var stat = d < 0 ? "" : key[(d + 1)..];
            int wi = Array.IndexOf(WeaponOrder, weapon), si = Array.IndexOf(StatOrder, stat);
            return (wi >= 0 ? wi : 99, si >= 0 ? si : 99, key);
        }
        int i = RowOrder.TryGetValue(tab, out var order) ? Array.IndexOf(order, key) : -1;
        return (i >= 0 ? i : 99, 0, key);
    }

    public static UpgradesView UpgradesScreen(SaveModel m, UpgradeList info)
    {
        var db = m.Db;
        var tabs = Tabs.ToDictionary(t => t, _ => new Dictionary<string, UpgradeRowView>());
        var slots = new List<UpgradeNodeView>();
        foreach (var r in info.Items)
        {
            var rec = db.UpgradeLookup.Get(r.Name);
            var (tab, key, label) = RowOf(rec, r);
            var node = new UpgradeNodeView
            {
                Name = r.Name, Id = r.Id, Title = r.Title, Description = r.Description ?? "", Status = r.Status,
                Den = r.DenLevel, DenMet = r.DenMet ?? true, Major = rec != null && rec.IsMajor,
                Duration = r.Duration, Cost = r.Cost ?? new Dictionary<string, int>(),
                CanStart = r.CanStart, CanExpedite = r.CanExpedite, Reason = r.Reason, Started = r.Started, Progress = r.Progress,
                Remaining = r.Status == "InProgress" && r.Duration is > 0 ? r.Duration - r.Progress : null,
            };
            if (!tabs.TryGetValue(tab, out var rows)) tabs[tab] = rows = new Dictionary<string, UpgradeRowView>();
            if (!rows.TryGetValue(key, out var row)) rows[key] = row = new UpgradeRowView { Key = key, Label = label };
            row.Nodes.Add(node);
            if (r.Status == "InProgress") slots.Add(node);
        }
        var outTabs = new List<UpgradeTabView>();
        foreach (var t in Tabs)
        {
            var rows = tabs[t].Values.OrderBy(x => RowSort(t, x.Key).Item1).ThenBy(x => RowSort(t, x.Key).Item2)
                                     .ThenBy(x => RowSort(t, x.Key).Item3, Ord).ToList();
            foreach (var row in rows)
            {
                var sorted = row.Nodes.OrderBy(n => n.Den ?? 0).ThenBy(n => n.Title ?? "", Ord).ToList();
                row.Nodes.Clear();
                row.Nodes.AddRange(sorted);
            }
            outTabs.Add(new UpgradeTabView { Key = t, Rows = rows });
        }
        return new UpgradesView
        {
            Turn = info.Turn, InProgress = info.InProgress, Level = info.RosterLevel + 1, Gamedata = info.Gamedata,
            Tabs = outTabs, Slots = slots,
        };
    }
}
