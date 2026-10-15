using System.Buffers.Binary;
using System.Text;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

// Reinforcements: queued waves and wave definitions on the encounter actor, and the trigger actors that start waves.
public static partial class CombatOps
{
    static string? Str(GvasFile g, GvasNode? n) => n == null ? null : g.GetStr(n);

    static GvasNode? WaveArchive(GvasFile g, GvasNode wave, string which) => g.Child(g.Child(wave, which), "ArchiveBytes");

    /// <summary>"/Game/.../SG_TKT_Trooper_x2.SG_TKT_Trooper_x2" -> "TKT Trooper x2".</summary>
    public static string GroupLabel(string path)
    {
        var n = Naming.AssetName(path);
        if (n.StartsWith("SG_", StringComparison.Ordinal)) n = n[3..];
        return n.Replace('_', ' ');
    }

    /// <summary>"PersistentLevel.BP_ActorSpawner_C_3" -> "Spawner 4".</summary>
    public static string SpawnerLabel(string path) => NumberedTitle(After(path, '.'), "BP_Actor");

    static void BuildReinforcements(GvasFile g, CombatView view)
    {
        var rv = view.Reinforcements;
        var all = LogicActors(g);
        for (int i = 0; i < all.Count; i++)
        {
            var name = LogicName(g, all[i]);
            var ab = LogicArchive(g, all[i]);
            if (name.StartsWith("BP_TriggerReinforcements", StringComparison.Ordinal))
            {
                var flag = g.Child(ab, "bIsActive");
                var tag = Str(g, g.Child(g.Child(ab, "ReinforcementWaveTag"), "TagName")) ?? "";
                rv.Triggers.Add(new TriggerView
                {
                    Logic = i, Name = name, HasFlag = flag != null, Active = g.GetBool(flag),
                    Title = $"{(tag.Length > 0 ? Naming.Pretty(After(tag, '.')) : "Trigger")} ({NumberedTitle(name, "BP_")})",
                });
            }
            if (!name.StartsWith("BP_EncounterActor", StringComparison.Ordinal)) continue;

            var cfgs = g.Child(ab, "ReinforcementWaveSpawnerConfigs")?.Children ?? new List<GvasNode>();
            for (int k = 0; k < cfgs.Count; k++)
            {
                var c = cfgs[k];
                rv.Configs.Add(new WaveConfigView
                {
                    Logic = i, Index = k, Group = Str(g, g.Child(c, "SelectedSpawnGroup")) ?? "", EntryPoint = Str(g, g.Child(c, "ManualEntryPoint")) ?? "",
                    Automatic = g.GetBool(g.Child(c, "bIsAutomaticallyTriggered")),
                    Delay = Scalar(g, g.Child(c, "RoundDelayBeforeSpawn"), -1, 999),
                    TimesToSpawn = Scalar(g, g.Child(c, "NumTimesToSpawn"), -1, 999),
                    TimesTriggered = Scalar(g, g.Child(c, "NumTimesTriggered"), 0, 999),
                });
            }
            var q = g.Child(all[i], "QueuedReinforcementWaves")?.Children ?? new List<GvasNode>();
            for (int k = 0; k < q.Count; k++)
            {
                var sp = WaveArchive(g, q[k], "SpawnReinforcements");
                rv.Waves.Add(new WaveView
                {
                    Logic = i, Index = k, Group = Str(g, g.Child(sp, "SelectedSpawnGroup")) ?? "", EntryPoint = Str(g, g.Child(sp, "ReinforcementEntryPoint")) ?? "",
                    ConfigIndex = g.Child(sp, "WaveSpawnerConfigIndex") is { } ci ? (int)g.GetInt(ci) : null,
                    Round = Scalar(g, g.Child(sp, "RoundToSpawn"), 0, 9999),
                });
            }
        }
        rv.Groups.AddRange(SpawnGroups(g));
    }

