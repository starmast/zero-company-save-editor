using System.Text.RegularExpressions;

namespace ZeroCompany.Core.Views;

/// <summary>What the Scenery list shows. <see cref="Condition"/> is "" (any), "Pristine", "Damaged" or "Destroyed".</summary>
public sealed record SceneryQuery(string Text = "", string Condition = "", bool QueuedOnly = false);

/// <summary>Searching and ordering the Scenery list, which has hundreds of props in a real level.</summary>
public static partial class SceneryFilter
{
    [GeneratedRegex(@"^(?<base>.*?)(?<n>\d+)?$")]
    private static partial Regex TrailingNumber();

    /// <summary>Every word must appear (any case, any order) in the prop's name, sub-level, condition or internal name.</summary>
    public static bool Matches(PropView p, string text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;
        var hay = string.Join(' ', p.Title, p.Area, p.Condition, p.Actor[(p.Actor.LastIndexOf('.') + 1)..]);
        return words.All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <param name="queued">Whether a prop (by full object path) has a destroy or restore queued.</param>
    public static List<PropView> Run(IEnumerable<PropView> props, SceneryQuery q, Func<string, bool> queued) =>
        props.Where(p => (q.Condition.Length == 0 || p.Condition == q.Condition) && (!q.QueuedOnly || queued(p.Actor)) && Matches(p, q.Text))
             .OrderBy(p => Base(p.Title), StringComparer.OrdinalIgnoreCase)
             .ThenBy(p => Number(p.Title))
             .ThenBy(p => p.Actor, StringComparer.Ordinal)
             .ToList();

    // "Concrete Planter 1x1LC A 10" sorts after "... 9": natural order for the trailing instance number
    static string Base(string title) => TrailingNumber().Match(title).Groups["base"].Value;
    static int Number(string title) => int.TryParse(TrailingNumber().Match(title).Groups["n"].Value, out var n) ? n : 0;
}
