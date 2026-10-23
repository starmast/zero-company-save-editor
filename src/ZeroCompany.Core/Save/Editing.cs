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
    /// <summary>Give every operator <c>Total</c> gear slots of a type ("mod" or "utility").</summary>
    public sealed record SetGearSlots(string Type, int Total) : SaveAction;
    /// <summary>Make a Black Market quality table (kind "mods" or "utility", tier 1 to 3) roll <c>Count</c> items per refresh.</summary>
    public sealed record SetMarketTable(string Kind, int Tier, int Count) : SaveAction;
    /// <summary>Give an operator an ability another operator in the save holds, at the given focus-tree level.</summary>
    public sealed record GrantAbility(string Guid, string Tag, int Level) : SaveAction;
    /// <summary>Change one of the campaign's options (setting key such as <c>US_Gameplay_EnemyCrits</c>) to the choice with fact tag <c>Tag</c>.</summary>
    public sealed record SetCampaignOption(string Key, string Tag) : SaveAction;
    public sealed record ReorderRoster(IReadOnlyList<string> Order) : SaveAction;
    public sealed record RemoveCoilUpgrades(IReadOnlyList<CoilChange> Changes) : SaveAction;
    /// <summary>Give the squad <c>Copies</c> new copies of an item it may never have had (a utility item or weapon mod, by asset name).</summary>
    public sealed record AddInventoryItem(string Asset, int Copies) : SaveAction;
    /// <summary>Set how many cycles a Holotable operation has been on offer (0 = fresh), which decides how long it has left.</summary>
    public sealed record SetOperationAge(string Operation, int Age) : SaveAction;
    /// <summary>Set the cycle an available Den mission was added on; a later one gives it more time before it expires.</summary>
    public sealed record SetMissionTurnAdded(string Mission, int Turn) : SaveAction;
    /// <summary>Change which Coil upgrade an available crisis mission is about (the full object path of a crisis effect).</summary>
    public sealed record SetMissionCrisisEffect(string Mission, string Effect) : SaveAction;
    /// <summary>Point an available Den mission (by its id) at another enemy spawn set: the faction and threat tier it will spawn.</summary>
    public sealed record SetMissionSpawnSet(string Mission, string Set) : SaveAction;
    public sealed record StartUpgrade(string Id, string Name) : SaveAction;
    public sealed record ExpediteUpgrade(string Id, string Name) : SaveAction;
    public sealed record StartExpediteUpgrade(string Id, string Name) : SaveAction;
    /// <summary>Edits that only apply to a combat save; run by <c>SaveService.RunCombatAction</c>.</summary>
    public abstract record CombatAction : SaveAction;
    /// <summary>Set a unit's dead flag (unit = index into the tactical unit list).</summary>
    public sealed record SetUnitDead(int Unit, bool Dead) : CombatAction;
    /// <summary>Remove one active effect. <c>Def</c> guards against a stale index.</summary>
    public sealed record RemoveUnitEffect(int Unit, int Index, string Def) : CombatAction;
    /// <summary>Copy another unit's active effect onto this unit.</summary>
    public sealed record CopyUnitEffect(int Unit, int FromUnit, int FromIndex, string Def) : CombatAction;
    /// <summary>Move a unit to another team ("Player", "Enemy", "Enemy2", "Civilian"); see <c>CombatOps.SetTeam</c> for everything it changes.</summary>
    public sealed record SetUnitTeam(int Unit, string Team) : CombatAction;
    /// <summary>Set the spawn group of a queued reinforcement wave (<c>Kind</c> "wave") or a wave definition ("config").</summary>
    public sealed record SetSpawnGroup(string Kind, int Logic, int Index, string Group) : CombatAction;
    /// <summary>Bring destroyed scenery props (by full object path) back: their actor record goes from Destroyed to Pristine and their health to maximum.</summary>
    public sealed record RestoreScenery(IReadOnlyList<string> Actors) : CombatAction;
    /// <summary>Wreck intact or damaged scenery props (by full object path): their actor record goes to Destroyed (and health to 0 where a health entry exists).</summary>
    public sealed record DestroyScenery(IReadOnlyList<string> Actors) : CombatAction;
    /// <summary>Swap the mission's enemy threat tier (EnemySelectedSpawnSet) for another known tier, of its own or (experimental) another faction.</summary>
    public sealed record SetEnemySpawnSet(string Set) : CombatAction;
    /// <summary>Experimental: standard or three-way turn structure (Enemy2 needs the three-way one to stay a separate team).</summary>
    public sealed record SetTurnStructure(string Structure) : CombatAction;
    /// <summary>Switch a reinforcement trigger on or off. <c>Name</c> guards against a stale index.</summary>
    public sealed record SetTriggerActive(int Logic, string Name, bool Active) : CombatAction;
    /// <summary>Set an objective to "Active" or "Succeeded". <c>Name</c> guards against a stale index.</summary>
    public sealed record SetObjectiveStatus(int Index, string Name, string Status) : CombatAction;
    /// <summary>Bring a roster operator (GUID, hex) into the fight: the game spawns them with the squad when the save loads.</summary>
    public sealed record AddSquadMember(string Guid) : CombatAction;
    /// <summary>Set the round the fight is on (the tactical game state's saved round counter).</summary>
    public sealed record SetCurrentRound(int Round) : CombatAction;
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
    /// <summary>The in-combat state, or null when this is not a combat save.</summary>
    public CombatView? Combat { get; init; }

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