    /// <summary>All spawn groups of this save's reinforcement definitions and queued waves; only these may be chosen.</summary>
    public static IReadOnlyList<string> SpawnGroups(GvasFile g)
    {
        var groups = new SortedSet<string>(StringComparer.Ordinal);
        void Add(string? s) { if (!string.IsNullOrEmpty(s)) groups.Add(s); }
        foreach (var w in LogicActors(g))
        {
            foreach (var c in g.Child(LogicArchive(g, w), "ReinforcementWaveSpawnerConfigs")?.Children ?? new List<GvasNode>())
            {
                Add(Str(g, g.Child(c, "SelectedSpawnGroup")));
                Add(Str(g, g.Child(c, "DefaultSpawnGroup")));
            }
            foreach (var q in g.Child(w, "QueuedReinforcementWaves")?.Children ?? new List<GvasNode>())
                Add(Str(g, g.Child(WaveArchive(g, q, "SpawnReinforcements"), "SelectedSpawnGroup")));
        }
        return groups.ToList();
    }

    /// <summary>The group nodes a "kind/logic/index" refers to (a queued wave has two: its announcement and its spawn; a definition has one).</summary>
    static List<GvasNode> GroupNodes(GvasFile g, string kind, int logic, int index)
    {
        var @out = new List<GvasNode>();
        if (LogicAt(g, logic) is not { } w) return @out;
        if (kind == "config")
        {
            var c = g.Child(LogicArchive(g, w), "ReinforcementWaveSpawnerConfigs")?.Children;
            if (c != null && index >= 0 && index < c.Count && g.Child(c[index], "SelectedSpawnGroup") is { } n) @out.Add(n);
        }
        else if (kind == "wave")
        {
            var q = g.Child(w, "QueuedReinforcementWaves")?.Children;
            if (q != null && index >= 0 && index < q.Count)
                foreach (var which in new[] { "AnnounceReinforcements", "SpawnReinforcements" })
                    if (g.Child(WaveArchive(g, q[index], which), "SelectedSpawnGroup") is { } n) @out.Add(n);
        }
        return @out;
    }

    /// <summary>The spawn group a "kind/logic/index" currently has, or null.</summary>
    public static string? SpawnGroupOf(GvasFile g, string kind, int logic, int index) =>
        GroupNodes(g, kind, logic, index).Select(n => g.GetStr(n)).FirstOrDefault();

