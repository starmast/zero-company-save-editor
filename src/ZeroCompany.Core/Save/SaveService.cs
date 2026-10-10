using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core.Save;

/// <summary>
/// Open / edit / save workflow with backups and post-write verification. Nothing reaches the disk until the rebuilt file
/// has been re-parsed and proven to differ from the original only where an edit intended it to.
/// </summary>
public sealed class SaveService
{
    public const int KeepBackups = 20;
    static readonly Regex SafeName = new(@"^[A-Za-z0-9._ \-+()]+\.sav$", RegexOptions.Compiled);
    static readonly Regex GuidRx = new("^[0-9A-F]{32}$", RegexOptions.Compiled);
    static readonly Regex RecipeIdRx = new(@"^r\d+$", RegexOptions.Compiled);
    static readonly string[] GameProcessHints = { "swzerocompany", "bruno" };

    readonly string _backupRoot;
    readonly Func<string?> _gameRunning;
    readonly object _lock = new();

    public GameDatabase Db { get; set; }

    /// <summary>Save folders (id -> path); can be replaced while a save is open.</summary>
    public IReadOnlyDictionary<string, string> Dirs { get; set; }
    public OpenSave? Current { get; private set; }

    public SaveService(IReadOnlyDictionary<string, string> saveDirs, string backupRoot, GameDatabase? db = null,
                       Func<string?>? gameRunning = null)
    {
        Dirs = saveDirs;
        _backupRoot = backupRoot;
        Db = db ?? GameDatabase.Empty;
        _gameRunning = gameRunning ?? DefaultGameRunning;
        Directory.CreateDirectory(backupRoot);
    }

