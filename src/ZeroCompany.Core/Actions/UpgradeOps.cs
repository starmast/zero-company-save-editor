using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core.Actions;

public sealed class UpgradeRow
{
    /// <summary>"r{index}" for rows in ActiveRecipes; null for completed ones.</summary>
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Group { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanStart { get; set; }
    public bool CanExpedite { get; set; }
    public string Reason { get; set; } = "";
    public long Started { get; set; }
    public long Progress { get; set; }
    // present only when the game's own recipe data is available
    public int? Duration { get; set; }
    public IReadOnlyDictionary<string, int>? Cost { get; set; }
    public string? Description { get; set; }
    public int? DenLevel { get; set; }
    public bool? DenMet { get; set; }
}

public sealed class UpgradeList
{
    public long Turn { get; set; }
    public int InProgress { get; set; }
    public List<UpgradeRow> Items { get; set; } = new();
    public long RosterLevel { get; set; }
    public bool Gamedata { get; set; }
}

/// <summary>
/// Base (facility) upgrades. An upgrade is a recipe under .../Recipes/Upgrade_Facility/. We never fake a "completed" state
/// (that would skip the game's own effects). Instead we do what the game does when you press Start: mark the recipe
/// InProgress, stamp the current turn and set the facility tag. The game then finishes it on the next turn advance.
/// </summary>
public static class UpgradeOps
{
    public const string StatusPrefix = "ERecipeStatus::";
    public const string UpgradeFacilityTag = "BitReactor.Strategy.Facilities.Upgrade";
    public const int ExpediteTurns = 10;          // progress credited / how far TurnStarted is pushed back
    public const string UpgradeDir = "/Recipes/Upgrade_Facility/";
    static readonly Regex TierRx = new(@"^(?<stem>.+_)(?<n>\d+)$", RegexOptions.Compiled);
    static readonly Regex GroupBreak = new(@"([a-z0-9])([A-Z])", RegexOptions.Compiled);

    static string Status(GvasFile g, GvasNode v)
    {
        var s = g.GetStr(g.Child(v, "Status")!);
        return s.StartsWith(StatusPrefix, StringComparison.Ordinal) ? s[StatusPrefix.Length..] : s;
    }

    static string RecipeKey(GvasFile g, GvasNode v) => g.GetStr(g.Child(v, "SoftRecipeClass")!);

    static bool IsUpgrade(string path) => path.Contains(UpgradeDir) && !path.Contains("/Unused/");

    static string PrettyGroup(string s) => GroupBreak.Replace(s.Replace("_", " "), "$1 $2");