    public static GvasFile SetSpawnGroup(GvasFile g, string kind, int logic, int index, string group, CutInfo cut)
    {
        if (kind is not ("wave" or "config")) throw new GvasException($"unknown reinforcement kind '{kind}'");
        if (!SpawnGroups(g).Contains(group)) throw new GvasException("that spawn group is not one this encounter uses");
        var nodes = GroupNodes(g, kind, logic, index);
        if (nodes.Count == 0) throw new GvasException("the reinforcement list changed; re-open the save");
        foreach (var n in nodes) cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));
        cut.SpawnGroups[$"{kind}/{logic}/{index}"] = group;
        for (int which = 0; which < nodes.Count; which++)       // SetString returns a new file, so the next node is looked up again
        {
            var n = GroupNodes(g, kind, logic, index)[which];
            if (g.GetStr(n) != group) g = g.SetString(n, group);
        }
        // The game restores a definition's group from a second, separate list when it loads (tested in the game: with only the
        // definition changed, the old group came back), so that entry has to change too.
        return kind == "config" ? SetSavedGroup(g, logic, index, group, cut) : g;
    }

    // ---- the saved copy of the definitions' groups -----------------------------------------------------------------------------
    // SaveLoadReinforcementWaveSelectedSpawnGroups is an array of soft object paths, one per wave definition, in the same order.
    // Its value is an int32 count, then per element three FStrings: the package path, the asset name and a (normally empty) sub-path.

    const string SavedGroupsProp = "SaveLoadReinforcementWaveSelectedSpawnGroups";

    sealed record SoftPath(string Package, string Asset, string Sub);

    static GvasNode? SavedGroupsNode(GvasFile g, int logic) =>
        LogicAt(g, logic) is { } w ? g.Child(LogicArchive(g, w), SavedGroupsProp) : null;

    static List<SoftPath>? ParseSoftPaths(GvasFile g, GvasNode n)
    {
        var d = g.Data; int p = n.ValueOffset; int end = n.End;
        if (n.TName != "ArrayProperty" || end - p < 4 || end > d.Length) return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p)); p += 4;
        if (count < 0 || count > 10000) return null;
        string? ReadString()
        {
            if (p + 4 > end) return null;
            int len = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p)); p += 4;
            if (len == 0) return "";
            if (len < 1 || p + len > end || d[p + len - 1] != 0) return null;     // only plain (non-UTF-16) strings are handled
            var s = Encoding.ASCII.GetString(d, p, len - 1); p += len;
            return s;
        }
        var list = new List<SoftPath>(count);
        for (int i = 0; i < count; i++)
        {
            var pkg = ReadString(); var asset = ReadString(); var sub = ReadString();
            if (pkg == null || asset == null || sub == null) return null;
            list.Add(new SoftPath(pkg, asset, sub));
        }
        return p == end ? list : null;                                             // every byte accounted for, or it is not what we think
    }

    static byte[] BuildSoftPaths(IReadOnlyList<SoftPath> list)
    {
        var o = new List<byte>();
        void Int(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); o.AddRange(b); }
        void Str(string s) { if (s.Length == 0) { Int(0); return; } Int(s.Length + 1); o.AddRange(Encoding.ASCII.GetBytes(s)); o.Add(0); }
        Int(list.Count);
        foreach (var e in list) { Str(e.Package); Str(e.Asset); Str(e.Sub); }
        return o.ToArray();
    }

    /// <summary>Whether this encounter actor keeps the saved list of definition groups.</summary>
    public static bool HasSavedSpawnGroups(GvasFile g, int logic) => SavedGroupsNode(g, logic) != null;

    /// <summary>The saved group ("/Game/.../SG_X.SG_X") of wave definition <paramref name="index"/>, or null when there is no list or it cannot be read.</summary>
    public static string? SavedSpawnGroupOf(GvasFile g, int logic, int index)
    {
        if (SavedGroupsNode(g, logic) is not { } n || ParseSoftPaths(g, n) is not { } list || index < 0 || index >= list.Count) return null;
        return list[index].Package + "." + list[index].Asset;
    }

    static GvasFile SetSavedGroup(GvasFile g, int logic, int index, string group, CutInfo cut)
    {
        if (SavedGroupsNode(g, logic) is not { } n) return g;                      // a save without the list: nothing more to change
        var list = ParseSoftPaths(g, n) ?? throw new GvasException("the saved wave group list is not in the form this editor knows");
        if (index < 0 || index >= list.Count) throw new GvasException("the saved wave group list does not match the wave definitions; re-open the save");
        int dot = group.LastIndexOf('.');
        if (dot <= 0 || dot == group.Length - 1 || group.Any(c => c > 127)) throw new GvasException("that spawn group is not a plain asset path");
        list[index] = list[index] with { Package = group[..dot], Asset = group[(dot + 1)..] };
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));
        return g.ReplaceValue(n, BuildSoftPaths(list));
    }

    static GvasNode? TriggerFlag(GvasFile g, int logic) => g.Child(LogicArchive(g, LogicAt(g, logic)), "bIsActive");

    public static bool? TriggerActiveOf(GvasFile g, int logic) => TriggerFlag(g, logic) is { } n ? g.GetBool(n) : null;

    /// <summary>Switch a reinforcement trigger on or off (only when its active flag is saved; the file does not change size).</summary>
    public static GvasFile SetTriggerActive(GvasFile g, int logic, string name, bool active, CutInfo cut)
    {
        if (LogicAt(g, logic) is not { } w || LogicName(g, w) != name || !name.StartsWith("BP_TriggerReinforcements", StringComparison.Ordinal))
            throw new GvasException("the trigger list changed; re-open the save");
        var f = TriggerFlag(g, logic) ?? throw new GvasException("this trigger has no saved active flag");
        cut.TriggerActive[logic] = active;
        g.SetBool(f, active);
        return g;
    }
}
