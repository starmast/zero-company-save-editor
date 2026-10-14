using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Views;
using ZeroCompany.Core.Model;

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
    readonly Dictionary<int, bool> _dead = new();                       // combat: unit -> wanted dead flag (only when it differs from the save)
    readonly Dictionary<(string Kind, int Logic, int Index), string> _groups = new();   // combat: wave / definition -> wanted spawn group
    readonly Dictionary<int, string> _spawnSet = new();                                    // combat: wanted enemy spawn set (key 0)
    readonly Dictionary<int, string> _turn = new();                                        // combat: wanted turn structure (key 0)
    readonly Dictionary<string, string> _missionSets = new();                             // Den: mission id -> enemy spawn set path
    readonly Dictionary<string, int> _itemAdds = new();                                   // armory: item asset name -> copies to add
    readonly SortedSet<string> _sceneryRestores = new(StringComparer.Ordinal);               // combat: destroyed props (full object paths) to bring back
    readonly SortedSet<string> _sceneryDestroys = new(StringComparer.Ordinal);                // combat: intact or damaged props (full object paths) to wreck
    readonly Dictionary<int, bool> _triggers = new();                                   // combat: trigger logic index -> wanted active flag
    readonly Dictionary<int, string> _teams = new();                   // combat: unit -> wanted team (only when it differs)
    readonly Dictionary<int, string> _objectives = new();              // combat: objective index -> wanted status (only when it differs)
    readonly HashSet<(int Unit, int Index)> _effectRemovals = new();
    readonly List<(int Unit, int FromUnit, int FromIndex)> _effectCopies = new();

    public event Action? Changed;

    bool _alsoExpedite;

    /// <summary>Starting an upgrade also expedites it (one tick to finish).</summary>
    public bool AlsoExpedite
    {
        get => _alsoExpedite;
        set { if (_alsoExpedite != value) { _alsoExpedite = value; Raise(); } }
    }

    /// <summary>Pending roster order, or null when it matches the save.</summary>
    public IReadOnlyList<string>? RosterOrderOverride { get; private set; }

    public IReadOnlyDictionary<string, double> Edits => _edits;
    public IReadOnlyList<string> Starts => _starts;
    public IReadOnlyList<string> Expedites => _expedites;
    public IReadOnlyList<string> Heals => _heals;
    public IReadOnlyList<string> TreeFixes => _treeFixes;
    public IReadOnlyList<string> Revives => _revives;
    public IReadOnlyDictionary<string, string> CoilChanges => _coil;
    public IReadOnlyDictionary<int, bool> DeadChanges => _dead;
    public IReadOnlyDictionary<int, string> ObjectiveChanges => _objectives;
    public IReadOnlyDictionary<int, string> TeamChanges => _teams;
    public IReadOnlyDictionary<(string Kind, int Logic, int Index), string> GroupChanges => _groups;
    public IReadOnlyDictionary<int, bool> TriggerChanges => _triggers;
    public string? SpawnSetChange => _spawnSet.GetValueOrDefault(0);
    public string? TurnChange => _turn.GetValueOrDefault(0);
    public IReadOnlyDictionary<string, int> ItemAdds => _itemAdds;
    public IReadOnlyDictionary<string, string> MissionSetChanges => _missionSets;
    public IReadOnlySet<string> SceneryRestores => _sceneryRestores;
    public IReadOnlySet<string> SceneryDestroys => _sceneryDestroys;
    public IReadOnlyCollection<(int Unit, int Index)> EffectRemovals => _effectRemovals;
    public IReadOnlyList<(int Unit, int FromUnit, int FromIndex)> EffectCopies => _effectCopies;

    public int Count => _edits.Count + ActionCount;

    public int ActionCount => _starts.Count + _expedites.Count + _heals.Count + _revives.Count + _treeFixes.Count
                              + _coil.Count + (RosterOrderOverride != null ? 1 : 0)
                              + _dead.Count + _effectRemovals.Count + _effectCopies.Count + _objectives.Count + _teams.Count + _groups.Count + _triggers.Count + _spawnSet.Count + _turn.Count + _itemAdds.Count + _missionSets.Count + _sceneryRestores.Count + _sceneryDestroys.Count;

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
        Store(r, value, link);
        Raise();
    }

    /// <summary>Record many edits at once; listeners are told once.</summary>
    public void SetEdits(IEnumerable<(FieldRef Ref, double Value)> edits)
    {
        foreach (var (r, v) in edits) Store(r, v, true);
        Raise();
    }

    void Store(FieldRef r, double value, bool link)
    {
        _refs[r.Id] = r;
        if (value == r.Value) { _edits.Remove(r.Id); _noLink.Remove(r.Id); }
        else
        {
            _edits[r.Id] = value;
            if (link) _noLink.Remove(r.Id); else _noLink.Add(r.Id);
        }
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
        _dead.Clear(); _effectRemovals.Clear(); _effectCopies.Clear(); _objectives.Clear(); _teams.Clear(); _groups.Clear(); _triggers.Clear(); _spawnSet.Clear(); _turn.Clear(); _itemAdds.Clear(); _missionSets.Clear(); _sceneryRestores.Clear(); _sceneryDestroys.Clear();
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

    /// <summary>Queue many upgrade starts / expedites at once (one change notification).</summary>
    public void QueueUpgrades(IEnumerable<string> starts, IEnumerable<string> expedites)
    {
        foreach (var id in starts) Toggle(_starts, id, true);
        foreach (var id in expedites) Toggle(_expedites, id, true);
        Raise();
    }

    public void ClearUpgradeQueue()
    {
        _starts.Clear(); _expedites.Clear();
        Raise();
    }

    /// <summary>Take a Coil upgrade away ("Available" or "Prevented"), or pass null to keep it.</summary>
    public void SetCoil(string id, string? to)
    {
        if (to == null) _coil.Remove(id); else _coil[id] = to;
        Raise();
    }

    // ---- combat ----------------------------------------------------------------------
    /// <summary>A wanted value equal to the saved one is no change, so it is dropped rather than stored.</summary>
    static void SetOrClear<K, V>(Dictionary<K, V> d, K key, V wanted, V saved) where K : notnull
    {
        if (EqualityComparer<V>.Default.Equals(wanted, saved)) d.Remove(key); else d[key] = wanted;
    }

    public void SetDead(int unit, bool wanted, bool saved) { SetOrClear(_dead, unit, wanted, saved); Raise(); }
    public bool WantsDead(int unit, bool saved) => _dead.GetValueOrDefault(unit, saved);

    public void SetGroup(string kind, int logic, int index, string wanted, string saved) { SetOrClear(_groups, (kind, logic, index), wanted, saved); Raise(); }
    public string GroupWanted(string kind, int logic, int index, string saved) => _groups.GetValueOrDefault((kind, logic, index), saved);

    public void SetSpawnSet(string wanted, string saved) { SetOrClear(_spawnSet, 0, wanted, saved); Raise(); }
    public string SpawnSetWanted(string saved) => _spawnSet.GetValueOrDefault(0, saved);
    public void SetTurn(string wanted, string saved) { SetOrClear(_turn, 0, wanted, saved); Raise(); }
    public string TurnWanted(string saved) => _turn.GetValueOrDefault(0, saved);

    public void SetMissionSet(string mission, string wanted, string saved) { SetOrClear(_missionSets, mission, wanted, saved); Raise(); }
    public string MissionSetWanted(string mission, string saved) => _missionSets.GetValueOrDefault(mission, saved);

    /// <summary>Queue <paramref name="copies"/> new copies of an item (0 clears it).</summary>
    public void SetItemAdd(string asset, int copies)
    {
        if (copies <= 0) _itemAdds.Remove(asset); else _itemAdds[asset] = Math.Min(copies, SaveService.MaxItemCopies);
        Raise();
    }
    public int ItemAddCount(string asset) => _itemAdds.GetValueOrDefault(asset);

    /// <summary>Queue or unqueue bringing one destroyed prop back.</summary>
    // A prop is either to be restored, to be destroyed or left alone: queuing one cancels the other.
    public void ToggleSceneryRestore(string actor) { _sceneryDestroys.Remove(actor); if (!_sceneryRestores.Remove(actor)) _sceneryRestores.Add(actor); Raise(); }
    public void ToggleSceneryDestroy(string actor) { _sceneryRestores.Remove(actor); if (!_sceneryDestroys.Remove(actor)) _sceneryDestroys.Add(actor); Raise(); }
    /// <summary>Queue bringing all of <paramref name="actors"/> back (the "Restore scenery" button, "Restore all shown").</summary>
    public void QueueSceneryRestores(IEnumerable<string> actors) { foreach (var a in actors) { _sceneryDestroys.Remove(a); _sceneryRestores.Add(a); } Raise(); }
    /// <summary>Queue wrecking all of <paramref name="actors"/> ("Destroy all shown").</summary>
    public void QueueSceneryDestroys(IEnumerable<string> actors) { foreach (var a in actors) { _sceneryRestores.Remove(a); _sceneryDestroys.Add(a); } Raise(); }
    /// <summary>Drop every queued scenery restore and destroy.</summary>
    public void ClearScenery() { _sceneryRestores.Clear(); _sceneryDestroys.Clear(); Raise(); }

    public void SetTrigger(int logic, bool wanted, bool saved) { SetOrClear(_triggers, logic, wanted, saved); Raise(); }
    public bool TriggerWanted(int logic, bool saved) => _triggers.GetValueOrDefault(logic, saved);

    public void SetTeam(int unit, string wanted, string saved) { SetOrClear(_teams, unit, wanted, saved); Raise(); }
    public string TeamWanted(int unit, string saved) => _teams.GetValueOrDefault(unit, saved);

    public void SetObjective(int index, string wanted, string? saved) { SetOrClear(_objectives, index, wanted, saved!); Raise(); }
    public string? ObjectiveWanted(int index, string? saved) => _objectives.TryGetValue(index, out var s) ? s : saved;

    public void ToggleEffectRemoval(int unit, int index, bool? on = null)
    {
        bool want = on ?? !_effectRemovals.Contains((unit, index));
        if (want) _effectRemovals.Add((unit, index)); else _effectRemovals.Remove((unit, index));
        Raise();
    }

    public void AddEffectCopy(int unit, int fromUnit, int fromIndex)
    {
        _effectCopies.Add((unit, fromUnit, fromIndex));
        Raise();
    }

    public void RemoveEffectCopy(int copyIndex)
    {
        if (copyIndex >= 0 && copyIndex < _effectCopies.Count) { _effectCopies.RemoveAt(copyIndex); Raise(); }
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
        @out.AddRange(_itemAdds.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.AddInventoryItem(kv.Key, kv.Value)));
        @out.AddRange(_missionSets.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetMissionSpawnSet(kv.Key, kv.Value)));
        if (RosterOrderOverride != null) @out.Add(new SaveAction.ReorderRoster(RosterOrderOverride));
        if (_coil.Count > 0) @out.Add(new SaveAction.RemoveCoilUpgrades(_coil.Select(kv => new CoilChange(kv.Key, kv.Value)).ToList()));
        return @out;
    }

    /// <summary>Combat actions, ordered so each index is still valid when it runs: flags, then copies, then removals (highest index first).</summary>
    public List<SaveAction> BuildCombatActions(CombatView combat)
    {
        var @out = new List<SaveAction>();
        @out.AddRange(_dead.OrderBy(kv => kv.Key).Select(kv => new SaveAction.SetUnitDead(kv.Key, kv.Value)));
        string Def(int u, int i) => combat.Units[u].Effects[i].Def;
        @out.AddRange(_effectCopies.Select(x => new SaveAction.CopyUnitEffect(x.Unit, x.FromUnit, x.FromIndex, Def(x.FromUnit, x.FromIndex))));
        @out.AddRange(_effectRemovals.OrderBy(x => x.Unit).ThenByDescending(x => x.Index)
            .Select(x => new SaveAction.RemoveUnitEffect(x.Unit, x.Index, Def(x.Unit, x.Index))));
        @out.AddRange(_teams.OrderBy(kv => kv.Key).Select(kv => new SaveAction.SetUnitTeam(kv.Key, kv.Value)));
        @out.AddRange(_groups.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Key.Logic).ThenBy(kv => kv.Key.Index)
            .Select(kv => new SaveAction.SetSpawnGroup(kv.Key.Kind, kv.Key.Logic, kv.Key.Index, kv.Value)));
        if (_sceneryRestores.Count > 0) @out.Add(new SaveAction.RestoreScenery(_sceneryRestores.ToList()));
        if (_sceneryDestroys.Count > 0) @out.Add(new SaveAction.DestroyScenery(_sceneryDestroys.ToList()));
        if (_spawnSet.TryGetValue(0, out var ss)) @out.Add(new SaveAction.SetEnemySpawnSet(ss));
        if (_turn.TryGetValue(0, out var tn)) @out.Add(new SaveAction.SetTurnStructure(tn));
        @out.AddRange(_triggers.OrderBy(kv => kv.Key).Select(kv =>
            new SaveAction.SetTriggerActive(kv.Key, combat.Reinforcements.Triggers.First(t => t.Logic == kv.Key).Name, kv.Value)));
        @out.AddRange(_objectives.OrderBy(kv => kv.Key).Select(kv =>
            new SaveAction.SetObjectiveStatus(kv.Key, combat.Objectives.First(o => o.Index == kv.Key).Name, kv.Value)));
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
        foreach (var (asset, n) in _itemAdds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Armory", "", null, null, $"Add {(n > 1 ? n + " x " : "")}{save.Model.Db.ItemLabel(asset) ?? Naming.Pretty(asset)}"));
        foreach (var (id, set) in _missionSets.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Galaxy", "", null, null, $"{save.View.Galaxy.Missions.FirstOrDefault(m => m.Id == id)?.Title ?? "Mission"}: enemies {CombatOps.SpawnSetLabel(set)}"));
        foreach (var g in _heals) @out.Add(new PendingLine("Medbay", "", null, null, $"Heal {Op(g)} (free, instant)"));
        foreach (var g in _treeFixes) @out.Add(new PendingLine("Personnel", "", null, null, $"Complete focus tree: {Op(g)}"));
        foreach (var g in _revives) @out.Add(new PendingLine("Personnel", "", null, null, $"Bring back {Op(g)} from the Memorial"));
        if (RosterOrderOverride != null)
        {
            var names = v.Personnel.Roster.ToDictionary(o => o.Guid, o => o.Name);
            @out.Add(new PendingLine("Personnel", "", null, null,
                "Roster order: " + string.Join(", ", RosterOrderOverride.Select(g => names.GetValueOrDefault(g, "?")))));
        }
        if (save.Combat is { } cb)
        {
            string U(int i) => i >= 0 && i < cb.Units.Count ? $"{cb.Units[i].Name} ({cb.Units[i].Team})" : "unit";
            string E(int u, int i) => cb.Units[u].Effects[i].Title;
            void Add(string text) => @out.Add(new PendingLine("Combat", "", null, null, text));
            foreach (var (u, d) in _dead.OrderBy(kv => kv.Key)) Add(d ? $"Kill {U(u)}" : $"Revive {U(u)}");
            foreach (var x in _effectCopies) Add($"Give {U(x.Unit)} the effect {E(x.FromUnit, x.FromIndex)}");
            foreach (var x in _effectRemovals.OrderBy(x => x.Unit).ThenBy(x => x.Index)) Add($"Remove effect {E(x.Unit, x.Index)} from {U(x.Unit)}");
            foreach (var (k, grp) in _groups.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Key.Index))
                Add($"Reinforcement {(k.Kind == "wave" ? "wave" : "definition")} {k.Index + 1}: group {CombatOps.GroupLabel(grp)}");
            if (_sceneryRestores.Count > 0) Add($"Restore {_sceneryRestores.Count} destroyed or damaged scenery prop{(_sceneryRestores.Count == 1 ? "" : "s")}");
            if (_sceneryDestroys.Count > 0) Add($"Destroy {_sceneryDestroys.Count} scenery prop{(_sceneryDestroys.Count == 1 ? "" : "s")}");
            if (_spawnSet.TryGetValue(0, out var sset)) Add($"Enemy spawn set: {CombatOps.SpawnSetLabel(sset)}");
            if (_turn.TryGetValue(0, out var tstr)) Add($"Turn structure: {(tstr == CombatOps.TurnThreeWay ? "three-way battle" : "standard")} (experimental)");
            foreach (var (logic, on) in _triggers.OrderBy(kv => kv.Key))
                Add($"Reinforcement trigger {cb.Reinforcements.Triggers.FirstOrDefault(t => t.Logic == logic)?.Title ?? "?"}: {(on ? "on" : "off")}");
            foreach (var (u, t) in _teams.OrderBy(kv => kv.Key)) Add($"Team: {U(u)} -> {t} (experimental)");
            foreach (var (i, st) in _objectives.OrderBy(kv => kv.Key))
                Add($"Objective {cb.Objectives.FirstOrDefault(o => o.Index == i)?.Title ?? "?"}: {st}");
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
