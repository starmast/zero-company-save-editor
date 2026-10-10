using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Save;

/// <summary>One line of the "what will change" list.</summary>
public sealed record PendingLine(string Where, string Label, double? From, double? To, string? Text);

/// <summary>
/// The open save's pending (unapplied) edits and actions. Nothing is written until the user applies; this is the single
/// source of truth the screens edit and the action bar summarizes.
/// </summary>
public sealed class PendingChanges
{
    readonly Dictionary<string, double> _edits = new();
    readonly Dictionary<string, FieldRef> _refs = new();
    readonly HashSet<string> _noLink = new();
    readonly List<string> _starts = new(), _expedites = new(), _heals = new(), _treeFixes = new(), _revives = new();
    readonly Dictionary<string, string> _coil = new();

    public event Action? Changed;

    /// <summary>Starting an upgrade also expedites it (one tick to finish).</summary>
    public bool AlsoExpedite { get; set; }

    /// <summary>Pending roster order, or null when it matches the save.</summary>
    public IReadOnlyList<string>? RosterOrderOverride { get; private set; }

    public IReadOnlyDictionary<string, double> Edits => _edits;
    public IReadOnlyList<string> Starts => _starts;
    public IReadOnlyList<string> Expedites => _expedites;
    public IReadOnlyList<string> Heals => _heals;
    public IReadOnlyList<string> TreeFixes => _treeFixes;
    public IReadOnlyList<string> Revives => _revives;
    public IReadOnlyDictionary<string, string> CoilChanges => _coil;

    public int Count => _edits.Count + _starts.Count + _expedites.Count + _heals.Count + _revives.Count
                        + _treeFixes.Count + _coil.Count + (RosterOrderOverride != null ? 1 : 0);

    public int ActionCount => _starts.Count + _expedites.Count + _heals.Count + _revives.Count + _treeFixes.Count
                              + _coil.Count + (RosterOrderOverride != null ? 1 : 0);

    void Raise() => Changed?.Invoke();

    public double GetValue(FieldRef r) => _edits.TryGetValue(r.Id, out var v) ? v : r.Value;
    public bool IsChanged(FieldRef r) => _edits.ContainsKey(r.Id);
    public bool IsChanged(string id) => _edits.ContainsKey(id);
    public bool HasNoLink(string id) => _noLink.Contains(id);

    public static bool Valid(FieldRef r, double v) =>
        double.IsFinite(v) && v >= r.Min && v <= r.Max && (r.Kind == "float" || v == Math.Floor(v));

    /// <summary>Record an edit in save units. A value equal to the original removes the pending edit.</summary>
    public void SetEdit(FieldRef r, double value, bool link = true)
    {
        _refs[r.Id] = r;
        if (value == r.Value) { _edits.Remove(r.Id); _noLink.Remove(r.Id); }
        else
        {
            _edits[r.Id] = value;
            if (link) _noLink.Remove(r.Id); else _noLink.Add(r.Id);
        }
        Raise();
    }

    public void Revert(FieldRef r)
    {
        _edits.Remove(r.Id); _noLink.Remove(r.Id);
        Raise();
    }

    public void Discard()
    {
        Clear();
        Raise();
    }

    /// <summary>Forget everything without notifying (used when a save is (re)opened).</summary>
    public void Clear()
    {
        _edits.Clear(); _noLink.Clear(); _refs.Clear(); _starts.Clear(); _expedites.Clear(); _heals.Clear();
        _revives.Clear(); _treeFixes.Clear(); _coil.Clear(); RosterOrderOverride = null;
    }

    static void Toggle(List<string> set, string id, bool? on)
    {
        bool want = on ?? !set.Contains(id);
        if (want) { if (!set.Contains(id)) set.Add(id); } else set.Remove(id);
    }

    public void ToggleStart(string id, bool? on = null) { Toggle(_starts, id, on); Raise(); }
    public void ToggleExpedite(string id, bool? on = null) { Toggle(_expedites, id, on); Raise(); }
    public void ToggleHeal(string guid, bool? on = null) { Toggle(_heals, guid, on); Raise(); }
    public void ToggleTreeFix(string guid, bool? on = null) { Toggle(_treeFixes, guid, on); Raise(); }
    public void ToggleRevive(string guid, bool? on = null) { Toggle(_revives, guid, on); Raise(); }

    /// <summary>Take a Coil upgrade away ("Available" or "Prevented"), or pass null to keep it.</summary>
    public void SetCoil(string id, string? to)
    {
        if (to == null) _coil.Remove(id); else _coil[id] = to;
        Raise();
    }

    // ---- roster order ----------------------------------------------------------------
    public IReadOnlyList<string> RosterOrder(SaveView view) =>
        RosterOrderOverride ?? view.Personnel.Roster.Select(o => o.Guid).ToList();

    void SetRosterOrder(IReadOnlyList<string> order, SaveView view)
    {
        var saved = view.Personnel.Roster.Select(o => o.Guid).ToList();
        RosterOrderOverride = order.SequenceEqual(saved) ? null : order.ToList();
        Raise();
    }

