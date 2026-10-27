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
    readonly HashSet<string> _noLink = new();
    readonly Dictionary<string, FieldRef> _links = new();             // field id -> the field that follows it (unspent focus -> total focus)
    readonly Dictionary<string, double> _linkedDelta = new();         // field id -> how far its edits have moved the field that follows it
    readonly List<string> _starts = new(), _expedites = new(), _heals = new(), _treeFixes = new(), _revives = new();
    readonly Dictionary<string, string> _coil = new();
    readonly Dictionary<(string Kind, int Tier), int> _market = new();                      // Den: (mods/utility, tier) -> items per Black Market refresh
    readonly Dictionary<string, string?> _weaponCopies = new();                            // Personnel: operator -> donor operator (null = automatic) for a weapon copy
    readonly Dictionary<string, int> _gearSlots = new();                                    // Den: "mod"/"utility" -> slots every operator should have
    readonly Dictionary<string, string> _options = new();                                   // campaign: option key (US_...) -> wanted fact tag
    readonly Dictionary<(string Guid, string Tag), int> _grants = new();
    readonly SortedSet<(string Guid, string Trait)> _traits = new();                        // Personnel: (operator, bond trait effect asset) to give                  // Personnel: (operator, ability tag) -> focus-tree level to give it at
    readonly Dictionary<int, bool> _dead = new();                       // combat: unit -> wanted dead flag (only when it differs from the save)
    readonly Dictionary<(string Kind, int Logic, int Index), string> _groups = new();   // combat: wave / definition -> wanted spawn group
    readonly Dictionary<int, string> _spawnSet = new();                                    // combat: wanted enemy spawn set (key 0)
    readonly Dictionary<int, string> _turn = new();                                        // combat: wanted turn structure (key 0)
    readonly Dictionary<int, int> _round = new();                                          // combat: wanted round (key 0)
    readonly Dictionary<string, SortedSet<int>> _zoneClaims = new();                       // galaxy: zone tag -> wanted claimed reward tiers
    readonly Dictionary<string, long> _profileAdds = new();                                // profile: achievement counter to start -> count
    readonly Dictionary<string, int> _operationAges = new();                              // Holotable: operation id -> cycles on offer
    readonly Dictionary<string, int> _missionTurns = new();                               // Den: mission id -> cycle it was added on
    readonly Dictionary<string, string> _missionCrisis = new();                           // Den: crisis mission id -> crisis effect path
    readonly Dictionary<string, string> _missionSets = new();                             // Den: mission id -> enemy spawn set path
    readonly Dictionary<string, int> _itemAdds = new();                                   // armory: item asset name -> copies to add
    readonly SortedSet<string> _sceneryRestores = new(StringComparer.Ordinal);               // combat: destroyed props (full object paths) to bring back
    readonly SortedSet<string> _sceneryDestroys = new(StringComparer.Ordinal);                // combat: intact or damaged props (full object paths) to wreck
    readonly Dictionary<int, bool> _triggers = new();                                   // combat: trigger logic index -> wanted active flag
    readonly Dictionary<int, string> _teams = new();                   // combat: unit -> wanted team (only when it differs)
    readonly SortedSet<string> _squadAdds = new(StringComparer.Ordinal);                  // combat: roster operators (GUID hex) to bring into the fight
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
    public IReadOnlyDictionary<(string Guid, string Tag), int> Grants => _grants;
    public IReadOnlySet<(string Guid, string Trait)> Traits => _traits;
    public IReadOnlyDictionary<string, int> GearSlots => _gearSlots;
    public IReadOnlyDictionary<string, string?> WeaponCopies => _weaponCopies;
    public IReadOnlyDictionary<(string Kind, int Tier), int> MarketTables => _market;
    public IReadOnlyDictionary<string, string> CampaignOptions => _options;
    public IReadOnlyDictionary<string, string> CoilChanges => _coil;
    public IReadOnlyDictionary<int, bool> DeadChanges => _dead;
    public IReadOnlyDictionary<int, string> ObjectiveChanges => _objectives;
    public IReadOnlyDictionary<int, string> TeamChanges => _teams;
    public IReadOnlyDictionary<(string Kind, int Logic, int Index), string> GroupChanges => _groups;
    public IReadOnlyDictionary<int, bool> TriggerChanges => _triggers;
    public string? SpawnSetChange => _spawnSet.GetValueOrDefault(0);
    public string? TurnChange => _turn.GetValueOrDefault(0);
    public int? RoundChange => _round.TryGetValue(0, out var r) ? r : null;
    public IReadOnlyDictionary<string, int> ItemAdds => _itemAdds;
    public IReadOnlyDictionary<string, string> MissionSetChanges => _missionSets;
    public IReadOnlyDictionary<string, int> OperationAgeChanges => _operationAges;
    public IReadOnlyDictionary<string, SortedSet<int>> ZoneClaimChanges => _zoneClaims;
    public IReadOnlyDictionary<string, long> ProfileCounterAdds => _profileAdds;
    public IReadOnlyDictionary<string, int> MissionTurnChanges => _missionTurns;
    public IReadOnlyDictionary<string, string> MissionCrisisChanges => _missionCrisis;
    public IReadOnlySet<string> SceneryRestores => _sceneryRestores;
    public IReadOnlySet<string> SceneryDestroys => _sceneryDestroys;
    public IReadOnlyCollection<(int Unit, int Index)> EffectRemovals => _effectRemovals;
    public IReadOnlyList<(int Unit, int FromUnit, int FromIndex)> EffectCopies => _effectCopies;

    public int Count => _edits.Count + ActionCount;

    public int ActionCount => _starts.Count + _expedites.Count + _heals.Count + _revives.Count + _treeFixes.Count + _grants.Count + _traits.Count + _weaponCopies.Count + _gearSlots.Count + _market.Count
                              + _options.Count + _coil.Count + (RosterOrderOverride != null ? 1 : 0)
                              + _dead.Count + _effectRemovals.Count + _effectCopies.Count + _objectives.Count + _teams.Count + _groups.Count + _triggers.Count + _spawnSet.Count + _turn.Count + _round.Count + _squadAdds.Count + _itemAdds.Count + _missionSets.Count + _operationAges.Count + _zoneClaims.Count + _profileAdds.Count + _missionTurns.Count + _missionCrisis.Count + _sceneryRestores.Count + _sceneryDestroys.Count;

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

    /// <summary>
    /// Forget everything and learn the open save's linked fields: each operator's unspent focus drives their total focus, so that
    /// points spent stay the same. (Field ids are byte offsets, so links are re-learned whenever a save is (re)opened.)
    /// </summary>
    public void Reset(OpenSave? save)
    {
        Clear();
        Relink(save);
    }

    /// <summary>Learn the links of <paramref name="save"/> without touching pending edits (the same file read again).</summary>
    public void Relink(OpenSave? save)
    {
        _links.Clear();
        if (save == null) return;
        foreach (var op in save.View.Personnel.Roster.Concat(save.View.Personnel.Memorial))
            if (op.Focus is { TotalRef: { } total } focus) AddLink(focus.Id, total);
    }

    /// <summary>Make <paramref name="target"/> follow every linked edit of field <paramref name="sourceId"/>.</summary>
    public void AddLink(string sourceId, FieldRef target) => _links[sourceId] = target;

    /// <summary>
    /// A linked edit moves the field that follows it right away, by the same amount, so the pending value of that field is always the
    /// one that will be written (whatever else edits it, in whatever order). The move is recorded so a revert can take it back.
    /// </summary>
    void Store(FieldRef r, double value, bool link)
    {
        if (link && _links.TryGetValue(r.Id, out var target))
        {
            double delta = value - GetValue(r);
            if (delta != 0)
            {
                StoreValue(target, GetValue(target) + delta, false);
                _linkedDelta[r.Id] = _linkedDelta.GetValueOrDefault(r.Id) + delta;
            }
            link = false;                                        // already applied: the writer must not add it again
        }
        StoreValue(r, value, link);
    }

    void StoreValue(FieldRef r, double value, bool link)
    {
        if (value == r.Value) { _edits.Remove(r.Id); _noLink.Remove(r.Id); }
        else
        {
            _edits[r.Id] = value;
            if (link) _noLink.Remove(r.Id); else _noLink.Add(r.Id);
        }
    }

    public void Revert(FieldRef r)
    {
        if (_links.TryGetValue(r.Id, out var target) && _linkedDelta.Remove(r.Id, out var delta))
            StoreValue(target, GetValue(target) - delta, false);
        foreach (var (source, t) in _links)                     // reverting the follower drops what its leader had moved
            if (t.Id == r.Id) _linkedDelta.Remove(source);
        _edits.Remove(r.Id); _noLink.Remove(r.Id);
        Raise();
    }

    public void Discard()
    {
        Clear();
        Raise();
    }

    /// <summary>Forget every pending edit and action without notifying; the open save's links stay (see <see cref="Reset"/>).</summary>
    public void Clear()
    {
        _edits.Clear(); _noLink.Clear(); _linkedDelta.Clear(); _starts.Clear(); _expedites.Clear(); _heals.Clear();
        _revives.Clear(); _treeFixes.Clear(); _grants.Clear(); _traits.Clear(); _weaponCopies.Clear(); _gearSlots.Clear(); _market.Clear(); _options.Clear(); _coil.Clear(); RosterOrderOverride = null;
        _dead.Clear(); _effectRemovals.Clear(); _effectCopies.Clear(); _objectives.Clear(); _teams.Clear(); _groups.Clear(); _triggers.Clear(); _spawnSet.Clear(); _turn.Clear(); _round.Clear(); _squadAdds.Clear(); _itemAdds.Clear(); _missionSets.Clear(); _operationAges.Clear(); _zoneClaims.Clear(); _profileAdds.Clear(); _missionTurns.Clear(); _missionCrisis.Clear(); _sceneryRestores.Clear(); _sceneryDestroys.Clear();
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
    /// <summary>Queue making a Black Market quality table roll <paramref name="count"/> items per refresh; a negative count removes the request.</summary>
    public void SetMarketTable(string kind, int tier, int count)
    {
        if (count < 0) _market.Remove((kind, tier)); else _market[(kind, tier)] = count;
        Raise();
    }

    /// <summary>Queue starting a profile counter at <paramref name="count"/>; null takes it back out of the queue.</summary>
    public void SetProfileCounterAdd(string stat, long? count)
    {
        if (count == null) _profileAdds.Remove(stat); else _profileAdds[stat] = count.Value;
        Raise();
    }

    /// <summary>Whether tier <paramref name="tier"/> of <paramref name="zone"/> will count as claimed (queued, else as <paramref name="saved"/> says).</summary>
    public bool ZoneTierClaimed(string zone, int tier, IReadOnlyCollection<int> saved) =>
        _zoneClaims.TryGetValue(zone, out var want) ? want.Contains(tier) : saved.Contains(tier);

    /// <summary>Queue marking a zone's reward tier claimed or not; back at what the save has, the zone drops out of the queue.</summary>
    public void SetZoneTierClaimed(string zone, int tier, bool claimed, IReadOnlyCollection<int> saved)
    {
        var want = _zoneClaims.TryGetValue(zone, out var w) ? w : new SortedSet<int>(saved);
        if (claimed) want.Add(tier); else want.Remove(tier);
        if (want.SetEquals(saved)) _zoneClaims.Remove(zone); else _zoneClaims[zone] = want;
        Raise();
    }

    /// <summary>Queue changing a campaign option to the choice with fact tag <paramref name="tag"/>; null removes the request.</summary>
    public void SetCampaignOption(string key, string? tag)
    {
        if (tag == null) _options.Remove(key); else _options[key] = tag;
        Raise();
    }
    /// <summary>Queue giving every operator <paramref name="total"/> gear slots of <paramref name="type"/> ("mod" or "utility"); a total below 1 removes the request.</summary>
    public void SetGearSlots(string type, int total)
    {
        if (total < 1) _gearSlots.Remove(type); else _gearSlots[type] = total;
        Raise();
    }
    /// <summary>Queue giving <paramref name="guid"/> a copy of <paramref name="donor"/>'s weapon (null = the first suitable operator), or take it back out of the queue.</summary>
    public void SetWeaponCopy(string guid, bool on, string? donor = null)
    {
        if (on) _weaponCopies[guid] = donor; else _weaponCopies.Remove(guid);
        Raise();
    }
    /// <summary>Queue giving <paramref name="guid"/> the ability <paramref name="tag"/> at <paramref name="level"/>; a level below 1 takes it back out of the queue.</summary>
    public void SetGrant(string guid, string tag, int level)
    {
        if (level < 1) _grants.Remove((guid, tag)); else _grants[(guid, tag)] = level;
        Raise();
    }
    /// <summary>Queue giving <paramref name="guid"/> the bond trait <paramref name="trait"/>, or take it back out of the queue.</summary>
    public void SetTrait(string guid, string trait, bool on)
    {
        if (on) _traits.Add((guid, trait)); else _traits.Remove((guid, trait));
        Raise();
    }
    public void ToggleRevive(string guid, bool? on = null)
    {
        Toggle(_revives, guid, on);
        if (!_revives.Contains(guid) && RosterOrderOverride != null)        // an operator who stays fallen has no place in the roster
        {
            var order = RosterOrderOverride.Where(g => g != guid).ToList();
            RosterOrderOverride = IsDefaultOrder(order) ? null : order;
        }
        Raise();
    }

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
    public void SetRound(int wanted, int saved) { SetOrClear(_round, 0, wanted, saved); Raise(); }
    public int RoundWanted(int saved) => _round.GetValueOrDefault(0, saved);

    public void SetMissionTurn(string mission, int wanted, int saved) { SetOrClear(_missionTurns, mission, wanted, saved); Raise(); }
    public int MissionTurnWanted(string mission, int saved) => _missionTurns.GetValueOrDefault(mission, saved);
    public void SetMissionCrisis(string mission, string wanted, string saved) { SetOrClear(_missionCrisis, mission, wanted, saved); Raise(); }
    public string MissionCrisisWanted(string mission, string saved) => _missionCrisis.GetValueOrDefault(mission, saved);
    public void SetOperationAge(string operation, int wanted, int saved) { SetOrClear(_operationAges, operation, wanted, saved); Raise(); }
    public int OperationAgeWanted(string operation, int saved) => _operationAges.GetValueOrDefault(operation, saved);
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

    /// <summary>Queue or unqueue bringing a roster operator into the fight.</summary>
    public void SetSquadAdd(string guid, bool wanted) { if (wanted) _squadAdds.Add(guid); else _squadAdds.Remove(guid); Raise(); }
    public bool SquadAddWanted(string guid) => _squadAdds.Contains(guid);
    public IReadOnlyCollection<string> SquadAdds => _squadAdds;

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
    IReadOnlyList<string> _liveRoster = Array.Empty<string>();          // the saved roster, as of the last reorder

    /// <summary>The order a write produces with no reorder: the saved roster, then the queued revives.</summary>
    bool IsDefaultOrder(IEnumerable<string> order) => order.SequenceEqual(_liveRoster.Concat(_revives));

    /// <summary>The pending order with any revive queued after it was made added at the end (what the writer needs).</summary>
    IReadOnlyList<string>? FullOverride() =>
        RosterOrderOverride?.Concat(_revives.Where(g => !RosterOrderOverride.Contains(g))).ToList();

    /// <summary>
    /// The roster as it will be written: the pending order, else the saved one, with operators queued to come back from the Memorial at
    /// the end (where the game, and a revive, put a returning operator) unless the pending order already places them.
    /// </summary>
    public IReadOnlyList<string> RosterOrder(SaveView view)
    {
        return FullOverride() ?? view.Personnel.Roster.Select(o => o.Guid).Concat(_revives).ToList();
    }

    void SetRosterOrder(IReadOnlyList<string> order, SaveView view)
    {
        _liveRoster = view.Personnel.Roster.Select(o => o.Guid).ToList();
        RosterOrderOverride = IsDefaultOrder(order) ? null : order.ToList();
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
        @out.AddRange(_market.OrderBy(kv => kv.Key.Kind, StringComparer.Ordinal).ThenBy(kv => kv.Key.Tier)
                             .Select(kv => (SaveAction)new SaveAction.SetMarketTable(kv.Key.Kind, kv.Key.Tier, kv.Value)));
        @out.AddRange(_gearSlots.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetGearSlots(kv.Key, kv.Value)));
        @out.AddRange(_options.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetCampaignOption(kv.Key, kv.Value)));
        @out.AddRange(_grants.OrderBy(kv => kv.Key.Guid, StringComparer.Ordinal).ThenBy(kv => kv.Key.Tag, StringComparer.Ordinal)
                             .Select(kv => (SaveAction)new SaveAction.GrantAbility(kv.Key.Guid, kv.Key.Tag, kv.Value)));
        @out.AddRange(_traits.Select(t => (SaveAction)new SaveAction.GiveTrait(t.Guid, t.Trait)));
        @out.AddRange(_weaponCopies.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.CopyWeapon(kv.Key, kv.Value)));
        @out.AddRange(_itemAdds.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.AddInventoryItem(kv.Key, kv.Value)));
        @out.AddRange(_profileAdds.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.AddProfileCounter(kv.Key, kv.Value)));
        @out.AddRange(_zoneClaims.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetZoneClaims(kv.Key, kv.Value.ToList())));
        @out.AddRange(_operationAges.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetOperationAge(kv.Key, kv.Value)));
        @out.AddRange(_missionTurns.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetMissionTurnAdded(kv.Key, kv.Value)));
        @out.AddRange(_missionCrisis.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetMissionCrisisEffect(kv.Key, kv.Value)));
        @out.AddRange(_missionSets.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (SaveAction)new SaveAction.SetMissionSpawnSet(kv.Key, kv.Value)));
        if (FullOverride() is { } order) @out.Add(new SaveAction.ReorderRoster(order));             // after the revives, so it can place them
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
        @out.AddRange(_squadAdds.Select(guid => new SaveAction.AddSquadMember(guid)));
        if (_sceneryRestores.Count > 0) @out.Add(new SaveAction.RestoreScenery(_sceneryRestores.ToList()));
        if (_sceneryDestroys.Count > 0) @out.Add(new SaveAction.DestroyScenery(_sceneryDestroys.ToList()));
        if (_spawnSet.TryGetValue(0, out var ss)) @out.Add(new SaveAction.SetEnemySpawnSet(ss));
        if (_turn.TryGetValue(0, out var tn)) @out.Add(new SaveAction.SetTurnStructure(tn));
        if (_round.TryGetValue(0, out var rd)) @out.Add(new SaveAction.SetCurrentRound(rd));
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
        string AbilityLabel(string tag) => Naming.Ability(save.Model.Db, tag);
        foreach (var id in _starts)
        {
            var row = save.Upgrades.Items.FirstOrDefault(x => x.Id == id);
            @out.Add(new PendingLine("Upgrades", "", null, null, (AlsoExpedite ? "Start + expedite " : "Start ") + (row?.Title ?? id)));
        }
        foreach (var id in _expedites)
            @out.Add(new PendingLine("Upgrades", "", null, null, "Expedite " + (save.Upgrades.Items.FirstOrDefault(x => x.Id == id)?.Title ?? id)));
        foreach (var (asset, n) in _itemAdds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Armory", "", null, null, $"Add {(n > 1 ? n + " x " : "")}{save.Model.Db.ItemLabel(asset) ?? Naming.Pretty(asset)}"));
        foreach (var (stat, count) in _profileAdds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Achievements", "", null, null,
                $"Start counting {ProfileOps.Achievements.FirstOrDefault(a => a.Stat == stat)?.Name ?? stat} at {count}"));
        foreach (var (zone, tiers) in _zoneClaims.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var r = save.View.Galaxy.Regions.FirstOrDefault(x => x.Tag == zone);
            var unclaimed = (r?.Claimed ?? new List<int>()).Except(tiers).Select(t => r?.TierLabel(t) ?? $"tier {t + 1}").ToList();
            var claimed = tiers.Except(r?.Claimed ?? new List<int>()).Select(t => r?.TierLabel(t) ?? $"tier {t + 1}").ToList();
            @out.Add(new PendingLine("Galaxy", "", null, null, $"{r?.Name ?? zone}: "
                + string.Join("; ", new[] { unclaimed.Count > 0 ? "claimable again: " + string.Join(", ", unclaimed) : "", claimed.Count > 0 ? "marked claimed: " + string.Join(", ", claimed) : "" }.Where(x => x.Length > 0))));
        }
        foreach (var (id, age) in _operationAges.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Galaxy", "", null, null, $"{save.View.Galaxy.Operations.FirstOrDefault(o => o.Id == id)?.Title ?? "Operation"}: {age} cycle{(age == 1 ? "" : "s")} on offer"));
        string MissionName(string id) => save.View.Galaxy.Missions.FirstOrDefault(m => m.Id == id)?.Title ?? "Mission";
        foreach (var (id, turn) in _missionTurns.OrderBy(kv => kv.Key, StringComparer.Ordinal)) @out.Add(new PendingLine("Galaxy", "", null, null, $"{MissionName(id)}: added on cycle {turn}"));
        foreach (var (id, fx) in _missionCrisis.OrderBy(kv => kv.Key, StringComparer.Ordinal)) @out.Add(new PendingLine("Galaxy", "", null, null, $"{MissionName(id)}: Coil upgrade {Naming.Pretty(Naming.AssetName(fx))}"));
        foreach (var (id, set) in _missionSets.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Galaxy", "", null, null, $"{save.View.Galaxy.Missions.FirstOrDefault(m => m.Id == id)?.Title ?? "Mission"}: enemies {CombatOps.SpawnSetLabel(set)}"));
        foreach (var g in _heals) @out.Add(new PendingLine("Medbay", "", null, null, $"Heal {Op(g)} (free, instant)"));
        foreach (var g in _treeFixes) @out.Add(new PendingLine("Personnel", "", null, null, $"Complete focus tree: {Op(g)}"));
        foreach (var g in _revives) @out.Add(new PendingLine("Personnel", "", null, null, $"Bring back {Op(g)} from the Memorial"));
        foreach (var ((kind, tier), n) in _market.OrderBy(kv => kv.Key.Kind, StringComparer.Ordinal).ThenBy(kv => kv.Key.Tier))
            @out.Add(new PendingLine("Den", "", null, null, $"Black Market rolls {n} {MarketOps.Quality(tier)} {(kind == "mods" ? "weapon mod" : "utility")} item{(n == 1 ? "" : "s")} per refresh"));
        foreach (var (type, n) in _gearSlots.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Den", "", null, null, $"Every operator gets {n} {type} slots"));
        foreach (var (key, tag) in _options.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Campaign", "", null, null,
                CampaignOptionOps.Find(key) is { } opt ? $"Campaign option {opt.Label}: {opt.LabelOf(tag) ?? tag}" : $"Campaign option {key}: {tag}"));
        foreach (var ((g, tag), lvl) in _grants.OrderBy(kv => kv.Key.Guid, StringComparer.Ordinal).ThenBy(kv => kv.Key.Tag, StringComparer.Ordinal))
            @out.Add(new PendingLine("Personnel", "", null, null, $"Give {Op(g)} {AbilityLabel(tag)} (level {lvl})"));
        foreach (var (g, trait) in _traits)
            @out.Add(new PendingLine("Personnel", "", null, null, $"Give {Op(g)} the bond trait {TraitOps.Find(trait)?.Name ?? trait}"));
        foreach (var (g, donor) in _weaponCopies.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            @out.Add(new PendingLine("Personnel", "", null, null, $"Give {Op(g)} a copy of {(donor == null ? "another operator's" : Op(donor) + "'s")} vanilla weapon"
                + (save.View.Weapons.Rows.FirstOrDefault(r => r.Guid == g) is { Damaged: false } ? " (replaces a working weapon)" : "")));
        if (FullOverride() is { } order)
            @out.Add(new PendingLine("Personnel", "", null, null, "Roster order: " + string.Join(", ", order.Select(Op))));
        if (save.Combat is { } cb)
        {
            string U(int i) => i >= 0 && i < cb.Units.Count ? $"{cb.Units[i].Name} ({cb.Units[i].Team})" : "unit";
            string E(int u, int i) => cb.Units[u].Effects[i].Title;
            void Add(string text) => @out.Add(new PendingLine("Combat", "", null, null, text));
            foreach (var (u, d) in _dead.OrderBy(kv => kv.Key)) Add(d ? $"Kill {U(u)}" : $"Revive {U(u)}");
            foreach (var guid in _squadAdds) Add($"Bring {cb.Squad.FirstOrDefault(m => m.Guid == guid)?.Name ?? "an operator"} into the fight");
            foreach (var x in _effectCopies) Add($"Give {U(x.Unit)} the effect {E(x.FromUnit, x.FromIndex)}");
            foreach (var x in _effectRemovals.OrderBy(x => x.Unit).ThenBy(x => x.Index)) Add($"Remove effect {E(x.Unit, x.Index)} from {U(x.Unit)}");
            foreach (var (k, grp) in _groups.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Key.Index))
                Add($"Reinforcement {cb.Reinforcements.EncounterPrefix(k.Logic)}{(k.Kind == "wave" ? "wave" : "definition")} {k.Index + 1}: group {CombatOps.GroupLabel(grp)}");
            if (_sceneryRestores.Count > 0) Add($"Restore {_sceneryRestores.Count} destroyed or damaged scenery prop{(_sceneryRestores.Count == 1 ? "" : "s")}");
            if (_sceneryDestroys.Count > 0) Add($"Destroy {_sceneryDestroys.Count} scenery prop{(_sceneryDestroys.Count == 1 ? "" : "s")}");
            if (_spawnSet.TryGetValue(0, out var sset)) Add($"Enemy spawn set: {CombatOps.SpawnSetLabel(sset)}");
            if (_round.TryGetValue(0, out var rnd)) Add($"Current round: {rnd}");
            if (_turn.TryGetValue(0, out var tstr)) Add($"Turn structure: {(tstr == CombatOps.TurnThreeWay ? "three-way battle" : "standard")} (experimental)");
            foreach (var (logic, on) in _triggers.OrderBy(kv => kv.Key))
                Add($"Reinforcement trigger {cb.Reinforcements.Triggers.FirstOrDefault(t => t.Logic == logic)?.Title ?? "?"}: {(on ? "on" : "off")}");
            foreach (var (u, t) in _teams.OrderBy(kv => kv.Key)) Add($"Team: {U(u)} -> {t}");
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
