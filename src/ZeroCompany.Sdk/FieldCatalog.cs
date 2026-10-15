using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Sdk;

/// <summary>One editable number with a stable dotted path, e.g. <c>resource.Intel</c> or <c>operator.neiya.ability.Kick.level</c>.</summary>
public sealed record FieldEntry(string Path, string Id, string Label, double Value, double Min, double Max, string Kind)
{
    internal FieldRef Ref => new() { Id = Id, Value = Value, Min = Min, Max = Max, Kind = Kind };
    public string Section => Path.Contains('.') ? Path[..Path.IndexOf('.')] : Path;
}

/// <summary>
/// Every value the editor's screens can change, under names that do not depend on byte offsets, so a script or an agent can say
/// <c>resource.Intel</c> instead of <c>o2897</c>. Paths are matched ignoring case, spaces and punctuation other than the dots.
/// The raw field id (<c>o2897</c>) is always accepted too.
/// </summary>
public sealed class FieldCatalog
{
    readonly OpenSave _save;
    readonly List<FieldEntry> _entries = new();
    readonly Dictionary<string, FieldEntry> _byNorm = new();
    readonly Dictionary<string, FieldEntry> _byId = new();

    public IReadOnlyList<FieldEntry> Entries => _entries;

    public static string Slug(string s)
    {
        var t = Regex.Replace(s.Trim(), @"[^\p{L}\p{N}]+", "_").Trim('_');
        return t.Length == 0 ? "x" : t;
    }

    /// <summary>"br_AbilityID_Passive_FearlessLeader" -> "Passive_FearlessLeader".</summary>
    static string AbilitySlug(string tag) => Slug(tag.StartsWith("br_AbilityID_", StringComparison.Ordinal) ? tag["br_AbilityID_".Length..] : tag);