    /// <summary>Swap one operator with its neighbour.</summary>
    public void MoveOperator(string guid, int delta, SaveView view)
    {
        var order = RosterOrder(view).ToList();
        int i = order.IndexOf(guid), j = i + delta;
        if (i < 0 || j < 0 || j >= order.Count) return;
        (order[i], order[j]) = (order[j], order[i]);
        SetRosterOrder(order, view);
    }

    /// <summary>Drop <paramref name="guid"/> so that <paramref name="index"/> other operators come before it.</summary>
    public void MoveOperatorTo(string guid, int index, SaveView view)
    {
        var cur = RosterOrder(view);
        if (!cur.Contains(guid)) return;
        var order = cur.Where(g => g != guid).ToList();
        order.Insert(Math.Clamp(index, 0, order.Count), guid);
        SetRosterOrder(order, view);
    }

    // ---- applying --------------------------------------------------------------------
    public List<FieldChange> BuildChanges() =>
        _edits.Select(kv => new FieldChange(kv.Key, kv.Value, !_noLink.Contains(kv.Key))).ToList();

    public List<SaveAction> BuildActions(UpgradeList upgrades)
    {
        string NameOf(string id) => upgrades.Items.First(x => x.Id == id).Name;
        var @out = new List<SaveAction>();
        @out.AddRange(_starts.Select(id => AlsoExpedite
            ? (SaveAction)new SaveAction.StartExpediteUpgrade(id, NameOf(id)) : new SaveAction.StartUpgrade(id, NameOf(id))));
        @out.AddRange(_expedites.Select(id => (SaveAction)new SaveAction.ExpediteUpgrade(id, NameOf(id))));
        @out.AddRange(_heals.Select(g => (SaveAction)new SaveAction.HealOperator(g)));
        @out.AddRange(_treeFixes.Select(g => (SaveAction)new SaveAction.CompleteFocusTree(g)));
        @out.AddRange(_revives.Select(g => (SaveAction)new SaveAction.ReviveOperator(g)));
        if (RosterOrderOverride != null) @out.Add(new SaveAction.ReorderRoster(RosterOrderOverride));
        if (_coil.Count > 0) @out.Add(new SaveAction.RemoveCoilUpgrades(_coil.Select(kv => new CoilChange(kv.Key, kv.Value)).ToList()));
        return @out;
    }

    /// <summary>Human-readable list of everything pending, for the action bar.</summary>
    public List<PendingLine> Describe(OpenSave save)
    {
        var @out = new List<PendingLine>();
        foreach (var (id, to) in _edits)
        {
            if (save.Model.ById.TryGetValue(id, out var f))
                @out.Add(new PendingLine(f.Section.Length > 0 ? f.Section : f.Group, f.Label, f.Value, to, null));
            else @out.Add(new PendingLine("Raw", id, null, to, null));
        }
        var v = save.View;
        string Op(string guid) => v.Personnel.Roster.Concat(v.Personnel.Memorial).FirstOrDefault(o => o.Guid == guid)?.Name ?? "operator";
        foreach (var id in _starts)
        {
            var row = save.Upgrades.Items.FirstOrDefault(x => x.Id == id);
            @out.Add(new PendingLine("Upgrades", "", null, null, (AlsoExpedite ? "Start + expedite " : "Start ") + (row?.Title ?? id)));
        }
        foreach (var id in _expedites)
            @out.Add(new PendingLine("Upgrades", "", null, null, "Expedite " + (save.Upgrades.Items.FirstOrDefault(x => x.Id == id)?.Title ?? id)));
        foreach (var g in _heals) @out.Add(new PendingLine("Medbay", "", null, null, $"Heal {Op(g)} (free, instant)"));
        foreach (var g in _treeFixes) @out.Add(new PendingLine("Personnel", "", null, null, $"Complete focus tree: {Op(g)}"));
        foreach (var g in _revives) @out.Add(new PendingLine("Personnel", "", null, null, $"Bring back {Op(g)} from the Memorial"));
        if (RosterOrderOverride != null)
        {
            var names = v.Personnel.Roster.ToDictionary(o => o.Guid, o => o.Name);
            @out.Add(new PendingLine("Personnel", "", null, null,
                "Roster order: " + string.Join(", ", RosterOrderOverride.Select(g => names.GetValueOrDefault(g, "?")))));
        }
        if (_coil.Count > 0)
        {
            string Label(KeyValuePair<string, string> c) =>
                (v.Coil.Active.FirstOrDefault(u => u.Id == c.Key)?.Name ?? c.Key) + (c.Value == "Prevented" ? " (prevent)" : "");
            @out.Add(new PendingLine("Galaxy", "", null, null,
                "Remove Coil upgrade" + (_coil.Count == 1 ? ": " : "s: ") + string.Join(", ", _coil.Select(Label))));
        }
        return @out;
    }
}
