using System.Buffers.Binary;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Actions;

// The round the fight is on. The tactical game state keeps it in its own saved block (TacticalGameStateWrapper/ArchiveBytes): an int32
// byte count (the block's size minus the count itself), then the round as an int32, then strings (the phase tags and references to the
// combat area). A live test showed the game reads it back on load: a copy with the round set to 8 loaded as round 8, and a manual save
// made at round 3 loaded as round 3. It is one int32 in place, so changing it never changes a size.
public static partial class CombatOps
{
    /// <summary>The largest round the editor will write.</summary>
    public const int MaxRound = 999;

    static GvasNode? GameStateBlock(GvasFile g) => g.Child(g.Child(Tactical(g), "TacticalGameStateWrapper"), "ArchiveBytes");

    /// <summary>True when the block is in the known shape: a byte count that matches its size, then at least the round.</summary>
    static bool IsGameStateBlock(GvasFile g, GvasNode n) =>
        n.TName == "ArrayProperty" && n.Size >= 8 && n.ValueOffset + n.Size <= g.Data.Length
        && BinaryPrimitives.ReadInt32LittleEndian(g.Data.AsSpan(n.ValueOffset)) == n.Size - 4;

    /// <summary>The round the fight is on, or null when the save has no game state block or it is not in the known shape.</summary>
    public static int? CurrentRound(GvasFile g) =>
        GameStateBlock(g) is { } n && IsGameStateBlock(g, n) ? BinaryPrimitives.ReadInt32LittleEndian(g.Data.AsSpan(n.ValueOffset + 4)) : null;

    /// <summary>Set the round (1 to <see cref="MaxRound"/>). Only that int32 changes.</summary>
    public static GvasFile SetCurrentRound(GvasFile g, int round, CutInfo cut)
    {
        if (round < 1 || round > MaxRound) throw new GvasException($"the round must be between 1 and {MaxRound}");
        if (GameStateBlock(g) is not { } n || !IsGameStateBlock(g, n)) throw new GvasException("this save does not keep the round in the form this editor knows");
        var bytes = g.Data.AsSpan(n.ValueOffset, n.Size).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), round);
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(n)));
        cut.CurrentRound = round;
        return g.ReplaceValue(n, bytes);
    }
}
