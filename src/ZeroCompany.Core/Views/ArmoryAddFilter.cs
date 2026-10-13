namespace ZeroCompany.Core.Views;

/// <summary>How the Add items list is ordered.</summary>
public enum AddSort { Name, TierLow, TierHigh, Rarity, FewestOwned }

/// <summary>
/// What the Add items tab shows. Empty text, "All" and 0 mean "no filter". <see cref="Group"/> is "All", "Utility", "Mods" (every
/// weapon mod) or "Mods:Scope" (one mod slot); <see cref="Rarity"/> is "" or a rarity name; <see cref="Tier"/> is 0 or 1 to 4.
/// </summary>
public sealed record AddQuery(string Text = "", string Group = "All", int Tier = 0, string Rarity = "",
                              bool NeverOwnedOnly = false, bool QueuedOnly = false, AddSort Sort = AddSort.Name);

/// <summary>Filtering and sorting for the Armory's Add items list (about 150 items), kept apart from the screen so it can be tested.</summary>
public static class ArmoryAddFilter
{
    /// <summary>Common is the plainest, Unique the rarest. Unknown or empty rarities rank below all of them.</summary>
    public static int RarityRank(string rarity) => rarity switch { "Common" => 1, "Uncommon" => 2, "Rare" => 3, "Unique" => 4, _ => 0 };

    /// <summary>Every word must appear in the item's name, internal name, description, slot or rarity (any case).</summary>
    public static bool MatchesText(ArmoryAddView it, string text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;
        var hay = string.Join(' ', it.Name, it.Asset, it.Description, it.Slot, it.Rarity);
        return words.All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    static bool InGroup(ArmoryAddView it, string group) => group switch
    {
        "" or "All" => true,
        "Utility" => it.Kind == "Utility",
        "Mods" => it.Kind == "Modification",
        _ when group.StartsWith("Mods:", StringComparison.Ordinal) => it.Kind == "Modification" && it.Slot == group["Mods:".Length..],
        _ => true,
    };

    /// <param name="queued">How many copies of an item (by asset name) are queued to be added.</param>
    public static List<ArmoryAddView> Run(IEnumerable<ArmoryAddView> items, AddQuery q, Func<string, int> queued)
    {
        var list = items.Where(it =>
            InGroup(it, q.Group)
            && (q.Tier == 0 || it.Tier == q.Tier)
            && (q.Rarity.Length == 0 || it.Rarity == q.Rarity)
            && (!q.NeverOwnedOnly || it.Owned == 0)
            && (!q.QueuedOnly || queued(it.Asset) > 0)
            && MatchesText(it, q.Text));

        IOrderedEnumerable<ArmoryAddView> ordered = q.Sort switch
        {
            AddSort.TierLow => list.OrderBy(i => i.Tier ?? 0),
            AddSort.TierHigh => list.OrderByDescending(i => i.Tier ?? 0),
            AddSort.Rarity => list.OrderByDescending(i => RarityRank(i.Rarity)),
            AddSort.FewestOwned => list.OrderBy(i => i.Owned),
            _ => list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };
        // a stable, predictable order inside ties: name, then asset (so T1/T2/T3 of one item stay together)
        return ordered.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Asset, StringComparer.Ordinal).ToList();
    }

    /// <summary>The "Show" choices present in the data: (value, label) pairs, "All" first.</summary>
    public static List<(string Value, string Label)> Groups(IEnumerable<ArmoryAddView> items)
    {
        var all = items.ToList();
        var @out = new List<(string, string)> { ("All", "All items") };
        if (all.Any(i => i.Kind == "Utility")) @out.Add(("Utility", "Utility items"));
        if (all.Any(i => i.Kind == "Modification")) @out.Add(("Mods", "Weapon mods (all)"));
        foreach (var slot in all.Where(i => i.Kind == "Modification" && i.Slot.Length > 0).Select(i => i.Slot).Distinct().OrderBy(s => s, StringComparer.Ordinal))
            @out.Add(($"Mods:{slot}", $"Weapon mods: {slot}"));
        return @out;
    }

    /// <summary>Rarity names present in the data, plainest first.</summary>
    public static List<string> Rarities(IEnumerable<ArmoryAddView> items) =>
        items.Select(i => i.Rarity).Where(r => r.Length > 0).Distinct().OrderBy(RarityRank).ThenBy(r => r, StringComparer.Ordinal).ToList();

    /// <summary>Tier numbers present in the data, lowest first.</summary>
    public static List<int> Tiers(IEnumerable<ArmoryAddView> items) =>
        items.Where(i => i.Tier is > 0).Select(i => i.Tier!.Value).Distinct().OrderBy(t => t).ToList();
}
