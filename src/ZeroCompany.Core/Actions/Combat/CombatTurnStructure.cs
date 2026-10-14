using System.Text;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Actions;

// The mission actor's TurnStructure: how many sides take turns. Missions with the default two-sided structure do not save the
// property at all; the three-way structure (a second hostile team, Enemy2) is saved as a class reference.
public static partial class CombatOps
{
    public const string TurnStandard = "Standard";
    public const string TurnThreeWay = "ThreeWay";
    const string ThreeWayPath = "/Game/Game/Core/Blueprints/Objects/TurnStructures/BP_TurnStructure_ThreeWayBattle.BP_TurnStructure_ThreeWayBattle_C";

    static GvasNode? MissionArchive(GvasFile g) =>
        LogicActors(g).Where(w => LogicName(g, w).StartsWith("BP_MissionActor", StringComparison.Ordinal))
            .Select(w => LogicArchive(g, w)).FirstOrDefault(a => a != null);

    static GvasNode? TurnNode(GvasFile g) => g.Child(MissionArchive(g), "TurnStructure");

    /// <summary>"Standard", "ThreeWay", or the asset name of some other structure; null when the save has no mission actor.</summary>
    public static string? TurnStructureOf(GvasFile g)
    {
        if (MissionArchive(g) == null) return null;
        if (TurnNode(g) is not { } n) return TurnStandard;
        var p = g.GetStr(n);
        if (p.Length == 0) return "None";
        return p == ThreeWayPath ? TurnThreeWay : Naming.AssetName(p);
    }

    /// <summary>True for a three-way battle: the mission saves the three-way turn structure, or a unit is already on Enemy2. Only there does Enemy2 stay a separate team.</summary>
    public static bool IsThreeWay(GvasFile g) =>
        TurnStructureOf(g) == TurnThreeWay || Enumerable.Range(0, Units(g).Count).Any(i => TeamOf(g, i) == "Enemy2");

    static void BuildTurnStructure(GvasFile g, CombatView view)
    {
        view.ThreeWay = IsThreeWay(g);
        if (TurnStructureOf(g) is not { } cur || cur != TurnThreeWay) return;          // offered only where three-way battles are a thing
        var tv = new TurnStructureView { Current = cur };
        tv.Choices.Add((TurnStandard, "Standard (two sides)"));
        tv.Choices.Add((TurnThreeWay, "Three-way battle"));
        if (cur is not (TurnStandard or TurnThreeWay)) tv.Choices.Add((cur, Naming.Pretty(cur)));
        view.TurnStructure = tv;
    }

    static byte[] FString(string s)
    {
        var raw = Encoding.ASCII.GetBytes(s + "\0");
        var b = new byte[4 + raw.Length];
        BitConverter.GetBytes(raw.Length).CopyTo(b, 0);
        raw.CopyTo(b, 4);
        return b;
    }

    /// <summary>A whole ObjectProperty (tag and value) in the layout the game writes.</summary>
    static byte[] ObjectPropertyBytes(string name, string path)
    {
        var value = FString(path);
        using var ms = new MemoryStream();
        ms.Write(FString(name));
        ms.Write(FString("ObjectProperty"));
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(value.Length));
        ms.WriteByte(0);
        ms.Write(value);
        return ms.ToArray();
    }

    /// <summary>Switch the mission to the standard or the three-way turn structure.</summary>
    public static GvasFile SetTurnStructure(GvasFile g, string structure, CutInfo cut)
    {
        if (structure is not (TurnStandard or TurnThreeWay)) throw new GvasException("unknown turn structure");
        var archive = MissionArchive(g) ?? throw new GvasException("this save has no mission actor");
        var node = TurnNode(g);
        // only the TurnStructure property may differ: the verifier keys properties by name, so adding or removing it moves no sibling
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(archive), "TurnStructure"));
        cut.TurnStructure = structure;
        if (structure == TurnStandard)
            return node == null ? g : g.RemoveFromArchive(archive, node.Start, node.End);
        if (node != null) return g.GetStr(node) == ThreeWayPath ? g : g.SetString(node, ThreeWayPath);
        int at = g.Child(archive, "EnemySelectedSpawnSet")?.Start ?? archive.Children!.Last().End;
        return g.InsertInArchive(archive, at, ObjectPropertyBytes("TurnStructure", ThreeWayPath));
    }
}
