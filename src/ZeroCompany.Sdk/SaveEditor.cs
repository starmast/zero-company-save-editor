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

    /// <summary>An ability tag without its 'br.AbilityID.&lt;kind&gt;.' start: 'CombatJump', or 'Ambush.2nd' for a second specialization's copy.</summary>
    static string ShortAbility(string tag)
    {
        var parts = tag.Split('.');
        return parts.Length > 3 ? string.Join('.', parts.Skip(3)) : tag;
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
        Reg("grant-ability", "grant-ability <operator> <ability> [level]", "give an operator an ability another operator holds (e.g. CombatJump, the jetpack)", a =>
        {
            var o = Op(a[0]);
            var opts = AbilityOps.Grantable(Save.Gvas, o.Guid);
            string key = NormName(a[1]);
            var hit = opts.Where(d => NormName(ShortAbility(d.Tag)) == key || NormName(d.Tag) == key).ToList();
            if (hit.Count != 1)
                throw new ZcException(hit.Count == 0
                    ? $"{o.Name} cannot be given '{a[1]}'. Options: {(opts.Count == 0 ? "none (no other operator in this save holds an ability that fits)" : string.Join(", ", opts.Select(d => ShortAbility(d.Tag))))}"
                    : $"'{a[1]}' matches more than one ability: {string.Join(", ", hit.Select(d => d.Tag))}");
            int level = a.Count > 2 ? (int)Count(a[2], 1, 6) : 1;
            p.SetGrant(o.Guid, hit[0].Tag, level);
            return $"give {o.Name} {hit[0].Tag} at level {level}";
        });
        Reg("zone-claimed", "zone-claimed <zone> <tier> <true|false>", "mark a galaxy zone's influence reward tier (1 = first) claimed or not; false lets the game offer it again, e.g. zone-claimed Jenth 3 false", a =>
        {
            string key = NormName(a[0]);
            var zones = Save.View.Galaxy.Regions.Where(r => r.Claimed != null).ToList();
            // an exact zone tag ("OuterSouth") wins over a part of a name ("Jenth" in "Zone Jenth (Outer South)")
            var hit = zones.Where(r => NormName(r.Tag[(r.Tag.LastIndexOf('.') + 1)..]) == key).ToList();
            if (hit.Count == 0) hit = zones.Where(r => key.Length >= 3 && NormName(r.Name).Contains(key)).ToList();
            if (hit.Count != 1)
                throw new ZcException($"{(hit.Count == 0 ? "no zone" : "more than one zone")} matches '{a[0]}'. Zones: {string.Join(", ", zones.Select(r => r.Name))}");
            var z = hit[0];
            int tier = (int)Count(a[1], 1, ZoneRewardOps.MaxTier + 1) - 1;
            bool claimed = a.Count > 2 && bool.TryParse(a[2], out var b) ? b : throw new ZcException("usage: zone-claimed <zone> <tier> <true|false>");
            p.SetZoneTierClaimed(z.Tag, tier, claimed, z.Claimed!);
            return $"{z.Name}: {z.TierLabel(tier)} {(claimed ? "marked claimed" : "claimable again")}";
        });
        Reg("add-stat", "add-stat <counter> <count>", "start an achievement counter the profile (SharedGameSettings.sav) does not have yet, e.g. add-stat AngryBeeps 1", a =>
        {
            if (!ProfileOps.IsProfile(Save.Gvas)) throw new ZcException("'add-stat' only works on the profile file, SharedGameSettings.sav");
            string key = NormName(a[0]);
            var hit = ProfileOps.Achievements.Where(x => NormName(x.Stat) == key || NormName(x.Id) == key).Select(x => x.Stat).Distinct().ToList();
            if (hit.Count != 1) throw new ZcException($"'{a[0]}' is not an achievement counter. Counters: {string.Join(", ", ProfileOps.Achievements.Select(x => x.Stat))}");
            if (ProfileOps.Counters(Save.Gvas).Any(c => c.Stat == hit[0]))
                throw new ZcException($"the profile already counts {hit[0]}; set it with: zc edit set <file> stat.{hit[0]} <count>");
            if (a.Count < 2 || !long.TryParse(a[1], out var count) || count < 0) throw new ZcException("usage: add-stat <counter> <count> (a whole number of 0 or more)");
            p.SetProfileCounterAdd(hit[0], count);
            return $"start counting {hit[0]} at {count}";
        });
        Reg("give-trait", "give-trait <operator> <trait>", "give an operator a bond trait (a class trait normally learned through cross training), e.g. give-trait Hawks Grenadier", a =>
        {
            var o = Op(a[0]);
            var opts = TraitOps.Givable(Save.Gvas, o.Guid);
            if (opts.Count == 0)
                throw new ZcException(TraitOps.CanGive(Save.Gvas, o.Guid) ? $"{o.Name} already has every bond trait" : $"{o.Name} has no effect record a trait can be copied from");
            string key = NormName(string.Join(" ", a.Skip(1)));
            var hit = opts.Where(t => NormName(t.Name) == key || NormName(t.Asset) == key || NormName(t.Asset["GE_".Length..]) == key).ToList();
            if (hit.Count != 1)
                throw new ZcException($"{o.Name} cannot be given '{string.Join(" ", a.Skip(1))}'. Options: {string.Join(", ", opts.Select(t => $"{t.Name} ({t.Class})"))}");
            p.SetTrait(o.Guid, hit[0].Asset, true);
            return $"give {o.Name} the bond trait {hit[0].Name}";
        });
        Reg("give-stat", "give-stat <operator> <stat>", "give an operator a cross-training stat upgrade at one stack (raise the stacks afterwards with set), e.g. give-stat Hawks \"Ranged Damage\"", a =>
        {
            var o = Op(a[0]);
            var opts = StatOps.Givable(Save.Gvas, o.Guid);
            if (opts.Count == 0)
                throw new ZcException(StatOps.CanGive(Save.Gvas, o.Guid) ? $"{o.Name} already has every stat upgrade" : $"{o.Name} has no effect record a stat can be copied from");
            string key = NormName(string.Join(" ", a.Skip(1)));
            var hit = opts.Where(s => NormName(s.Name) == key || NormName(s.Asset) == key || NormName(s.Asset["GE_CrossTraining_".Length..]) == key).ToList();
            if (hit.Count != 1)
                throw new ZcException($"{o.Name} cannot be given '{string.Join(" ", a.Skip(1))}'. Options: {string.Join(", ", opts.Select(s => s.Name))}");
            p.SetStat(o.Guid, hit[0].Asset, true);
            return $"give {o.Name} the stat upgrade {hit[0].Name}";
        });
        Reg("set-market","set-market <mods|utility> <tier 1-3> <count>", "how many items the Black Market rolls per refresh from one quality table (1 Basic, 2 Superior, 3 Exquisite)", a =>
        {
            string kind = a[0].ToLowerInvariant();
            if (!MarketOps.Kinds.Contains(kind)) throw new ZcException("the kind must be mods or utility");
            int tier = (int)Count(a[1], MarketOps.MinTier, MarketOps.MaxTier);
            int n = a.Count > 2 ? (int)Count(a[2], 0, MarketOps.MaxCount) : throw new ZcException("usage: set-market <mods|utility> <tier 1-3> <count>");
            p.SetMarketTable(kind, tier, n);
            return $"Black Market rolls {n} {MarketOps.Quality(tier)} {kind} item(s) per refresh";
        });
        Reg("set-option", "set-option <option> <choice>", "change a campaign option picked when the campaign started (game 1.2), e.g. set-option EnemyCrits On", a =>
        {
            var held = CampaignOptionOps.Read(Save.Gvas) ?? throw new ZcException("this campaign has no campaign options (it was started before game 1.2)");
            var known = CampaignOptionOps.Settings.Where(s => held.ContainsKey(s.Key)).ToList();
            string key = NormName(a[0]);
            var hit = known.Where(s => NormName(s.Key) == key || NormName(s.Key[(s.Key.IndexOf('_', 3) + 1)..]) == key || NormName(s.Label) == key).ToList();
            if (hit.Count != 1)
                throw new ZcException($"unknown option '{a[0]}'. Options: {string.Join(", ", known.Select(s => $"{s.Key[(s.Key.IndexOf('_', 3) + 1)..]} ({s.Label})"))}");
            var opt = hit[0];
            var choice = opt.Choices.FirstOrDefault(c => NormName(c.Label) == NormName(a[1]) || c.Tag == a[1])
                         ?? throw new ZcException($"{opt.Label}: choose one of {string.Join(", ", opt.Choices.Select(c => c.Label))}");
            p.SetCampaignOption(opt.Key, choice.Tag == held[opt.Key] ? null : choice.Tag);
            return choice.Tag == held[opt.Key] ? $"{opt.Label} is already {choice.Label}" : $"campaign option {opt.Label}: {choice.Label}";
        });
        Reg("set-slots", "set-slots <mod|utility> <total>", "give every operator this many gear slots (mod 1-4, utility 1-12)", a =>
        {
            string type = a[0].ToLowerInvariant();
            if (type is not ("mod" or "utility")) throw new ZcException("the slot type must be mod or utility");
            int top = type == "mod" ? GearOps.MaxModSlots : GearOps.MaxUtilitySlots;
            int n = (int)Count(a[1], 1, top);
            p.SetGearSlots(type, n);
            return $"every operator gets {n} {type} slots";
        });
        Reg("equip", "equip <operator> <utility|mod> <slot> <item|empty>", "put an owned item in an operator's gear slot (slots count from 1), or empty the slot", a =>
        {
            var o = Op(a[0]);
            var type = a[1].ToLowerInvariant();
            if (type is not ("utility" or "mod")) throw new ZcException("the slot type must be utility or mod");
            var slots = Save.View.Loadout.Slots.GetValueOrDefault(o.Guid)?.Where(s => s.Type == type).ToList() ?? new();
            if (slots.Count == 0) throw new ZcException($"{o.Name} has no {type} slots");
            int slot = (int)Count(a[2], 1, slots.Count) - 1;
            string? saved = slots[slot].Item.Length == 0 ? null : slots[slot].Item;
            if (a[3].Equals("empty", StringComparison.OrdinalIgnoreCase) || a[3].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                p.SetEquip(o.Guid, type, slot, null, saved); return $"empty {o.Name}'s {type} slot {slot + 1}";
            }
            var it = Pick("item", Save.View.Loadout.Items.Where(i => i.Type == type), a[3], x => x.Guid, x => x.Name);
            p.SetEquip(o.Guid, type, slot, it.Guid, saved);
            return $"put {it.Name} in {o.Name}'s {type} slot {slot + 1}" + (it.HeldBy.Length > 0 && it.HeldBy != o.Guid ? $" (takes it from {it.HeldByName})" : "");
        });
        Reg("copy-weapon", "copy-weapon <operator> [donor|Rifle|Pistol|Longarm|Repeater]", "give an operator a copy of another operator's vanilla weapon, or of a generic one (the first suitable donor, else the generic weapon of the operator's class)", a =>
        {
            var o = Op(a[0]);
            string? source = null, what = "another operator's (or a generic) weapon";
            if (a.Count > 1)
            {
                if (WeaponOps.Template(a[1]) is { } t) { source = t.Key; what = "the " + t.Label; }
                else { var d = Op(a[1]); source = d.Guid; what = $"{d.Name}'s weapon"; }
            }
            p.SetWeaponCopy(o.Guid, true, source); return $"give {o.Name} a copy of {what}";
        });
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
        Reg("unit-team", "unit-team <unit> <Player|Enemy|Enemy2|Civilian>", "move a unit to another team", a =>
        {
            var u = Unit(a[0]);
            var team = CombatOps.Teams.FirstOrDefault(t => string.Equals(t, a[1], StringComparison.OrdinalIgnoreCase))
                       ?? throw new ZcException($"the team must be one of {string.Join(", ", CombatOps.Teams)}");
            if (!CombatOps.Teams.Contains(u.Team)) throw new ZcException($"unit {u.Index} {u.Name} is on no side the game fights for ({u.Team}), so its team cannot be changed");
            p.SetTeam(u.Index, team, u.Team); return $"unit {u.Index} {u.Name}: team {team}";
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
