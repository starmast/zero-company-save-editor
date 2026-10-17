using System.Globalization;
using System.Text.RegularExpressions;
using ZeroCompany.Core;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Sdk;

/// <summary>The write could not go ahead because of the state of the world (the game is running, the save changed on disk). Nothing was written.</summary>
public sealed class ZcRefusal : ZcException
{
    public ZcRefusal(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>A named structural edit that <see cref="SaveEditor.Do"/> accepts.</summary>
public sealed record EditAction(string Name, string Usage, string Summary, bool CombatOnly, Func<IReadOnlyList<string>, string> Run)
{
    public int MinArgs => Usage.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Count(p => p.StartsWith('<'));
}

/// <summary>
/// Edits one save without the UI. It drives the very same pieces the editor's screens do (<see cref="PendingChanges"/> and
/// <see cref="EditorSession"/>), so a write gets the same checks: the game-running refusal, the changed-on-disk check, linked fields,
/// a backup, and a verified rebuild. Nothing is written until <see cref="Apply"/>.
/// </summary>
public sealed class SaveEditor
{
    readonly Func<string?> _gameRunning;
    readonly Dictionary<string, EditAction> _actions = new();

    public ZcWorkspace Workspace { get; }
    public EditorSession Session { get; }
    public CatalogEntry Entry { get; }
    public OpenSave Save => Session.Current!;
    public FieldCatalog Fields { get; }
    public PendingChanges Pending => Session.Pending;
    public IReadOnlyCollection<EditAction> Actions => _actions.Values;

    SaveEditor(ZcWorkspace ws, CatalogEntry entry, Func<string?> gameRunning)
    {
        Workspace = ws; Entry = entry; _gameRunning = gameRunning;
        Session = new EditorSession(ws.Env, gameRunning);
        Session.Service.Dirs = SaveCatalog.DirsFor(ws, entry);
        try { Session.Open(entry.DirId, entry.Name); }
        catch (Exception e) when (e is EditException or GvasException or SaveFormatException or IOException)
        { throw new ZcException($"{entry.Name}: {e.Message}", e); }
        Fields = FieldCatalog.Build(Save);
        RegisterActions();
    }

    /// <summary>Open a save by any spec (path, name, comment, latest). <paramref name="gameRunning"/> overrides the process check (tests).</summary>
    public static SaveEditor Open(ZcWorkspace ws, string spec, Func<string?>? gameRunning = null) =>
        new(ws, SaveCatalog.Resolve(ws, spec), gameRunning ?? GameProcessName);

    static string? GameProcessName() => GameProcess.Name;

    public int PendingCount => Pending.Count;
    public bool IsCombat => Save.Combat != null;

    // ---- scalar edits --------------------------------------------------------------------------------

    /// <summary>Queue a new value for the field at <paramref name="path"/> (linked fields, such as total focus, follow as in the editor).</summary>
    public FieldEntry Set(string path, double value)
    {
        var e = Fields.Resolve(path);
        var r = e.Ref;
        if (!PendingChanges.Valid(r, value))
            throw new ZcException($"{e.Path}: {Fmt(value)} is out of range [{Fmt(e.Min)}, {Fmt(e.Max)}]{(e.Kind == "float" ? "" : " (whole numbers only)")}");
        Pending.SetEdit(r, value);
        return e;
    }

    /// <summary>A number for people: whole numbers in full, others to 7 significant digits (the game stores most fractions as 32-bit floats).</summary>
    public static string Fmt(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-9 ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) : v.ToString("G7", CultureInfo.InvariantCulture);

    public static double ParseNumber(string text)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) return v;
        throw new ZcException($"'{text}' is not a number");
    }

    // ---- writing -------------------------------------------------------------------------------------

    /// <summary>What a write would change. Values the editor's model does not label (combat stats ...) are named by their catalog path.</summary>
    public IReadOnlyList<PendingLine> Preview() =>
        Pending.Describe(Save).Select(l =>
            l.Where == "Raw" && Fields.ById(l.Label) is { } e ? new PendingLine(e.Section, $"{e.Label} ({e.Path})", e.Value, l.To, null) : l).ToList();

