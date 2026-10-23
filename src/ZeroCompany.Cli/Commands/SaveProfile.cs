using System.Text;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Views;
using ZeroCompany.Sdk;

namespace ZeroCompany.Cli;

/// <summary>`zc save profile`: one screen that says what a save is and what is in it.</summary>
static class SaveProfile
{
    public static void Run(SaveDocument s, bool verbose)
    {
        var info = s.Container.SaveInfo();
        string I(string k) => info?[k]?.ToString() ?? "";
        Console.WriteLine($"{Path.GetFileName(s.Path)}  ({new FileInfo(s.Path).Length / 1024:N0} KB, {File.GetLastWriteTime(s.Path):yyyy-MM-dd HH:mm})");
        Console.WriteLine($"  comment      {I("comment")}");
        Console.WriteLine($"  mode         {I("gameMode")} / {I("saveGameType")}  autosave={I("autoSaveType")}");
        Console.WriteLine($"  world        {I("worldName")}  mission={Path.GetFileName(I("missionDataPath"))}");
        Console.WriteLine($"  campaign     strategy turn {I("strategyTurn")}, difficulty {I("currentDifficultyLevel")}, permadeath {CampaignOptionOps.Permadeath(info)?.ToString().ToLowerInvariant() ?? "?"}, region {Last(info?["regionTag"]?["tagName"])} / {Last(info?["mapLocationTag"]?["tagName"])}");
        if (CampaignOptionOps.Read(s.Gvas) is { } opts)
            Console.WriteLine($"  options      {string.Join(", ", CampaignOptionOps.Describe(opts).Select(o => $"{o.Setting.Label} {o.Label}"))}");
        Console.WriteLine($"  entries      {string.Join(", ", s.Container.Blobs.Select(b => $"{b.Key} ({b.Value.Length / 1024:N0} KB)"))}");
        Console.WriteLine($"  gvas roots   {string.Join(", ", s.Gvas.Root.Select(r => $"{r.Name} ({r.Size / 1024:N0} KB)"))}");

        if (!CombatOps.IsCombatSave(s.Gvas)) { Console.WriteLine("  (not a combat save)"); return; }
        var cb = CombatOps.Build(s.Gvas, s.Comment);

        Console.WriteLine();
        Console.WriteLine("COMBAT");
        Console.WriteLine($"  round           {(cb.Round?.ToString() ?? "?")}");
        Console.WriteLine($"  turn structure  {CombatOps.TurnStructureOf(s.Gvas) switch { null => "no mission actor", CombatOps.TurnThreeWay => "three-way battle", CombatOps.TurnStandard => "standard", var other => other }}");
        Console.WriteLine($"  enemy spawn set {(cb.SpawnSet == null ? "-" : CombatOps.SpawnSetLabel(cb.SpawnSet.Current))}");

        Console.WriteLine();
        Console.WriteLine("TEAMS");
        foreach (var g in cb.Units.GroupBy(u => u.Team).OrderBy(g => g.Key))
        {
            var alive = g.Where(u => !u.Dead).ToList();
            Console.WriteLine($"  {g.Key,-9} {alive.Count} alive, {g.Count() - alive.Count} dead   " +
                string.Join(", ", alive.GroupBy(u => Group(u.Actor)).Select(x => $"{x.Key} x{x.Count()}")));
        }

        Console.WriteLine();
        Console.WriteLine("LIVING UNITS");
        Console.WriteLine($"  {"#",3} {"team",-9} {"hp",-9} {"ap",-4} {"fx",-3} {"pos",-16} name");
        foreach (var u in cb.Units.Where(u => !u.Dead).OrderBy(u => u.Team).ThenBy(u => u.Index))
            Console.WriteLine($"  {u.Index,3} {u.Team,-9} {Fmt(u.Attr("Health")?.Value)}/{Fmt(u.Attr("MaxHealth")?.Value),-5} {Fmt(u.Attr("ActionPoints")?.Value),-4} {u.Effects.Count,-3} {Pos(u),-16} {u.Name}{(u.PlayerBehavior ? "  [player behaviour]" : "")}");
        if (verbose)
            foreach (var u in cb.Units.Where(u => !u.Dead))
                Console.WriteLine($"    {u.Index,3} {u.Name}: effects {string.Join(", ", u.Effects.Select(e => e.Title + (e.Stacks != null && e.Stacks.Value > 1 ? $" x{e.Stacks.Value:0}" : "")))}");

        Console.WriteLine();
        Console.WriteLine("OBJECTIVES");
        foreach (var o in cb.Objectives)
            Console.WriteLine($"  [{o.Status ?? "-",-9}] {o.Title}{(o.Counters.Count > 0 ? "  " + string.Join(", ", o.Counters.Select(c => $"{c.Label}={c.Ref.Value:0}")) : "")}");
        foreach (var t in cb.Timers)
            Console.WriteLine($"  timer {t.Title}: {(t.Remaining == null ? "finished" : $"{t.Remaining.Value:0} of {t.Total?.Value:0} turns left")}  fails: {string.Join(", ", t.Fails)}");

        var rv = cb.Reinforcements;
        Console.WriteLine();
        Console.WriteLine($"REINFORCEMENTS  ({rv.Waves.Count} queued, {rv.Configs.Count} definitions, {rv.Triggers.Count} triggers)");
        foreach (var w in rv.Waves) Console.WriteLine($"  wave {w.Index + 1}: round {w.Round?.Value:0}  {CombatOps.GroupLabel(w.Group)}  at {CombatOps.SpawnerLabel(w.EntryPoint)}");
        foreach (var c in rv.Configs)
            Console.WriteLine($"  def  {c.Index + 1}: {CombatOps.GroupLabel(c.Group)}  at {CombatOps.SpawnerLabel(c.EntryPoint)}  auto={c.Automatic}  delay={c.Delay?.Value:0} spawn={c.TimesToSpawn?.Value:0} done={c.TimesTriggered?.Value:0}");
        foreach (var t in rv.Triggers) Console.WriteLine($"  trigger {t.Title}: {(t.HasFlag ? (t.Active ? "on" : "off") : "not fired")}");

        Console.WriteLine();
        Console.WriteLine($"SCENERY  {cb.Hazards.Count} fire zones, {cb.Destructibles.Count} destructibles ({cb.Destructibles.Count(d => d.Health.Value <= 0)} destroyed, {cb.Destructibles.Count(d => d.Health.Value > 0 && d.Health.Value < d.MaxHealth.Value)} damaged)");
        foreach (var h in cb.Hazards) Console.WriteLine($"  {h.Title}: {h.TurnsLeft.Value:0} turns left, radius {h.Radius:0}");
    }

    static string Last(System.Text.Json.Nodes.JsonNode? n) => n?.ToString().Split('.').Last() ?? "?";
    static string Fmt(double? v) => v == null ? "?" : v.Value.ToString("0.#");
    static string Pos(CombatUnitView u) => u.X == null || u.Y == null ? "-" : $"{u.X.Value / 100:0},{u.Y.Value / 100:0}";

    /// <summary>"Char_Enemy_Imperial_Trooper_C_3" -> "Imperial Trooper".</summary>
    static string Group(string actor)
    {
        var name = CombatOps.DisplayName(actor);
        var sb = new StringBuilder(name);
        while (sb.Length > 0 && (char.IsDigit(sb[^1]) || sb[^1] == ' ')) sb.Length--;
        return sb.ToString();
    }
}
