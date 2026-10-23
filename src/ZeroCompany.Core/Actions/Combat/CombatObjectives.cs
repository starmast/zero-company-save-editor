using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

// Objectives and countdown timers: encounter-logic actors inside EncounterLogicActorWrappers.
public static partial class CombatOps
{
    public static readonly IReadOnlyList<string> ObjectiveStatuses = new[] { "Active", "Succeeded" };
    const string StatusPrefix = "EObjectiveStatus::";

    public static IReadOnlyList<GvasNode> LogicActors(GvasFile g) =>
        g.Child(Tactical(g), "EncounterLogicActorWrappers")?.Children ?? (IReadOnlyList<GvasNode>)Array.Empty<GvasNode>();

    static GvasNode? LogicAt(GvasFile g, int index) => index < 0 || index >= LogicActors(g).Count ? null : LogicActors(g)[index];

    static GvasNode? LogicArchive(GvasFile g, GvasNode? wrapper) => g.Child(g.Child(wrapper, "EncounterLogicActor"), "ArchiveBytes");

    public static string LogicName(GvasFile g, GvasNode wrapper) => ObjectName(g, g.Child(wrapper, "EncounterLogicActor"));

    static GvasNode? StatusNode(GvasFile g, int index) => g.Child(LogicArchive(g, LogicAt(g, index)), "ObjectiveStatus");

    /// <summary>"Active" / "Succeeded" for an objective, or null.</summary>
    public static string? ObjectiveStatus(GvasFile g, int index)
    {
        if (StatusNode(g, index) is not { } n) return null;
        var s = g.GetStr(n);
        return s.StartsWith(StatusPrefix, StringComparison.Ordinal) ? s[StatusPrefix.Length..] : s;
    }

    /// <summary>"BP_TacticalObjective_CriticalInteract_C_4" -> "Critical Interact 5".</summary>
    public static string ObjectiveTitle(string name) => NumberedTitle(name, "BP_TacticalObjective_", "BP_");

    static void BuildObjectives(GvasFile g, CombatView view)
    {
        var all = LogicActors(g);
        for (int i = 0; i < all.Count; i++)
        {
            var name = LogicName(g, all[i]);
            if (!name.Contains("Objective", StringComparison.Ordinal)) continue;
            var ab = LogicArchive(g, all[i]);
            var o = new ObjectiveView { Index = i, Name = name, Title = ObjectiveTitle(name), Status = ObjectiveStatus(g, i) };
            foreach (var t in g.Child(ab, "InteractableActors")?.Children ?? new List<GvasNode>())
            {
                if (g.Get(t) is not string path || path.Length == 0) continue;
                o.Targets.Add(Naming.Pretty(UnitSuffix.Replace(After(path, '.'), "").Replace("BP_CriticalInteract_", "")));
            }
            void Counter(string label, GvasNode? n, double lo, double hi)
            {
                if (Scalar(g, n, lo, hi) is { } r) o.Counters.Add(new CounterView { Label = label, Ref = r });
            }
            var progress = g.Child(ab, "ObjectiveProgressData");
            Counter("Points so far", g.Child(progress, "AccumulatedPointsSoFar"), 0, 999);
            var goalTop = g.Child(ab, "AccumulatedPointsGoal"); var goalProg = g.Child(progress, "AccumulatedPointsGoal");
            Counter("Points needed", goalTop ?? goalProg, 0, 999);
            if (goalTop != null) Counter("Points needed (progress copy)", goalProg, 0, 999);      // the game keeps the goal in two places once counting starts
            Counter("Interactions done", g.Child(ab, "CurrentInteractionCount"), 0, 99);
            Counter("Interactions needed", g.Child(ab, "TotalInteractionCount"), 0, 99);
            view.Objectives.Add(o);
        }
    }

    /// <summary>Countdown timers (e.g. "3 player turns until the rescue fails"): editable turns left.</summary>
    static void BuildTimers(GvasFile g, CombatView view)
    {
        var all = LogicActors(g);
        for (int i = 0; i < all.Count; i++)
        {
            var name = LogicName(g, all[i]);
            if (!name.Contains("Timer", StringComparison.Ordinal)) continue;
            var ab = LogicArchive(g, all[i]);
            var t = new TimerView
            {
                Index = i, Name = name, Title = NumberedTitle(name, "BP_"),
                Remaining = Scalar(g, g.Child(ab, "DurationRemaining"), 0, 999),
                Total = Scalar(g, g.Child(ab, "DurationToCount"), 0, 999),
                CountsOn = g.Child(ab, "DurationType") is { } dt ? g.GetStr(dt).Split("::").Last() : "",
            };
            foreach (var f in g.Child(ab, "ObjectivesToFailOnTimerFinished")?.Children ?? new List<GvasNode>())
                if (g.Get(f) is string path && path.Length > 0) t.Fails.Add(ObjectiveTitle(After(path, '.')));
            foreach (var f in g.Child(ab, "ActorsToTriggerOnTimerCompleted")?.Children ?? new List<GvasNode>())
                if (g.Get(f) is string path && path.Length > 0) t.Starts.Add(ObjectiveTitle(After(path, '.')));
            view.Timers.Add(t);
        }
    }

    /// <summary>Set an objective to Active or Succeeded. Only the status text changes (the file grows or shrinks by the difference).</summary>
    public static GvasFile SetObjectiveStatus(GvasFile g, int index, string name, string status, CutInfo cut)
    {
        if (!ObjectiveStatuses.Contains(status)) throw new GvasException($"unknown objective status '{status}'");
        var n = StatusNode(g, index);
        if (n == null || LogicName(g, LogicAt(g, index)!) != name) throw new GvasException("the objective list changed; re-open the save");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));
        return g.SetString(n, StatusPrefix + status);
    }
}