    static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}.]", "");

    FieldCatalog(OpenSave save) { _save = save; }

    public static FieldCatalog Build(OpenSave save)
    {
        var c = new FieldCatalog(save);
        c.Walk(save.View, save.Combat);
        return c;
    }

    void Add(string path, string label, FieldRef? r)
    {
        if (r == null || _byId.ContainsKey(r.Id)) return;
        var p = path; int n = 2;
        while (_byNorm.ContainsKey(Norm(p))) p = $"{path}_{n++}";
        var e = new FieldEntry(p, r.Id, label, r.Value, r.Min, r.Max, r.Kind);
        _entries.Add(e); _byNorm[Norm(p)] = e; _byId[r.Id] = e;
    }

    void Walk(SaveView v, CombatView? combat)
    {
        foreach (var r in v.Header.Resources) Add("resource." + Slug(r.Key), r.Label, r);
        Add("level", "Level", v.Header.Level);
        Add("xp", "XP", v.Header.Xp);
        Add("turn", "Turn", v.Header.Turn);
        foreach (var (k, r) in v.Command.Progression) Add("progression." + Slug(k), k, r);

        var ops = v.Personnel.Roster.Concat(v.Personnel.Memorial).ToList();
        var dupNames = ops.GroupBy(o => Norm(Slug(o.Name))).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var op in ops)
        {
            var slug = Slug(op.Name);
            if (dupNames.Contains(Norm(slug))) slug += "_" + op.Guid[..Math.Min(6, op.Guid.Length)].ToLowerInvariant();
            var b = "operator." + slug;
            if (op.Focus != null) { Add(b + ".focus", op.Name + " focus", op.Focus); Add(b + ".total_focus", op.Name + " total focus", op.Focus.TotalRef); }
            foreach (var a in op.Abilities)
            {
                Add($"{b}.ability.{AbilitySlug(a.Tag)}.level", $"{op.Name}: {a.Name} level", a.Level);
                Add($"{b}.ability.{AbilitySlug(a.Tag)}.spent", $"{op.Name}: {a.Name} spent", a.Spent);
            }
            foreach (var e in op.Effects)
            {
                Add($"{b}.effect.{Slug(e.Asset)}.stacks", $"{op.Name}: {e.Title} stacks", e.Stacks);
                for (int i = 0; i < e.Magnitudes.Count; i++) Add($"{b}.effect.{Slug(e.Asset)}.magnitude.{i}", $"{op.Name}: {e.Title} value {i}", e.Magnitudes[i]);
            }
            foreach (var bond in op.Bonds)
            {
                var p = $"{b}.bond.{Slug(bond.PartnerName)}";
                var who = $"{op.Name} + {bond.PartnerName}";
                Add(p + ".level", who + " bond level", bond.Level);
                Add(p + ".progress", who + " bond progress", bond.Progress);
                Add(p + ".cross", who + " cross-training", bond.Cross);
                Add(p + ".highest", who + " highest bond level", bond.Highest);
            }
        }

        foreach (var it in v.Armory.Items) Add("item." + Slug(it.Asset), it.Name, it.Count);
        foreach (var r in v.Galaxy.Regions)
        {
            var p = "region." + Slug(r.Tag);
            Add(p + ".influence", r.Name + " influence", r.Influence);
            Add(p + ".contacts", r.Name + " contacts", r.Contacts);
            Add(p + ".reward", r.Name + " reward", r.Reward);
        }
        if (combat != null) WalkCombat(combat);
    }

    void WalkCombat(CombatView c)
    {
        foreach (var u in c.Units)
        {
            var b = $"unit.{u.Index}";
            Add(b + ".advantage", u.Name + " advantage", u.Advantage);
            Add(b + ".x", u.Name + " X", u.X); Add(b + ".y", u.Name + " Y", u.Y); Add(b + ".z", u.Name + " Z", u.Z);
            foreach (var a in u.Attributes) Add($"{b}.attr.{Slug(a.Name)}", $"{u.Name} {a.Info.Label}".Trim(), a.Ref);
            foreach (var a in u.Attributes) Add($"{b}.attr.{Slug(a.Set)}.{Slug(a.Name)}", $"{u.Name} {a.Name} ({a.Set})", a.Ref);
            foreach (var e in u.Effects) Add($"{b}.effect.{e.Index}.stacks", $"{u.Name}: {e.Title} stacks", e.Stacks);
        }
        foreach (var w in c.Reinforcements.Waves) Add($"wave.{w.Logic}.{w.Index}.round", $"Wave {w.Index + 1} round", w.Round);
        foreach (var w in c.Reinforcements.Configs)
        {
            var b = $"config.{w.Logic}.{w.Index}";
            Add(b + ".delay", $"Wave definition {w.Index + 1} delay", w.Delay);
            Add(b + ".spawns", $"Wave definition {w.Index + 1} times to spawn", w.TimesToSpawn);
            Add(b + ".triggered", $"Wave definition {w.Index + 1} times triggered", w.TimesTriggered);
        }
        foreach (var o in c.Objectives)
            foreach (var k in o.Counters) Add($"objective.{o.Index}.counter.{Slug(k.Label)}", $"{o.Title}: {k.Label}", k.Ref);
        foreach (var t in c.Timers)
        {
            Add($"timer.{t.Index}.remaining", t.Title + " turns left", t.Remaining);
            Add($"timer.{t.Index}.total", t.Title + " total turns", t.Total);
        }
        for (int i = 0; i < c.Hazards.Count; i++) Add($"hazard.{i}.turns", c.Hazards[i].Title + " turns left", c.Hazards[i].TurnsLeft);
        for (int i = 0; i < c.Destructibles.Count; i++)
        {
            Add($"prop.{i}.health", c.Destructibles[i].Title + " health", c.Destructibles[i].Health);
            Add($"prop.{i}.max_health", c.Destructibles[i].Title + " max health", c.Destructibles[i].MaxHealth);
        }
    }

    /// <summary>The entry for a raw field id, if the catalog names it.</summary>
    public FieldEntry? ById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The entries whose path or label contains the filter (all of them for an empty filter).</summary>
    public IEnumerable<FieldEntry> Search(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return _entries;
        var n = Norm(filter);
        return _entries.Where(e => Norm(e.Path).Contains(n) || e.Label.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The entry for a path (or a raw field id such as o2897). Throws with suggestions when nothing matches.</summary>
    public FieldEntry Resolve(string path)
    {
        path = path.Trim();
        if (_byNorm.TryGetValue(Norm(path), out var e)) return e;
        if (_byId.TryGetValue(path, out e)) return e;
        if (Regex.IsMatch(path, @"^o\d+$") && _save.Scalars.TryGetValue(path, out var node))
        {
            _save.Model.ById.TryGetValue(path, out var f);
            bool isFloat = node.TName is "FloatProperty" or "DoubleProperty";
            return new FieldEntry(path, path, f?.Label ?? "raw " + node.Name, f?.Value ?? _save.Gvas.GetDouble(node),
                f?.Min ?? double.MinValue, f?.Max ?? double.MaxValue, f?.Kind ?? (isFloat ? "float" : "int"));
        }
        var n = Norm(path);
        var near = _entries.Where(x => Norm(x.Path).Contains(n) || n.Contains(Norm(x.Path))).Take(8).Select(x => x.Path).ToList();
        if (near.Count == 0 && n.Contains('.'))
        {
            var head = n[..n.LastIndexOf('.')];
            near = _entries.Where(x => Norm(x.Path).StartsWith(head + ".")).Take(8).Select(x => x.Path).ToList();
        }
        throw new ZcException($"no field '{path}'" + (near.Count > 0 ? "; did you mean: " + string.Join(", ", near) : "; list the choices with: zc edit fields <save> <filter>"));
    }
}