    /// <summary>Name of a running game process, or null (also null when processes cannot be listed).</summary>
    public static string? DefaultGameRunning()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                string name;
                try { name = p.ProcessName.ToLowerInvariant(); } catch { continue; } finally { p.Dispose(); }
                if (GameProcessHints.Any(name.Contains)) return name;
            }
        }
        catch (Exception) { }
        return null;
    }

    public string? GameRunning() => _gameRunning();

    // -- path safety -------------------------------------------------------
    public string Resolve(string dirId, string name)
    {
        if (!Dirs.TryGetValue(dirId, out var dir)) throw new EditException("unknown save folder");
        if (Path.GetFileName(name) != name || !SafeName.IsMatch(name)) throw new EditException("invalid file name");
        var path = Path.Combine(dir, name);
        if (!File.Exists(path)) throw new EditException("file not found");
        return path;
    }

    /// <summary>Backups live in &lt;root&gt;/&lt;folder id&gt;/&lt;save name&gt;/ so same-named saves in different folders never share history.</summary>
    public string BackupDir(string name, string? dirId = null)
    {
        dirId ??= Current?.DirId;
        if (dirId == null || !Dirs.ContainsKey(dirId)) throw new EditException("unknown save folder");
        var d = Path.Combine(_backupRoot, dirId, Path.GetFileNameWithoutExtension(name));
        Directory.CreateDirectory(d);
        return d;
    }

    // -- images (read-only) ------------------------------------------------
    public byte[]? Thumbnail(string dirId, string name) => SaveContainer.ReadThumbnail(Resolve(dirId, name));

    // -- listing -----------------------------------------------------------
    public List<SaveEntry> ListSaves()
    {
        var @out = new List<SaveEntry>();
        foreach (var (id, dir) in Dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var fi in new DirectoryInfo(dir).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc))
                if (SafeName.IsMatch(fi.Name)) @out.Add(new SaveEntry(id, fi.Name, SaveContainer.Summarize(fi.FullName)));
        }
        return @out;
    }

    // -- open --------------------------------------------------------------
    public OpenSave Open(string dirId, string name)
    {
        var path = Resolve(dirId, name);
        var raw = File.ReadAllBytes(path);
        OpenSave open;
        try
        {
            var loaded = SaveContainer.Load(raw);
            if (!loaded.IsZip) throw new EditException("This file type (settings/databank) is not editable here.");
            var gv = new GvasFile(loaded.Gvas);
            var model = new SaveModel(gv, loaded.MetadataJson, Db);
            var info = loaded.SaveInfo();
            var portraits = new PortraitStore(loaded.Blobs);
            var upgrades = UpgradeOps.List(gv, Db);
            open = new OpenSave
            {
                DirId = dirId, Name = name, Path = path, Container = loaded, Gvas = gv, Model = model,
                Scalars = SaveModel.ScalarNodes(gv), DiskHash = Sha(raw), Portraits = portraits, Info = info,
                Upgrades = upgrades,
                View = ViewBuilder.Build(model, info, portraits.Guids, upgrades),
            };
        }
        catch (Exception e) when (e is SaveFormatException or GvasException)
        {
            throw new EditException($"Could not read save: {e.Message}", e);
        }
        EnsureOriginal(dirId, name, raw);
        lock (_lock) Current = open;
        return open;
    }

    // -- backups -----------------------------------------------------------
    void EnsureOriginal(string dirId, string name, byte[] raw)
    {
        var d = BackupDir(name, dirId);
        if (!Directory.EnumerateFiles(d).Any(f => f.EndsWith(".orig.sav", StringComparison.Ordinal)))
            WriteFile(Path.Combine(d, $"{DateTime.Now:yyyyMMdd-HHmmss}.orig.sav"), raw);
    }

    public string Snapshot(string name, string path)
    {
        var d = BackupDir(name);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dest = Path.Combine(d, $"{stamp}.sav");
        for (int n = 1; File.Exists(dest); n++) dest = Path.Combine(d, $"{stamp}-{n}.sav");
        File.Copy(path, dest);
        var snaps = Directory.EnumerateFiles(d).Select(Path.GetFileName).Where(f => !f!.EndsWith(".orig.sav", StringComparison.Ordinal))
                             .OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var old in snaps.Take(Math.Max(0, snaps.Count - KeepBackups))) File.Delete(Path.Combine(d, old!));
        return dest;
    }

    public List<BackupInfo> ListBackups(string name)
    {
        var d = BackupDir(name);
        return new DirectoryInfo(d).GetFiles().OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, f.Length, f.LastWriteTimeUtc, f.Name.EndsWith(".orig.sav", StringComparison.Ordinal))).ToList();
    }

    static void WriteFile(string path, byte[] data)
    {
        using var f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        f.Write(data, 0, data.Length);
        f.Flush(true);
    }

    static void AtomicReplace(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        try
        {
            WriteFile(tmp, data);
            if (Sha(File.ReadAllBytes(tmp)) != Sha(data)) throw new EditException("temp file verification failed; original untouched");
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    // -- validation --------------------------------------------------------
    static readonly Dictionary<string, (double Lo, double Hi)> TypeRanges = new()
    {
        ["IntProperty"] = (int.MinValue, int.MaxValue), ["UInt32Property"] = (0, uint.MaxValue),
        ["Int64Property"] = (long.MinValue, long.MaxValue), ["UInt64Property"] = (0, ulong.MaxValue),
        ["Int16Property"] = (short.MinValue, short.MaxValue), ["UInt16Property"] = (0, ushort.MaxValue),
        ["Int8Property"] = (-128, 127), ["FloatProperty"] = (-3.4e38, 3.4e38), ["DoubleProperty"] = (-1.7e308, 1.7e308),
    };

    /// <summary>Range-check a client-supplied value; throws <see cref="ArgumentException"/> with a user-facing message.</summary>
    public static double Validate(string kind, double value, double lo, double hi)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("value must be finite");
        if (kind != "float" && value != Math.Floor(value)) throw new ArgumentException("value must be a whole number");
        if (value < lo || value > hi) throw new ArgumentException($"value {Fmt(value)} out of range [{Fmt(lo)}, {Fmt(hi)}]");
        return value;
    }

    static string Fmt(double v) => v.ToString("G15", System.Globalization.CultureInfo.InvariantCulture);

    // -- apply -------------------------------------------------------------
    public ApplyResult Apply(IReadOnlyList<FieldChange> changes, bool force = false, bool asCopy = false,
                             IReadOnlyList<SaveAction>? actions = null)
    {
        var c = Current ?? throw new EditException("no save open");
        actions ??= Array.Empty<SaveAction>();
        if (changes.Count == 0 && actions.Count == 0) throw new EditException("no changes");
        if (!asCopy && !force)
        {
            var proc = _gameRunning();
            if (proc != null) throw new EditException($"The game appears to be running ({proc}). Close it first (or tick the override).");
        }
        if (!asCopy && Sha(File.ReadAllBytes(c.Path)) != c.DiskHash)
            throw new EditException("The save changed on disk since you opened it (the game may have autosaved). Re-open it before editing.");

        // 1) validate and compute every scalar write up front
        var writes = new Dictionary<int, (GvasNode Node, double Value)>();
        foreach (var ch in changes)
        {
            if (!c.Scalars.TryGetValue(ch.Id, out var node)) throw new EditException($"unknown field '{ch.Id}'");
            c.Model.ById.TryGetValue(ch.Id, out var f);
            var tr = TypeRanges[node.TName];
            double lo = f?.Min ?? tr.Lo, hi = f?.Max ?? tr.Hi;
            var kind = f?.Kind ?? (node.TName is "FloatProperty" or "DoubleProperty" ? "float" : "int");
            lo = Math.Max(lo, tr.Lo); hi = Math.Min(hi, tr.Hi);
            double neu;
            try { neu = Validate(kind, ch.Value, lo, hi); }
            catch (ArgumentException e) { throw new EditException($"{f?.Label ?? ch.Id}: {e.Message}", e); }
            double old = c.Gvas.GetDouble(node);
            writes[node.ValueOffset] = (node, neu);
            if (f != null && f.Linked.Count > 0 && neu != old && ch.Link)
                foreach (var ln in f.Linked)
                {
                    double cur = writes.TryGetValue(ln.ValueOffset, out var w) ? w.Value : c.Gvas.GetDouble(ln);
                    double lnNew = cur + (neu - old);
                    var llr = TypeRanges[ln.TName];
                    if (lnNew < llr.Lo || lnNew > llr.Hi) throw new EditException($"{f.Label}: linked value out of range");
                    writes[ln.ValueOffset] = (ln, lnNew);
                }
        }

        // 2) patch an independent copy (scalars are in place, offsets unchanged)
        var patched = new GvasFile(c.Container.Gvas);
        var edited = new List<(int Off, int Size, string Type, double Value)>();
        foreach (var (off, (node, val)) in writes)
        {
            patched.Set(node, IsIntegral(node.TName) ? (object)(long)val : val);
            edited.Add((off, GvasFile.Scalars[node.TName], node.TName, val));
        }

        // 3) structural actions (change sizes; every ancestor size is fixed up)
        var expected = new HashSet<string>(writes.Values.Select(w => PathKey.Of(GvasFile.PathOf(w.Node))));
        var cut = new CutInfo();
        if (actions.Count > 0) patched = RunActions(c, patched, actions, expected, cut);
        var newGvas = patched.ToBytes();
        bool structural = newGvas.Length != c.Container.Gvas.Length;

        var replace = new Dictionary<string, byte[]>();
        if (structural && c.Container.IsZip)
        {
            var meta = c.Container.SyncMetadataSizes(BlobSizes(patched), MedbayOps.CharacterSizes(patched).PerCharacter);
            if (meta != null) replace["SaveGameMetaData.json"] = meta;
        }
        var newFile = c.Container.Rebuild(newGvas, replace);

        // 4) verify before touching the disk
        if (structural || actions.Count > 0) VerifySemantic(c, newFile, expected, cut);
        else Verify(c, newFile, newGvas, edited);

        int count = writes.Count + actions.Count;
        if (asCopy)
        {
            var ext = Path.GetExtension(c.Name);
            var destName = $"{Path.GetFileNameWithoutExtension(c.Name)}.edited-{DateTime.Now:HHmmss}{ext}";
            AtomicReplace(Path.Combine(Path.GetDirectoryName(c.Path)!, destName), newFile);
            return new ApplyResult(destName, true, count, null);
        }
        var snap = Snapshot(c.Name, c.Path);
        AtomicReplace(c.Path, newFile);
        Open(c.DirId, c.Name);                    // re-open so the UI shows what is really on disk now
        return new ApplyResult(c.Name, false, count, Path.GetFileName(snap));
    }

    static bool IsIntegral(string tname) => tname is not ("FloatProperty" or "DoubleProperty");

    /// <summary>Byte counts of the two top-level archives, as mirrored in SaveGameMetaData.json.</summary>
    static Dictionary<string, int> BlobSizes(GvasFile g)
    {
        var @out = new Dictionary<string, int>();
        foreach (var w in g.Root)
        {
            var ab = g.Child(w, "ArchiveBytes");
            if (ab?.Count == null) continue;
            if (w.Name == "GameInstanceSaveGameWrapper") @out["GameInstanceSize"] = ab.Count.Value;
            else if (w.Name == "StrategySaveGameWrapper") @out["StrategySize"] = ab.Count.Value;
        }
        var (total, _) = MedbayOps.CharacterSizes(g);
        if (total != 0) @out["characterDataWrapperSize"] = total;
        return @out;
    }

    // -- actions -----------------------------------------------------------
    GvasFile RunActions(OpenSave c, GvasFile g, IReadOnlyList<SaveAction> actions, HashSet<string> touched, CutInfo cut)
    {
        var seen = new HashSet<string>();
        var healed = new HashSet<string>(); var revived = new HashSet<string>(); var completed = new HashSet<string>();
        bool rosterDone = false, coilDone = false;

        static string NormGuid(string raw)
        {
            var guid = (raw ?? "").ToUpperInvariant();
            if (!GuidRx.IsMatch(guid)) throw new EditException("bad operator reference");
            return guid;
        }

        foreach (var a in actions)
        {
            switch (a)
            {
                case SaveAction.HealOperator h:
                {
                    var guid = NormGuid(h.Guid);
                    if (!healed.Add(guid)) throw new EditException("bad operator reference");
                    g = Heal(c, g, guid, touched, cut);
                    break;
                }
                case SaveAction.CompleteFocusTree f:
                {
                    var guid = NormGuid(f.Guid);
                    if (!completed.Add(guid)) throw new EditException("bad operator reference");
                    g = CompleteFocus(c, g, guid, cut);
                    break;
                }
                case SaveAction.ReviveOperator r:
                {
                    var guid = NormGuid(r.Guid);
                    if (!revived.Add(guid)) throw new EditException("bad operator reference");
                    g = Revive(c, g, guid, touched, cut);
                    break;
                }
                case SaveAction.ReorderRoster ro:
                    if (rosterDone) throw new EditException("duplicate roster action");
                    rosterDone = true;
                    g = Reorder(g, ro.Order, touched, cut);
                    break;
                case SaveAction.RemoveCoilUpgrades rc:
                    if (coilDone) throw new EditException("duplicate Coil action");
                    coilDone = true;
                    g = RemoveCoil(g, rc.Changes, touched, cut);
                    break;
                case SaveAction.StartUpgrade s:
                    g = RunUpgrade(c, g, s.Id, s.Name, start: true, expedite: false, seen, touched);
                    break;
                case SaveAction.ExpediteUpgrade x:
                    g = RunUpgrade(c, g, x.Id, x.Name, start: false, expedite: true, seen, touched);
                    break;
                case SaveAction.StartExpediteUpgrade se:
                    g = RunUpgrade(c, g, se.Id, se.Name, start: true, expedite: true, seen, touched);
                    break;
                default:
                    throw new EditException($"unknown action {a.GetType().Name}");
            }
        }
        return g;
    }

    GvasFile Heal(OpenSave c, GvasFile g, string guid, HashSet<string> touched, CutInfo cut)
    {
        if (!c.Model.Operators.TryGetValue(guid, out var op) || op.Dead || op.Injuries == 0)
            throw new EditException("That operator is not injured (or has fallen); re-open the save.");
        try { return MedbayOps.HealOperator(g, guid, cut, touched); }
        catch (GvasException e) { throw new EditException($"{op.Name}: {e.Message}", e); }
    }

    GvasFile RemoveCoil(GvasFile g, IReadOnlyList<CoilChange> changes, HashSet<string> touched, CutInfo cut)
    {
        bool ok = changes != null && changes.Count > 0 && changes.All(ch =>
            ch != null && !string.IsNullOrEmpty(ch.Id) && CoilOps.States.Contains(ch.To) && CoilOps.IdRx.IsMatch(ch.Id));
        if (!ok || changes!.Select(x => x.Id).Distinct().Count() != changes!.Count) throw new EditException("bad Coil upgrade reference");
        try { return CoilOps.Remove(g, changes.ToDictionary(x => x.Id, x => x.To), cut, touched); }
        catch (GvasException e) { throw new EditException($"Coil upgrades: {e.Message}", e); }
    }

    GvasFile Revive(OpenSave c, GvasFile g, string guid, HashSet<string> touched, CutInfo cut)
    {
        var model = c.Model;
        if (!model.Operators.TryGetValue(guid, out var op) || !model.Dead.Contains(guid))
            throw new EditException("That operator is not among the fallen; re-open the save.");
        int cap = (int)Math.Round(model.Fact("Facts.Values.MaximumRosterCount", 0));
        try { g = ReviveOps.Revive(g, guid, cap == 0 ? null : cap, cut, touched); }
        catch (GvasException e) { throw new EditException($"{op.Name}: {e.Message}", e); }
        try { g = CompleteFocus(c, g, guid, cut); }                    // a returning operator gets a complete focus tree too
        catch (EditException) { }
        return g;
    }

    GvasFile CompleteFocus(OpenSave c, GvasFile g, string guid, CutInfo cut)
    {
        if (!c.Model.Operators.ContainsKey(guid)) throw new EditException("unknown operator");
        try { return FocusOps.Complete(g, guid, cut); }
        catch (GvasException e) { throw new EditException($"Focus tree: {e.Message}", e); }
    }

    GvasFile Reorder(GvasFile g, IReadOnlyList<string> order, HashSet<string> touched, CutInfo cut)
    {
        if (order == null || order.Count == 0 || order.Any(x => x == null || !GuidRx.IsMatch(x))) throw new EditException("bad roster order");
        try { return RosterOps.Reorder(g, order, cut, touched); }
        catch (GvasException e) { throw new EditException($"Roster: {e.Message}", e); }
    }

    GvasFile RunUpgrade(OpenSave c, GvasFile g, string rid, string name, bool start, bool expedite, HashSet<string> seen, HashSet<string> touched)
    {
        if (!RecipeIdRx.IsMatch(rid ?? "") || !seen.Add(rid!)) throw new EditException("bad upgrade reference");
        var row = UpgradeOps.List(g, Db).Items.FirstOrDefault(r => r.Id == rid);
        if (row == null || row.Name != name) throw new EditException("upgrade list changed; re-open the save");
        if (start && !row.CanStart) throw new EditException($"{row.Title}: {(string.IsNullOrEmpty(row.Reason) ? "cannot be started" : row.Reason)}");
        if (!start && expedite && !row.CanExpedite) throw new EditException($"{row.Title}: not in progress");
        int idx = int.Parse(rid![1..]);
        var v = g.Child(g.StrategyData(), "ActiveRecipes")!.Children![idx].Children![1];
        var baseKey = GvasFile.PathOf(v);
        try
        {
            if (start) g = UpgradeOps.Start(g, idx, name);
            if (expedite) g = UpgradeOps.Expedite(g, idx, name);
        }
        catch (GvasException e) { throw new EditException($"{row.Title}: {e.Message}", e); }
        var tails = new List<string[]>();
        if (start) tails.AddRange(new[] { new[] { "Status" }, new[] { "TurnStarted" }, new[] { "FulfilledRewardTiers" },
                                          new[] { "InProgressRecipeContext", "FacilityTag", "TagName" } });
        if (expedite) tails.AddRange(new[] { new[] { "TurnStarted" }, new[] { "InProgressTurns" } });
        foreach (var t in tails) touched.Add(PathKey.Of(baseKey, t));
        return g;
    }

    // -- verification ------------------------------------------------------
    /// <summary>Map structural path -> comparable content for every node (leaf bytes / counts).</summary>
    static Dictionary<string, object?> Flatten(GvasFile g)
    {
        var @out = new Dictionary<string, object?>();
        void Rec(IReadOnlyList<GvasNode> nodes, string prefix, IReadOnlyList<string>? keys)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var key = keys != null ? keys[i] : n.Name;
                var p = prefix.Length == 0 ? key : prefix + PathKey.Sep + key;
                if (n.Children != null)
                {
                    if (!n.Nested) @out[p + PathKey.Sep + "#count"] = n.Count;     // nested count is a byte length, not elements
                    Rec(n.Children, p, n.Name == MedbayOps.Effects ? MedbayOps.EffectKeys(g, n) : null);
                }
                else @out[p] = g.Data[n.ValueOffset..n.End];
            }
        }
        Rec(g.Root, "", null);
        return @out;
    }

    static bool SameValue(object? a, object? b) => a switch
    {
        null => b == null,
        byte[] x => b is byte[] y && x.AsSpan().SequenceEqual(y),
        _ => a.Equals(b),
    };

    /// <summary>For structural edits: reparse, then prove the ONLY differences are the intended ones.</summary>
    static void VerifySemantic(OpenSave c, byte[] newFile, HashSet<string> allowed, CutInfo cut)
    {
        SaveContainer re; GvasFile g2;
        try { re = SaveContainer.Load(newFile); g2 = new GvasFile(re.Gvas); }
        catch (Exception e) { throw new EditException($"verification failed (rebuilt file unreadable): {e.Message}", e); }
        var old = c.Gvas;
        if (g2.OpaqueCount != old.OpaqueCount - cut.OpaqueRemoved) throw new EditException("verification failed: tree shape changed (size fields inconsistent)");
        if (g2.Data.Length - g2.PropsEnd != old.Data.Length - old.PropsEnd) throw new EditException("verification failed: trailing data changed");
        if (!g2.Data.AsSpan(0, old.PropsStart).SequenceEqual(old.Data.AsSpan(0, old.PropsStart))) throw new EditException("verification failed: header changed");
        if (c.Container.IsZip)
        {
            foreach (var (k, v) in c.Container.Blobs)
                if (k != SaveContainer.MainEntry && k != "SaveGameMetaData.json"
                    && !(re.Blobs.TryGetValue(k, out var nb) && nb.AsSpan().SequenceEqual(v)))
                    throw new EditException($"verification failed: entry {k} altered");
            var txt = Encoding.Unicode.GetString(re.Blobs["SaveGameMetaData.json"]);
            foreach (var (key, size) in BlobSizes(g2))
            {
                var m = Regex.Match(txt, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)");
                if (m.Success && int.Parse(m.Groups[1].Value) != size) throw new EditException($"verification failed: metadata {key} out of sync");
            }
            var (_, per) = MedbayOps.CharacterSizes(g2);
            var (_, metaPer) = re.ReadCharacterSizes();
            foreach (var (gid, size) in per)
                if (metaPer.TryGetValue(gid, out var ms) && ms != null && ms != size)
                    throw new EditException("verification failed: a character's recorded size is out of sync");
        }
        if (cut.Revived.Count > 0)
        {
            var gone = cut.Revived.ToHashSet();
            if (!ReviveOps.DeadGuids(g2).ToHashSet().SetEquals(ReviveOps.DeadGuids(old).Where(x => !gone.Contains(x))))
                throw new EditException("verification failed: the fallen list is not what was intended");
            if (!ReviveOps.DiedMap(g2).Keys.ToHashSet().SetEquals(ReviveOps.DiedMap(old).Keys.Where(x => !gone.Contains(x))))
                throw new EditException("verification failed: the death records are not what was intended");
            var ro = ReviveOps.RosterOrder(g2); var ro0 = ReviveOps.RosterOrder(old);
            if (!ro.Take(ro0.Count).SequenceEqual(ro0) || !ro.Skip(ro0.Count).ToHashSet().SetEquals(gone))
                throw new EditException("verification failed: the roster is not what was intended");
            var recBefore = ReviveOps.RecruitedMap(old); var recAfter = ReviveOps.RecruitedMap(g2);
            var rest = recAfter.Where(kv => !gone.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (rest.Count != recBefore.Count || rest.Any(kv => !recBefore.TryGetValue(kv.Key, out var v) || v != kv.Value) || !gone.IsSubsetOf(recAfter.Keys))
                throw new EditException("verification failed: the recruited turns are not what was intended");
            foreach (var gid in gone)
            {
                var eff = MedbayOps.EffectsNode(g2, gid);
                var names = (eff?.Children ?? new List<GvasNode>()).Select(e => MedbayOps.EffectName(g2, e));
                if (names.Any(n => ReviveOps.DeadEffects.Any(d => n.StartsWith(d, StringComparison.Ordinal))))
                    throw new EditException("verification failed: a death effect is still present");
            }
        }
        foreach (var (guid, expected) in cut.Focus)
        {
            var have = FocusOps.Records(g2, guid);
            foreach (var (tag, recs) in expected)
                if (!have.TryGetValue(tag, out var h) || h.Count != recs.Count || !h.Zip(recs).All(p => p.First.AsSpan().SequenceEqual(p.Second)))
                    throw new EditException("verification failed: the focus tree tiers are not what was intended");
        }
        if (cut.Roster != null && !RosterOps.Order(g2).SequenceEqual(cut.Roster)) throw new EditException("verification failed: roster order is not what was intended");
        if (cut.FactTags != null && !CoilOps.Tags(g2).SequenceEqual(cut.FactTags)) throw new EditException("verification failed: fact tags are not what was intended");

        var a = Flatten(old); var b = Flatten(g2);
        string? stray = null;
        foreach (var k in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(k, out var av); b.TryGetValue(k, out var bv);
            if (SameValue(av, bv)) continue;                      // a missing key reads as null, as before
            if (allowed.Contains(k) || allowed.Contains(PathKey.Parent(k)) || cut.Prefixes.Any(p => PathKey.HasPrefix(k, p))) continue;
            if (stray == null || string.CompareOrdinal(k, stray) < 0) stray = k;
        }
        if (stray != null)
            throw new EditException("verification failed: unexpected change at " + string.Join("/", stray.Split(PathKey.Sep).TakeLast(6)));
    }

    /// <summary>For scalar-only edits: same size, only the edited bytes differ, every edited value reads back.</summary>
    static void Verify(OpenSave c, byte[] newFile, byte[] newGvas, List<(int Off, int Size, string Type, double Value)> edited)
    {
        SaveContainer re; GvasFile g2;
        try { re = SaveContainer.Load(newFile); g2 = new GvasFile(re.Gvas); }
        catch (Exception e) { throw new EditException($"verification failed (rebuilt file unreadable): {e.Message}", e); }
        var old = c.Container.Gvas;
        if (newGvas.Length != old.Length) throw new EditException("verification failed: payload size changed");
        if (c.Container.IsZip)
        {
            foreach (var (k, v) in c.Container.Blobs)
                if (k != SaveContainer.MainEntry && !(re.Blobs.TryGetValue(k, out var nb) && nb.AsSpan().SequenceEqual(v)))
                    throw new EditException($"verification failed: entry {k} altered");
            if (!re.Infos.Select(i => i.Name).SequenceEqual(c.Container.Infos.Select(i => i.Name)))
                throw new EditException("verification failed: entry list changed");
        }
        var allowedBytes = new bool[old.Length];
        foreach (var (off, size, _, _) in edited) for (int i = off; i < off + size; i++) allowedBytes[i] = true;
        for (int i = 0; i < old.Length; i++)
            if (old[i] != newGvas[i] && !allowedBytes[i]) throw new EditException("verification failed: unexpected bytes changed");
        var nodes = SaveModel.ScalarNodes(g2);
        foreach (var (off, _, type, val) in edited)
        {
            if (!nodes.TryGetValue($"o{off}", out var n)) throw new EditException("verification failed: edited field not found after rebuild");
            double got = g2.GetDouble(n);
            bool ok = type is "FloatProperty" or "DoubleProperty"
                ? Math.Abs(got - val) <= 1e-6 * Math.Max(1.0, Math.Abs(val)) : got == val;
            if (!ok) throw new EditException($"verification failed: {got} != {val}");
        }
        if (g2.OpaqueCount != c.Gvas.OpaqueCount) throw new EditException("verification failed: tree shape changed");
    }

    // -- restore -----------------------------------------------------------
    public (string Restored, string PreviousSavedAs) Restore(string backupFile, bool force = false)
    {
        var c = Current ?? throw new EditException("no save open");
        if (Path.GetFileName(backupFile) != backupFile || !backupFile.EndsWith(".sav", StringComparison.Ordinal)) throw new EditException("invalid backup name");
        var src = Path.Combine(BackupDir(c.Name), backupFile);
        if (!File.Exists(src)) throw new EditException("backup not found");
        if (!force && _gameRunning() != null) throw new EditException("The game appears to be running. Close it first (or tick the override).");
        var data = File.ReadAllBytes(src);
        try { _ = new GvasFile(SaveContainer.Load(data).Gvas); }
        catch (Exception e) { throw new EditException($"backup is not a valid save: {e.Message}", e); }
        var snap = Snapshot(c.Name, c.Path);               // keep what we are replacing
        AtomicReplace(c.Path, data);
        Open(c.DirId, c.Name);
        return (backupFile, Path.GetFileName(snap));
    }

    // -- raw tree viewer ---------------------------------------------------
    public List<TreeRow> Tree(string? nodeId)
    {
        var c = Current ?? throw new EditException("no save open");
        IReadOnlyList<GvasNode> kids;
        if (string.IsNullOrEmpty(nodeId)) kids = c.Gvas.Root;
        else kids = c.NodeIndex.TryGetValue(nodeId, out var n) ? n.Children ?? new List<GvasNode>() : throw new EditException("unknown node");
        return kids.Take(2000).Select(k => TreeRowOf(c.Gvas, k)).ToList();
    }

    static TreeRow TreeRowOf(GvasFile g, GvasNode n)
    {
        object? value = null; string? field = null, kind = null;
        if (GvasFile.Scalars.ContainsKey(n.TName))
        {
            value = g.Get(n); field = Naming.FieldId(n);
            kind = n.TName is "FloatProperty" or "DoubleProperty" ? "float" : "int";
        }
        else if (n.TName is "StrProperty" or "NameProperty" or "ObjectProperty" or "SoftObjectProperty" or "EnumProperty" && n.Size < 2000)
        {
            try { value = g.Get(n); } catch (Exception) { }
        }
        else if (n.TName == "BoolProperty") value = g.Get(n);
        return new TreeRow($"n{n.Start}", n.Name, n.Type.ToString(), n.Size, n.Children is { Count: > 0 }, n.Count, n.Opaque || n.Native,
                           value, field, kind);
    }
}
