using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Actions;
using ZeroCompany.Sdk;

namespace ZeroCompany.Cli;

/// <summary>`diff`, `find` and `dump`: look inside the property tree without a decompiler.</summary>
static class SaveInspect
{
    // ---- diff ----------------------------------------------------------------------------------
    static string Generic(string path) => Regex.Replace(path, @"/\[\d+\]", "/[]");

    public static void Diff(SaveDocument a, SaveDocument b, bool all, int top)
    {
        Console.WriteLine($"A  {Path.GetFileName(a.Path)}  [{a.Comment}]");
        Console.WriteLine($"B  {Path.GetFileName(b.Path)}  [{b.Comment}]");
        if (CombatOps.IsCombatSave(a.Gvas) && CombatOps.IsCombatSave(b.Gvas)) UnitDiff(a, b);

        var la = a.Leaves(); var lb = b.Leaves();
        var diffs = la.Keys.Union(lb.Keys).Where(k => la.GetValueOrDefault(k) != lb.GetValueOrDefault(k)).ToList();
        Console.WriteLine();
        Console.WriteLine($"{diffs.Count} changed values ({la.Count} vs {lb.Count} leaves){(all ? "" : "; noise hidden, use --all")}");
        var noise = new Regex(@"/(RagdollSnapshot|SaveLoadActorWrappers|ActorsToDestroyOnReload|BlueprintCreatedComponents|CapturedActorTags|CapturedSpecTags|ScopedTags|ContextArchiveBytes|BasedMovement|CapturedRelevantAttributes)(/|$)");
        foreach (var g in diffs.Where(k => all || !noise.IsMatch(k)).GroupBy(Generic).OrderByDescending(g => g.Count()).Take(top))
        {
            var ex = g.First();
            Console.WriteLine($"  {g.Count(),5}  {g.Key}");
            Console.WriteLine($"         e.g. {Regex.Match(ex, @"\[\d+\]").Value} {la.GetValueOrDefault(ex) ?? "(absent)"}  ->  {lb.GetValueOrDefault(ex) ?? "(absent)"}");
        }
    }

    static void UnitDiff(SaveDocument a, SaveDocument b)
    {
        string Key(SaveDocument s, int i) => CombatOps.ActorName(s.Gvas, CombatOps.Units(s.Gvas)[i]);
        var ua = Enumerable.Range(0, CombatOps.Units(a.Gvas).Count).ToDictionary(i => Key(a, i));
        var ub = Enumerable.Range(0, CombatOps.Units(b.Gvas).Count).ToDictionary(i => Key(b, i));
        var ca = CombatOps.Build(a.Gvas, a.Comment); var cb = CombatOps.Build(b.Gvas, b.Comment);
        Console.WriteLine();
        Console.WriteLine($"UNITS  {ua.Count} -> {ub.Count}");
        foreach (var n in ub.Keys.Except(ua.Keys)) Console.WriteLine($"  + {n} (new)");
        foreach (var n in ua.Keys.Except(ub.Keys)) Console.WriteLine($"  - {n} (gone)");
        foreach (var n in ua.Keys.Intersect(ub.Keys))
        {
            var x = ca.Units[ua[n]]; var y = cb.Units[ub[n]];
            var ch = new List<string>();
            if (x.Team != y.Team) ch.Add($"team {x.Team} -> {y.Team}");
            if (x.Dead != y.Dead) ch.Add(x.Dead ? "revived" : "died");
            if (CombatOps.IsFriendly(a.Gvas, ua[n]) != CombatOps.IsFriendly(b.Gvas, ub[n])) ch.Add("friendly flag flipped");
            var ta = string.Join(",", (CombatOps.UnitTags(a.Gvas, ua[n]) ?? new()).Where(t => t.Contains("Team.")).Order());
            var tb = string.Join(",", (CombatOps.UnitTags(b.Gvas, ub[n]) ?? new()).Where(t => t.Contains("Team.")).Order());
            if (ta != tb) ch.Add($"team tags [{Trim(ta)}] -> [{Trim(tb)}]");
            if (x.Attr("Health")?.Value != y.Attr("Health")?.Value) ch.Add($"hp {x.Attr("Health")?.Value:0.#} -> {y.Attr("Health")?.Value:0.#}");
            if (x.Effects.Count != y.Effects.Count) ch.Add($"effects {x.Effects.Count} -> {y.Effects.Count}");
            if (ch.Count > 0) Console.WriteLine($"  ~ {n}: {string.Join("; ", ch)}");
        }
        if (ca.TurnStructure?.Current != cb.TurnStructure?.Current) Console.WriteLine($"  turn structure {ca.TurnStructure?.Current} -> {cb.TurnStructure?.Current}");
        if (ca.SpawnSet?.Current != cb.SpawnSet?.Current) Console.WriteLine($"  spawn set {ca.SpawnSet?.Current} -> {cb.SpawnSet?.Current}");
        foreach (var o in cb.Objectives)
            if (ca.Objectives.FirstOrDefault(p => p.Name == o.Name) is { } p && p.Status != o.Status) Console.WriteLine($"  objective {o.Title}: {p.Status} -> {o.Status}");
    }

    static string Trim(string s) => s.Replace("BitReactor.Team.", "");

    // ---- find ----------------------------------------------------------------------------------
    /// <summary>Every string value or property name containing <paramref name="text"/>, with the path of the property that holds it.</summary>
    public static void Find(SaveDocument s, string text, int limit)
    {
        var (hits, truncated) = s.Find(text, limit);
        foreach (var h in hits) Console.WriteLine($"  {h.Path}{(h.Value.Length > 0 ? $" = {Clip(h.Value)}" : "")}");
        if (truncated) Console.WriteLine($"  ... stopped at {limit} (use --limit)");
        Console.WriteLine($"{hits.Count} match(es) in properties; entries other than SaveGame are not searched");
    }

    static string Clip(string s) => s.Length > 200 ? s[..200] + "..." : s;

    // ---- dump ----------------------------------------------------------------------------------
    /// <summary>Print the property tree below a path such as <c>TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos/[6]</c> (empty = roots).</summary>
    public static void Dump(SaveDocument s, string path, int depth, int take)
    {
        var at = s.NodeAt(path);
        void Print(GvasNode x, int d)
        {
            Console.WriteLine($"{new string(' ', d * 2)}{x.Name} : {x.TName} {SaveDocument.Short(s.Value(x))}{(x.Children != null && d >= depth ? $"  ({x.Children.Count} children)" : "")}");
            if (d >= depth) return;
            var kids = x.Children ?? new List<GvasNode>();
            foreach (var c in kids.Take(take)) Print(c, d + 1);
            if (kids.Count > take) Console.WriteLine($"{new string(' ', (d + 1) * 2)}... {kids.Count - take} more (use --take)");
        }
        if (at != null) Print(at, 0); else foreach (var r in s.Gvas.Root) Print(r, 0);
    }
}
