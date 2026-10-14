using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Actions;

/// <summary>One operation on the Holotable (an active field-task recipe in StrategyData/ActiveRecipes).</summary>
public sealed record OperationEntry(string Id, string Recipe, string Status, long AvailableTurns, long TurnAdded, string Location)
{
    public bool Available => Status == "Available";
}

/// <summary>
/// The operations on offer. The save counts how many cycles each has been on offer (AvailableTurns); the game's recipe says how many
/// it lasts (Expiration), so "expires in" is the difference. Lowering the count keeps an operation from expiring.
/// </summary>
public static class OperationOps
{
    /// <summary>The most cycles an operation can be saved as having been on offer (far above any real Expiration).</summary>
    public const int MaxAge = 99;

    static GvasNode? Map(GvasFile g) => g.StrategyDataOrNull() is { } sd ? g.Child(sd, "ActiveRecipes") : null;

    static IEnumerable<(GvasNode Entry, GvasNode Value)> Raw(GvasFile g)
    {
        foreach (var e in Map(g)?.Children ?? new List<GvasNode>())
            if (e.Children is { Count: > 1 } && e.Children[1] is { } v && g.Child(v, "RecipeClass") is { } rc && g.GetStr(rc).Contains("/Recipes/FieldTasks_", StringComparison.Ordinal))
                yield return (e, v);
    }

    public static List<OperationEntry> Active(GvasFile g)
    {
        var list = new List<OperationEntry>();
        foreach (var (e, v) in Raw(g))
        {
            string status = g.Child(v, "Status") is { } st ? g.GetStr(st) : "";
            string loc = g.Child(v, "DefaultRecipeContext") is { } ctx && g.Child(ctx, "LocationTag") is { } lt && g.Child(lt, "TagName") is { } tn ? g.GetStr(tn) : "";
            list.Add(new OperationEntry(ArmoryOps.StructGuid(g, e.Children![0]), RecipeName(g.GetStr(g.Child(v, "RecipeClass")!)),
                status[(status.LastIndexOf(':') + 1)..], g.Child(v, "AvailableTurns") is { } a ? g.GetInt(a) : 0, g.Child(v, "TurnAdded") is { } t ? g.GetInt(t) : -1,
                loc[(loc.LastIndexOf('.') + 1)..]));
        }
        return list;
    }

    /// <summary>"/Game/.../OT_0019.OT_0019_C" -> "OT_0019".</summary>
    static string RecipeName(string classPath) => Naming.AssetName(classPath);

    /// <summary>Set how many cycles an operation has been on offer (0 = just added, so it has all its time again).</summary>
    public static GvasFile SetAge(GvasFile g, string id, int age, CutInfo cut)
    {
        if (age < 0 || age > MaxAge) throw new GvasException($"cycles on offer must be 0 to {MaxAge}");
        var hit = Raw(g).Where(x => ArmoryOps.StructGuid(g, x.Entry.Children![0]) == id).Select(x => ((GvasNode, GvasNode)?)x).FirstOrDefault()
            ?? throw new GvasException("that operation is not on offer any more");
        var v = hit.Item2;
        if (!Active(g).First(o => o.Id == id).Available) throw new GvasException("only an operation that is still on offer can be changed");
        var node = g.Child(v, "AvailableTurns") ?? throw new GvasException("that operation has no cycle count");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(node)));
        cut.OperationAges[id] = age;
        g.Set(node, age);
        return g;
    }
}
