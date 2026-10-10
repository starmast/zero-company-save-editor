using System.Text.Json.Nodes;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Model;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Views;

namespace ZeroCompany.Core.Save;

/// <summary>User-facing failure (nothing was written).</summary>
public sealed class EditException : Exception
{
    public EditException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>One pending scalar edit. <paramref name="Link"/> false stops linked fields (e.g. total focus) following.</summary>
public sealed record FieldChange(string Id, double Value, bool Link = true);

public sealed record CoilChange(string Id, string To);

/// <summary>A structural edit, applied together with the scalar edits in one verified write.</summary>
public abstract record SaveAction
{
    public sealed record HealOperator(string Guid) : SaveAction;
    public sealed record CompleteFocusTree(string Guid) : SaveAction;
    public sealed record ReviveOperator(string Guid) : SaveAction;
    public sealed record ReorderRoster(IReadOnlyList<string> Order) : SaveAction;
    public sealed record RemoveCoilUpgrades(IReadOnlyList<CoilChange> Changes) : SaveAction;
    public sealed record StartUpgrade(string Id, string Name) : SaveAction;
    public sealed record ExpediteUpgrade(string Id, string Name) : SaveAction;
    public sealed record StartExpediteUpgrade(string Id, string Name) : SaveAction;
}

public sealed record ApplyResult(string Written, bool Copy, int Count, string? Backup);

public sealed record BackupInfo(string File, long Size, DateTime MTimeUtc, bool Original);

public sealed record SaveEntry(string Dir, string Name, SaveContainer.Summary Info);

/// <summary>A node of the raw property tree, for the Advanced screen.</summary>
public sealed record TreeRow(string Id, string Name, string Type, int Size, bool Expandable, int? Count, bool Opaque,
                             object? Value, string? Field, string? Kind);

/// <summary>Everything loaded for the currently open save.</summary>
public sealed class OpenSave
{
    public required string DirId { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required SaveContainer Container { get; init; }
    public required GvasFile Gvas { get; init; }
    public required SaveModel Model { get; init; }
    public required Dictionary<string, GvasNode> Scalars { get; init; }
    public required string DiskHash { get; init; }
    public required PortraitStore Portraits { get; init; }
    public required JsonObject? Info { get; init; }
    public UpgradeList Upgrades { get; init; } = new();
    public SaveView View { get; init; } = new();

    Dictionary<string, GvasNode>? _index;

    /// <summary>Node lookup by tree-row id (see <see cref="TreeId"/>), built on first use.</summary>
    public Dictionary<string, GvasNode> NodeIndex => _index ??= Gvas.Walk().ToDictionary(TreeId);

    /// <summary>
    /// A parent and its first child can start at the same byte, so the offset alone is not unique;
    /// the nesting depth makes it so.
    /// </summary>
    public static string TreeId(GvasNode n)
    {
        int depth = 0;
        for (var p = n.Parent; p != null; p = p.Parent) depth++;
        return $"n{n.Start}-{depth}";
    }

    public int ParsedNodes => Gvas.Walk().Count();
    public int OpaqueNodes => Gvas.OpaqueCount;
}