    /// <summary>Write everything pending (in place, or to a copy). Throws <see cref="ZcRefusal"/> when the game is running or the file changed.</summary>
    public ApplyResult Apply(bool asCopy = false, string? copyName = null, bool force = false)
    {
        if (Pending.Count == 0) throw new ZcException("nothing to change");
        if (!asCopy && !force && _gameRunning() is { } proc)
            throw new ZcRefusal($"the game appears to be running ({proc}); close it first, or pass --force");
        try { return Session.Apply(asCopy, force, copyName); }
        catch (EditException e) when (e.Message.Contains("changed on disk")) { throw new ZcRefusal(e.Message, e); }
        catch (EditException e) { throw new ZcException(e.Message, e); }
    }

    /// <summary>The editor's backups of this save, newest first.</summary>
    public IReadOnlyList<BackupInfo> Backups() => Session.Service.ListBackups(Entry.Name);

    /// <summary>Put a backup back (the current file is backed up first). Throws <see cref="ZcRefusal"/> while the game is running unless forced.</summary>
    public (string Restored, string PreviousSavedAs) Restore(string backupFile, bool force = false)
    {
        if (!force && _gameRunning() is { } proc)
            throw new ZcRefusal($"the game appears to be running ({proc}); close it first, or pass --force");
        try { return Session.Service.Restore(backupFile, force); }
        catch (EditException e) { throw new ZcException(e.Message, e); }
    }

    // ---- structural edits ----------------------------------------------------------------------------

    /// <summary>Queue one named structural edit; returns a one-line description of what was queued.</summary>
    public string Do(string action, IReadOnlyList<string> args)
    {
        if (!_actions.TryGetValue(NormName(action), out var a))
            throw new ZcException($"unknown action '{action}' (one of: {string.Join(", ", _actions.Values.Select(x => x.Name))})");
        if (args.Count < a.MinArgs) throw new ZcException($"usage: {a.Usage}");
        if (a.CombatOnly && !IsCombat) throw new ZcException($"'{a.Name}' only works on a combat save");
        return a.Run(args);
    }

