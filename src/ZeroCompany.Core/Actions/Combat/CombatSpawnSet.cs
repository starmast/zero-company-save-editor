using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

// The mission actor's EnemySelectedSpawnSet: which threat tier (e.g. SS_Coil_Threat_10) the enemy faction draws its units from.
public static partial class CombatOps
{
    static readonly Regex SpawnSetPath = new(@"^(?<dir>.*/Faction/(?<f>[A-Za-z]+)/)SS_(?<f2>[A-Za-z]+)_Threat_(?<n>\d+)\.SS_[A-Za-z]+_Threat_\d+$", RegexOptions.Compiled);

    /// <summary>
    /// Every enemy spawn set the game ships (read from its own assets with zcassets): folder name -> (file prefix, threat tiers).
    /// The separate Coil "crisis elite" set is not a tier and is left out.
    /// </summary>
    static readonly IReadOnlyDictionary<string, (string Prefix, int[] Tiers)> KnownTiers = new Dictionary<string, (string, int[])>
    {
        ["Coil"] = ("Coil", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }),
        ["Separatist"] = ("Separatist", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }),
        ["Outlaw"] = ("Outlaw", new[] { 1, 2, 3, 4, 5 }),
        ["Imperial"] = ("Imperial", new[] { 1, 9 }),
        ["SlugRats"] = ("SlugRat", new[] { 1 }),
    };

    static GvasNode? SpawnSetNode(GvasFile g) =>
        LogicActors(g).Select(w => g.Child(LogicArchive(g, w), "EnemySelectedSpawnSet")).FirstOrDefault(n => n != null);

    /// <summary>The enemy spawn set of the mission (full object path), or null.</summary>
    public static string? EnemySpawnSetOf(GvasFile g) => SpawnSetNode(g) is { } n ? g.GetStr(n) : null;

    /// <summary>"/Game/.../Coil/SS_Coil_Threat_10.SS_Coil_Threat_10" -> "Coil threat 10".</summary>
    public static string SpawnSetLabel(string path) =>
        SpawnSetPath.Match(path) is { Success: true } m ? $"{m.Groups["f"].Value} threat {int.Parse(m.Groups["n"].Value)}" : Naming.Pretty(Naming.AssetName(path));

    /// <summary>
    /// The current set, the other tiers of its faction, then (experimental) the known tiers of the other factions.
    /// Same folder layout and naming for every faction, so a set's path is built from its faction and tier.
    /// </summary>
    public static List<string> SpawnSetChoices(string current)
    {
        var list = new List<string>();
        if (SpawnSetPath.Match(current) is { Success: true } m)
        {
            var root = m.Groups["dir"].Value[..(m.Groups["dir"].Value.IndexOf("/Faction/", StringComparison.Ordinal) + "/Faction/".Length)];
            string own = m.Groups["f"].Value;
            foreach (var faction in KnownTiers.Keys.OrderBy(k => k != own).ThenBy(k => k, StringComparer.Ordinal))
                foreach (var t in KnownTiers[faction].Tiers)
                {
                    var name = $"SS_{KnownTiers[faction].Prefix}_Threat_{t:00}";
                    list.Add($"{root}{faction}/{name}.{name}");
                }
        }
        list.Remove(current);
        list.Insert(0, current);                                  // the current set always comes first
        return list;
    }

    /// <summary>True when <paramref name="candidate"/> belongs to a different faction than <paramref name="current"/>.</summary>
    public static bool IsOtherFaction(string current, string candidate) =>
        SpawnSetPath.Match(current) is { Success: true } a && SpawnSetPath.Match(candidate) is { Success: true } b && a.Groups["f"].Value != b.Groups["f"].Value;

    /// <summary>How many encounters after the current one a mission has, given how many it lists and which one is current (0-based).</summary>
    public static int LaterEncounters(int total, int current) => Math.Max(0, total - 1 - Math.Max(0, current));

    /// <summary>
    /// Encounters of this mission that have not started yet. The threat tier is only read when an encounter starts, so it can only
    /// matter for these; the enemies and reinforcement waves of the current encounter are already fixed in the save.
    /// </summary>
    public static int EncountersLeft(GvasFile g)
    {
        var archive = MissionArchive(g);
        int total = g.Child(archive, "OrderedEncounterArray")?.Children?.Count ?? 0;
        int current = g.Child(archive, "RelevantEncounterIndex") is { } n ? (int)g.GetInt(n) : 0;       // not saved while it is still 0
        return LaterEncounters(total, current);
    }

    static void BuildSpawnSet(GvasFile g, CombatView view)
    {
        if (EnemySpawnSetOf(g) is not { } cur) return;
        var sv = new SpawnSetView { Current = cur, EncountersLeft = EncountersLeft(g) };
        foreach (var c in SpawnSetChoices(cur)) sv.Choices.Add((c, SpawnSetLabel(c) + (IsOtherFaction(cur, c) ? " (other faction)" : "")));
        view.SpawnSet = sv;
    }

    public static GvasFile SetEnemySpawnSet(GvasFile g, string set, CutInfo cut)
    {
        var n = SpawnSetNode(g) ?? throw new GvasException("this save has no enemy spawn set");
        if (!SpawnSetChoices(g.GetStr(n)).Contains(set)) throw new GvasException("that is not a known spawn set");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));
        cut.EnemySpawnSet = set;
        return g.GetStr(n) == set ? g : g.SetString(n, set);
    }
}