    static string Last(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Integer property, or <paramref name="dflt"/> when the save omits it (the game skips default values).</summary>
    static long IntOr(GvasFile g, GvasNode parent, string name, long dflt = 0)
    {
        var n = g.Child(parent, name);
        return n != null ? g.GetInt(n) : dflt;
    }

    /// <summary>The save's active fact tags (native container: int32 count, then FStrings).</summary>
    public static HashSet<string> FactTags(GvasFile g, GvasNode sd)
    {
        var node = g.Child(sd, "FactTags");
        var @out = new HashSet<string>();
        if (node == null || node.Size < 4) return @out;
        try
        {
            var r = new ByteReader(g.Data, node.ValueOffset);
            int n = r.I32();
            for (int i = 0; i < n; i++) @out.Add(r.FString());
        }
        catch (Exception) { return new HashSet<string>(); }          // unexpected layout: behave as "no tags known"
        return @out;
    }

    public static UpgradeList List(GvasFile g, GameDatabase db)
    {
        var sd = g.StrategyData();
        long turn = IntOr(g, sd, "StrategyTurn");
        bool haveData = db.Upgrades.Count > 0;
        var tags = haveData ? FactTags(g, sd) : new HashSet<string>();
        long roster = IntOr(g, sd, "RosterLevel");
        var items = new List<UpgradeRow>();
        var doneNames = new HashSet<string>();
        foreach (var e in g.Child(sd, "CompletedRecipes")?.Children ?? new List<GvasNode>())
        {
            var path = RecipeKey(g, e.Children![1]);
            if (!IsUpgrade(path))  continue;
            doneNames.Add(Last(path));
            items.Add(Row(g, e.Children[1], null, "Completed", path, db, roster));
        }
        var rows = new List<(int I, GvasNode V, string Path)>();
        var active = g.Child(sd, "ActiveRecipes")?.Children ?? new List<GvasNode>();
        for (int i = 0; i < active.Count; i++)
        {
            var v = active[i].Children![1];
            var path = RecipeKey(g, v);
            if (IsUpgrade(path)) rows.Add((i, v, path));
        }
        var names = rows.Select(r => Last(r.Path)).ToHashSet();
        foreach (var (i, v, path) in rows)
        {
            var st = Status(g, v);
            var row = Row(g, v, i, st, path, db, roster);
            if (st == "Available") (row.CanStart, row.Reason) = Eligibility(path, names, doneNames, db, tags);
            else if (st == "InProgress") row.CanExpedite = row.Progress < ExpediteTurns;
            items.Add(row);
        }
        return new UpgradeList
        {
            Turn = turn, InProgress = items.Count(r => r.Status == "InProgress"), Items = items,
            RosterLevel = roster, Gamedata = haveData,
        };
    }

    static UpgradeRow Row(GvasFile g, GvasNode v, int? index, string status, string path, GameDatabase db, long roster)
    {
        var after = path.Split(UpgradeDir, 2)[1].Split('/');
        var row = new UpgradeRow
        {
            Id = index != null ? $"r{index}" : null,
            Name = Last(path),
            Title = g.GetStr(g.Child(v, "TitleProperty")!),
            Category = PrettyGroup(after[0]),
            Group = after.Length > 2 ? PrettyGroup(after[1]) : "",
            Status = status,
            Started = g.GetInt(g.Child(v, "TurnStarted")!),
            Progress = g.GetInt(g.Child(v, "InProgressTurns")!),
        };
        var rec = db.UpgradeLookup.Get(row.Name);
        if (rec != null)
        {
            var info = rec.Summary();
            if (!string.IsNullOrEmpty(info.Title)) row.Title = info.Title;      // the game's own upgrade name
            row.Duration = info.Duration;
            row.Cost = info.Cost;
            row.Description = info.Description;
            row.DenLevel = info.DenLevel;
            // In-game level = save RosterLevel + 1 (save 4 is shown as LV 5 on the upgrade screen).
            row.DenMet = info.DenLevel == null || roster + 1 >= info.DenLevel;
        }
        return row;
    }

    /// <summary>
    /// Prerequisites: the game's own fact-tag requirements when game data is available, otherwise a conservative naming rule
    /// (higher tiers need the previous tier completed).
    /// </summary>
    static (bool Ok, string Reason) Eligibility(string path, HashSet<string> activeNames, HashSet<string> doneNames,
                                                GameDatabase db, HashSet<string> tags)
    {
        var name = Last(path);
        var rec = db.UpgradeLookup.Get(name);
        if (rec != null)
        {
            var missing = rec.Summary().RequireTags.Where(t => !tags.Contains(t)).ToList();
            if (missing.Count > 0)
            {
                db.UpgradeLookup.ByTag.TryGetValue(missing[0], out var src);
                var what = !string.IsNullOrEmpty(src?.Title) ? src!.Title! : missing[0][(missing[0].LastIndexOf('.') + 1)..];
                return (false, "Needs " + what + " first");
            }
            return (true, "");
        }
        var m = TierRx.Match(name);
        if (m.Success && int.Parse(m.Groups["n"].Value) > 1)
        {
            var prev = $"{m.Groups["stem"].Value}{int.Parse(m.Groups["n"].Value) - 1}";
            if (doneNames.Contains(prev)) return (true, "");
            if (activeNames.Contains(prev)) return (false, "Complete the previous tier first");
        }
        return (true, "");
    }

    /// <summary>Return a new GvasFile with the given Available upgrade set InProgress.</summary>
    public static GvasFile Start(GvasFile g, int activeIndex, string expectName)
    {
        var sd = g.StrategyData();
        var entries = g.Child(sd, "ActiveRecipes")?.Children ?? new List<GvasNode>();
        if (activeIndex < 0 || activeIndex >= entries.Count) throw new GvasException("upgrade not found");
        var v = entries[activeIndex].Children![1];
        var path = RecipeKey(g, v);
        if (Last(path) != expectName || !IsUpgrade(path)) throw new GvasException("upgrade list changed; reopen the save");
        if (Status(g, v) != "Available") throw new GvasException($"{expectName} is not Available");
        long turn = IntOr(g, sd, "StrategyTurn");

        var baseKey = GvasFile.PathOf(v);                // re-find nodes after each resize
        GvasNode At(GvasFile gg, params string[] names) =>
            gg.FindPath(baseKey.Concat(names).ToArray()) ?? throw new GvasException($"missing {string.Join("/", names)}");

        g = g.SetString(At(g, "Status"), StatusPrefix + "InProgress");
        g.Set(At(g, "TurnStarted"), turn);               // fixed-size, in place
        var tag = At(g, "InProgressRecipeContext", "FacilityTag", "TagName");
        g = g.SetString(tag, UpgradeFacilityTag);
        // The game-written in-progress entry also carries reward tier 0.
        var tiers = At(g, "FulfilledRewardTiers");
        if ((tiers.Count ?? 0) == 0) g = g.ArrayAppendInt(tiers, 0);
        return g;
    }

    /// <summary>
    /// Make an InProgress upgrade look finished to the game's next turn tick: raise its progress counter and push TurnStarted
    /// back (in place, no size change). The game still performs the actual completion, so all real effects are applied by it.
    /// </summary>
    public static GvasFile Expedite(GvasFile g, int activeIndex, string expectName)
    {
        var sd = g.StrategyData();
        var entries = g.Child(sd, "ActiveRecipes")?.Children ?? new List<GvasNode>();
        if (activeIndex < 0 || activeIndex >= entries.Count) throw new GvasException("upgrade not found");
        var v = entries[activeIndex].Children![1];
        var path = RecipeKey(g, v);
        if (Last(path) != expectName || !IsUpgrade(path)) throw new GvasException("upgrade list changed; reopen the save");
        if (Status(g, v) != "InProgress") throw new GvasException($"{expectName} is not in progress");
        long turn = IntOr(g, sd, "StrategyTurn");
        var prog = g.Child(v, "InProgressTurns")!;
        var started = g.Child(v, "TurnStarted")!;
        g.Set(prog, Math.Max(g.GetInt(prog), ExpediteTurns));
        g.Set(started, Math.Max(0, Math.Min(g.GetInt(started), turn - ExpediteTurns)));
        return g;
    }
}