    static string NormName(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    void Reg(string name, string usage, string summary, Func<IReadOnlyList<string>, string> run, bool combat = false) =>
        _actions[NormName(name)] = new EditAction(name, usage, summary, combat, run);

    void RegisterActions()
    {
        var p = Pending;
        Reg("heal", "heal <operator>", "heal an injured operator", a => { var o = Op(a[0]); p.ToggleHeal(o.Guid, true); return $"heal {o.Name}"; });
        Reg("revive", "revive <operator>", "bring a fallen operator back from the Memorial", a => { var o = Op(a[0]); p.ToggleRevive(o.Guid, true); return $"revive {o.Name}"; });
        Reg("complete-tree", "complete-tree <operator>", "finish an operator's focus tree", a => { var o = Op(a[0]); p.ToggleTreeFix(o.Guid, true); return $"complete focus tree of {o.Name}"; });
        Reg("move-operator", "move-operator <operator> <position>", "put an operator at a place in the roster (1 = first)", a =>
        {
            var o = Op(a[0]); p.MoveOperatorTo(o.Guid, (int)Count(a[1], 1, 999) - 1, Save.View); return $"move {o.Name} to place {a[1]}";
        });
        Reg("add-item", "add-item <item> [copies]", "give the squad new copies of a utility item or weapon mod", a =>
        {
            var it = Item(a[0]); int n = a.Count > 1 ? (int)Count(a[1], 1, SaveService.MaxItemCopies) : 1;
            p.SetItemAdd(it.Asset, n); return $"add {n} x {it.Name}";
        });
        Reg("start-upgrade", "start-upgrade <upgrade>", "start a base upgrade", a => { var u = Upgrade(a[0]); p.ToggleStart(u.Id!, true); return $"start {u.Title}"; });
        Reg("expedite-upgrade", "expedite-upgrade <upgrade>", "finish an upgrade in progress next turn", a => { var u = Upgrade(a[0]); p.ToggleExpedite(u.Id!, true); return $"expedite {u.Title}"; });
        Reg("coil", "coil <upgrade> <Available|Prevented|keep>", "take a Coil upgrade away", a =>
        {
            var row = Save.View.Coil.Active.Where(r => r.Id.Equals(a[0], StringComparison.OrdinalIgnoreCase) || NormName(r.Name) == NormName(a[0])).ToList();
            if (row.Count != 1) throw new ZcException(row.Count == 0 ? $"no active Coil upgrade '{a[0]}'" : $"'{a[0]}' matches {row.Count} Coil upgrades; use the id");
            var to = a[1].Equals("keep", StringComparison.OrdinalIgnoreCase) ? null : Choice("Coil state", a[1], new[] { ("Available", "Available"), ("Prevented", "Prevented") });
            p.SetCoil(row[0].Id, to); return $"Coil upgrade {row[0].Name}: {to ?? "keep"}";
        });
        Reg("operation-age", "operation-age <operation> <cycles>", "set how long a Holotable operation has been on offer", a =>
        {
            var o = Pick("operation", Save.View.Galaxy.Operations, a[0], x => x.Id, x => x.Title);
            if (!o.CanEdit) throw new ZcException($"{o.Title}: the game data does not say how long it lasts, so its age cannot be changed");
            int age = (int)Count(a[1], 0, 999); p.SetOperationAge(o.Id, age, o.Age); return $"{o.Title}: {age} cycles on offer";
        });
        Reg("mission-turn", "mission-turn <mission> <cycle>", "set the cycle a Den mission was added on", a =>
        {
            var m = Pick("mission", Save.View.Galaxy.Missions, a[0], x => x.Id, x => x.Title);
            int t = (int)Count(a[1], 0, 99999); p.SetMissionTurn(m.Id, t, (int)m.TurnAdded); return $"{m.Title}: added on cycle {t}";
        });
        Reg("mission-spawn-set", "mission-spawn-set <mission> <set>", "point a Den mission at another enemy spawn set", a =>
        {
            var m = Pick("mission", Save.View.Galaxy.Missions, a[0], x => x.Id, x => x.Title);
            var set = Choice("spawn set", a[1], m.Choices); p.SetMissionSet(m.Id, set, m.Current); return $"{m.Title}: spawn set {set}";
        });
        Reg("mission-crisis", "mission-crisis <mission> <effect>", "change which Coil upgrade a crisis mission is about", a =>
        {
            var m = Pick("mission", Save.View.Galaxy.Missions, a[0], x => x.Id, x => x.Title);
            var fx = Choice("crisis effect", a[1], m.CrisisChoices); p.SetMissionCrisis(m.Id, fx, m.CrisisEffect); return $"{m.Title}: crisis {fx}";
        });

        // combat
        Reg("unit-dead", "unit-dead <unit> <true|false>", "mark a unit dead or alive", a =>
        {
            var u = Unit(a[0]); bool d = Bool(a[1]); p.SetDead(u.Index, d, u.Dead); return $"unit {u.Index} {u.Name}: {(d ? "dead" : "alive")}";
        }, combat: true);
        Reg("unit-team", "unit-team <unit> <Player|Enemy|Enemy2|Civilian>", "move a unit to another team (experimental)", a =>
        {
            var u = Unit(a[0]); p.SetTeam(u.Index, a[1], u.Team); return $"unit {u.Index} {u.Name}: team {a[1]}";
        }, combat: true);
        Reg("remove-effect", "remove-effect <unit> <effect-index>", "remove one active effect from a unit", a =>
        {
            var u = Unit(a[0]); int i = (int)Count(a[1], 0, 9999);
            if (u.Effects.All(e => e.Index != i)) throw new ZcException($"unit {u.Index} has no effect #{i} (its effects: {string.Join(", ", u.Effects.Select(e => $"{e.Index}={e.Title}"))})");
            p.ToggleEffectRemoval(u.Index, i, true); return $"remove effect #{i} from unit {u.Index} {u.Name}";
        }, combat: true);
        Reg("copy-effect", "copy-effect <unit> <from-unit> <effect-index>", "copy another unit's effect onto this unit", a =>
        {
            var u = Unit(a[0]); var f = Unit(a[1]); int i = (int)Count(a[2], 0, 9999);
            if (f.Effects.All(e => e.Index != i)) throw new ZcException($"unit {f.Index} has no effect #{i}");
            p.AddEffectCopy(u.Index, f.Index, i); return $"copy effect #{i} of unit {f.Index} onto unit {u.Index}";
        }, combat: true);
        Reg("spawn-group", "spawn-group <wave|config> <logic> <index> <group>", "set the spawn group of a queued reinforcement wave or a wave definition", a =>
        {
            var kind = a[0].ToLowerInvariant(); int logic = (int)Count(a[1], 0, 9999), idx = (int)Count(a[2], 0, 9999);
            string saved = kind switch
            {
                "wave" => Save.Combat!.Reinforcements.Waves.FirstOrDefault(w => w.Logic == logic && w.Index == idx)?.Group,
                "config" => Save.Combat!.Reinforcements.Configs.FirstOrDefault(w => w.Logic == logic && w.Index == idx)?.Group,
                _ => throw new ZcException("kind must be wave or config"),
            } ?? throw new ZcException($"no {kind} {logic}.{idx} (see: zc save profile)");
            var group = Choice("spawn group", a[3], Save.Combat!.Reinforcements.Groups.Select(g => (g, g)));
            p.SetGroup(kind, logic, idx, group, saved); return $"{kind} {logic}.{idx}: {group}";
        }, combat: true);
        Reg("restore-scenery", "restore-scenery <prop-path>... | all", "bring destroyed or damaged props back", a =>
        {
            var props = a.Count == 1 && a[0].Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Save.Combat!.Props.Where(x => x.Condition != "Pristine").Select(x => x.Actor).ToList() : a.ToList();
            p.QueueSceneryRestores(props); return $"restore {props.Count} prop(s)";
        }, combat: true);
        Reg("destroy-scenery", "destroy-scenery <prop-path>... | all", "wreck intact or damaged props", a =>
        {
            var props = a.Count == 1 && a[0].Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Save.Combat!.Props.Where(x => x.Condition != "Destroyed").Select(x => x.Actor).ToList() : a.ToList();
            p.QueueSceneryDestroys(props); return $"destroy {props.Count} prop(s)";
        }, combat: true);
        Reg("spawn-set", "spawn-set <set>", "swap the mission's enemy threat tier", a =>
        {
            var s = Save.Combat!.SpawnSet ?? throw new ZcException("this save has no enemy spawn set");
            var set = Choice("spawn set", a[0], s.Choices); p.SetSpawnSet(set, s.Current); return $"enemy spawn set {set}";
        }, combat: true);
        Reg("turn-structure", "turn-structure <Standard|ThreeWay>", "standard or three-way turn structure (experimental)", a =>
        {
            var t = Save.Combat!.TurnStructure ?? throw new ZcException("this save has no turn structure");
            var v = Choice("turn structure", a[0], t.Choices); p.SetTurn(v, t.Current); return $"turn structure {v}";
        }, combat: true);
        Reg("trigger", "trigger <logic> <on|off>", "switch a reinforcement trigger on or off", a =>
        {
            int logic = (int)Count(a[0], 0, 9999);
            var t = Save.Combat!.Reinforcements.Triggers.FirstOrDefault(x => x.Logic == logic) ?? throw new ZcException($"no trigger {logic} (see: zc save profile)");
            bool on = Bool(a[1]); p.SetTrigger(logic, on, t.Active); return $"trigger {t.Title}: {(on ? "on" : "off")}";
        }, combat: true);
        Reg("objective", "objective <index> <Active|Succeeded>", "set an objective's status", a =>
        {
            int i = (int)Count(a[0], 0, 9999);
            var o = Save.Combat!.Objectives.FirstOrDefault(x => x.Index == i) ?? throw new ZcException($"no objective {i} (see: zc save profile)");
            if (!o.CanEdit) throw new ZcException($"{o.Title} has not started, so its status cannot be changed");
            var st = Choice("status", a[1], new[] { ("Active", "Active"), ("Succeeded", "Succeeded") }); p.SetObjective(i, st, o.Status); return $"objective {o.Title}: {st}";
        }, combat: true);
        Reg("add-squad", "add-squad <operator>|all [heal]", "bring a roster operator into this fight (the game spawns them when the save loads); heal also removes injuries so they arrive at full health", a =>
        {
            bool heal = a.Count > 1 && a[1].Equals("heal", StringComparison.OrdinalIgnoreCase);
            if (a.Count > 1 && !heal) throw new ZcException("the only option is 'heal'");
            var open = Save.Combat!.Squad.Where(m => !m.InSquad && !m.Dead).ToList();
            var who = a[0].Equals("all", StringComparison.OrdinalIgnoreCase) ? open : new List<SquadMemberView> { Pick("operator", open, a[0], x => x.Guid, x => x.Name) };
            foreach (var m in who) { p.SetSquadAdd(m.Guid, true); if (heal && m.Injuries > 0) p.ToggleHeal(m.Guid, true); }
            return who.Count == 1 ? $"bring {who[0].Name} into the fight" : $"bring {who.Count} operator(s) into the fight";
        }, combat: true);
        Reg("round", "round <n>", "set the round the fight is on", a =>
        {
            int r = (int)Count(a[0], 0, 9999); p.SetRound(r, Save.Combat!.Round ?? 0); return $"round {r}";
        }, combat: true);
    }

    // ---- argument lookups ----------------------------------------------------------------------------

    static double Count(string text, int lo, int hi)
    {
        var v = ParseNumber(text);
        if (v != Math.Floor(v) || v < lo || v > hi) throw new ZcException($"'{text}' must be a whole number from {lo} to {hi}");
        return v;
    }

    static bool Bool(string text) => text.ToLowerInvariant() switch
    {
        "true" or "on" or "yes" or "1" => true,
        "false" or "off" or "no" or "0" => false,
        _ => throw new ZcException($"'{text}' must be true or false"),
    };

    /// <summary>The single item of <paramref name="items"/> that <paramref name="what"/> names by id or title (ignoring case and punctuation).</summary>
    static T Pick<T>(string noun, IEnumerable<T> items, string what, Func<T, string> id, Func<T, string> title)
    {
        var list = items.ToList();
        var n = NormName(what);
        var m = list.Where(x => id(x).Equals(what, StringComparison.OrdinalIgnoreCase) || NormName(title(x)) == n).ToList();
        if (m.Count == 0) m = list.Where(x => n.Length > 0 && NormName(title(x)).Contains(n)).ToList();
        if (m.Count == 1) return m[0];
        if (m.Count == 0) throw new ZcException($"no {noun} '{what}'");
        throw new ZcException($"'{what}' matches {m.Count} {noun}s: {string.Join(", ", m.Take(8).Select(x => $"{title(x)} [{id(x)}]"))}");
    }

    OperatorView Op(string who) =>
        Pick("operator", Save.View.Personnel.Roster.Concat(Save.View.Personnel.Memorial), who, o => o.Guid, o => o.Name);

    CombatUnitView Unit(string which)
    {
        var c = Save.Combat ?? throw new ZcException("this is not a combat save");
        if (int.TryParse(which, NumberStyles.None, CultureInfo.InvariantCulture, out var i))
            return c.Units.FirstOrDefault(u => u.Index == i) ?? throw new ZcException($"no unit {i} (the save has {c.Units.Count}; see: zc save profile)");
        return Pick("unit", c.Units, which, u => u.Index.ToString(CultureInfo.InvariantCulture), u => u.Name);
    }

    ArmoryAddView Item(string what)
    {
        var addable = Save.View.Armory.Addable;
        if (addable.Count == 0) throw new ZcException("no game data is installed, so the items that can be added are unknown (run: zc data extract)");
        return Pick("item", addable, what, x => x.Asset, x => x.Name);
    }

    UpgradeRow Upgrade(string what) =>
        Pick("upgrade", Save.Upgrades.Items.Where(u => u.Id != null), what, u => u.Id!, u => u.Title.Length > 0 ? u.Title : u.Name);

    /// <summary>One of <paramref name="choices"/> by value or label; any text when there are no choices to check against.</summary>
    static string Choice(string noun, string given, IEnumerable<(string Value, string Label)> choices)
    {
        var list = choices.ToList();
        if (list.Count == 0) return given;
        var n = NormName(given);
        var m = list.Where(c => c.Value.Equals(given, StringComparison.OrdinalIgnoreCase)).ToList();
        if (m.Count == 0) m = list.Where(c => NormName(c.Label) == n || NormName(c.Value) == n).ToList();
        if (m.Count == 0 && n.Length > 0) m = list.Where(c => NormName(c.Value).Contains(n) || NormName(c.Label).Contains(n)).ToList();
        if (m.Count == 1) return m[0].Value;
        throw new ZcException(m.Count == 0
            ? $"unknown {noun} '{given}'; choices: {string.Join(", ", list.Take(10).Select(c => c.Value == c.Label ? c.Value : $"{c.Label} [{c.Value}]"))}{(list.Count > 10 ? ", ..." : "")}"
            : $"'{given}' matches {m.Count} {noun}s; be more specific: {string.Join(", ", m.Take(6).Select(c => c.Value))}");
    }
}
