using ZeroCompany.Core.Platform;

namespace ZeroCompany.Sdk;

public sealed record FieldChangeLine(string Path, string Label, double? From, double? To);

public sealed record FieldDiffResult(string A, string B, IReadOnlyList<FieldChangeLine> Changes, IReadOnlyList<string> Notes)
{
    public bool IsEmpty => Changes.Count == 0 && Notes.Count == 0;
}

/// <summary>What changed between two saves, by the editor's own field names: values that differ, and what appeared or went away.</summary>
public static class FieldDiff
{
    public static FieldDiffResult Compare(ZcWorkspace ws, string a, string b)
    {
        var ea = SaveEditor.Open(ws, a);
        var eb = SaveEditor.Open(ws, b);
        var va = ea.Fields.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        var vb = eb.Fields.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);

        var changes = new List<FieldChangeLine>();
        foreach (var (path, x) in va)
        {
            if (!vb.TryGetValue(path, out var y)) changes.Add(new FieldChangeLine(path, x.Label, x.Value, null));
            else if (x.Value != y.Value) changes.Add(new FieldChangeLine(path, x.Label, x.Value, y.Value));
        }
        foreach (var (path, y) in vb)
            if (!va.ContainsKey(path)) changes.Add(new FieldChangeLine(path, y.Label, null, y.Value));

        var notes = new List<string>();
        Names("roster", ea.Save.View.Personnel.Roster.Select(o => o.Name), eb.Save.View.Personnel.Roster.Select(o => o.Name), notes);
        Names("memorial", ea.Save.View.Personnel.Memorial.Select(o => o.Name), eb.Save.View.Personnel.Memorial.Select(o => o.Name), notes);
        if (ea.Save.Combat != null && eb.Save.Combat != null)
        {
            var ca = ea.Save.Combat; var cb = eb.Save.Combat;
            Names("units", ca.Units.Select(u => u.Name), cb.Units.Select(u => u.Name), notes);
            foreach (var u in ca.Units.Where(u => u.Index < cb.Units.Count && cb.Units[u.Index].Name == u.Name))
            {
                var v = cb.Units[u.Index];
                if (u.Dead != v.Dead) notes.Add($"unit {u.Index} {u.Name}: {(v.Dead ? "died" : "revived")}");
                if (u.Team != v.Team) notes.Add($"unit {u.Index} {u.Name}: team {u.Team} -> {v.Team}");
            }
            if (ca.TurnStructure?.Current != cb.TurnStructure?.Current) notes.Add($"turn structure {ca.TurnStructure?.Current} -> {cb.TurnStructure?.Current}");
            if (ca.SpawnSet?.Current != cb.SpawnSet?.Current) notes.Add($"enemy spawn set {ca.SpawnSet?.Current} -> {cb.SpawnSet?.Current}");
            if (ca.Round != cb.Round) notes.Add($"round {ca.Round} -> {cb.Round}");
            foreach (var o in cb.Objectives)
                if (ca.Objectives.FirstOrDefault(p => p.Name == o.Name) is { } p && p.Status != o.Status) notes.Add($"objective {o.Title}: {p.Status} -> {o.Status}");
            if (ca.Props.Count(p => p.Condition == "Destroyed") != cb.Props.Count(p => p.Condition == "Destroyed"))
                notes.Add($"destroyed props {ca.Props.Count(p => p.Condition == "Destroyed")} -> {cb.Props.Count(p => p.Condition == "Destroyed")}");
        }
        return new FieldDiffResult(ea.Entry.Name, eb.Entry.Name, changes.OrderBy(c => c.Path, StringComparer.Ordinal).ToList(), notes);
    }

    static void Names(string what, IEnumerable<string> a, IEnumerable<string> b, List<string> notes)
    {
        var la = a.ToList(); var lb = b.ToList();
        foreach (var n in lb.Except(la)) notes.Add($"+ {what}: {n}");
        foreach (var n in la.Except(lb)) notes.Add($"- {what}: {n}");
        if (la.Count == lb.Count && la.OrderBy(x => x).SequenceEqual(lb.OrderBy(x => x)) && !la.SequenceEqual(lb)) notes.Add($"{what} reordered");
    }
}
