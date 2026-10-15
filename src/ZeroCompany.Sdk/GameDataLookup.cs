using ZeroCompany.Core.Actions;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Sdk;

/// <summary>A name the game defines, as <c>zc edit</c> accepts it.</summary>
public sealed record NamedValue(string Id, string Label, string Detail);

/// <summary>
/// The names that edits take (items to add, upgrades, effects, Coil upgrades, enemy spawn sets), read from the extracted game
/// database. The same data feeds <see cref="SaveEditor"/>, so a name listed here is a name it accepts.
/// </summary>
public static class GameDataLookup
{
    public static readonly string[] Kinds = { "items", "upgrades", "effects", "coil", "spawn-sets" };

    public static List<NamedValue> List(GameDatabase db, string kind, string filter = "")
    {
        IEnumerable<NamedValue> all = kind.ToLowerInvariant() switch
        {
            "items" => db.Items.Where(kv => kv.Value.Kind is "Utility" or "Modification")
                .Select(kv => new NamedValue(kv.Key, db.ItemLabel(kv.Key) ?? kv.Key,
                    $"{kv.Value.Kind}{(kv.Value.Tier is int t and not 0 ? $", tier {t}" : "")}{(kv.Value.Rarity.Length > 0 ? ", " + kv.Value.Rarity : "")}")),
            "upgrades" => db.Upgrades.Where(u => !u.Unused)
                .Select(u => new NamedValue(u.Name, GameDatabase.Plain(u.Title).Length > 0 ? GameDatabase.Plain(u.Title) : u.Name, $"{u.Duration} cycles{(u.IsMajor ? ", major" : "")}")),
            "effects" => db.Effects.Select(kv => new NamedValue(kv.Key, GameDatabase.Plain(kv.Value.Title).Length > 0 ? GameDatabase.Plain(kv.Value.Title) : kv.Key, GameDatabase.Plain(kv.Value.Description))),
            "coil" => db.CoilEffects.Select(kv => new NamedValue(kv.Key, GameDatabase.Plain(kv.Value.Title).Length > 0 ? GameDatabase.Plain(kv.Value.Title) : kv.Key, GameDatabase.Plain(kv.Value.Description))),
            "spawn-sets" => CombatOps.KnownSpawnTiers().Select(s => new NamedValue(s.Label, s.Label, s.Faction)),
            _ => throw new ZcException($"unknown kind '{kind}' (one of: {string.Join(", ", Kinds)})"),
        };
        if (kind.Equals("spawn-sets", StringComparison.OrdinalIgnoreCase) == false && db.IsEmpty)
            throw new ZcException("no game data yet (run: zc data extract)");
        var hits = all.Where(n => filter.Length == 0
                || n.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || n.Label.Contains(filter, StringComparison.OrdinalIgnoreCase));
        return kind.Equals("spawn-sets", StringComparison.OrdinalIgnoreCase)
            ? hits.ToList()                                                      // already in faction and tier order
            : hits.OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
