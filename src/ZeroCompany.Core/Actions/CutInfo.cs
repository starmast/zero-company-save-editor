using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>Everything a structural action deliberately changed, so the verifier can prove nothing else did.</summary>
public sealed class CutInfo
{
    /// <summary>Path prefixes (see PathKey) under which differences are expected (removed/added elements).</summary>
    public HashSet<string> Prefixes { get; } = new();
    public int OpaqueRemoved { get; set; }
    /// <summary>Expected FactTags after a Coil removal.</summary>
    public List<string>? FactTags { get; set; }
    /// <summary>GUIDs brought back from the fallen.</summary>
    public List<string> Revived { get; } = new();
    public List<(string Guid, Dictionary<string, List<byte[]>> Expected)> Focus { get; } = new();
    /// <summary>Expected roster order after a reorder.</summary>
    public List<string>? Roster { get; set; }
    /// <summary>Combat: expected dead flag per unit index after the edit.</summary>
    /// <summary>Armory: items added as new inventory entries (item name, entry GUID).</summary>
    public List<(string Asset, string Guid)> AddedItems { get; } = new();
    /// <summary>Combat scenery: the state each changed prop (full object path) must end up in, "Pristine" or "Destroyed".</summary>
    public Dictionary<string, string> SceneryStates { get; } = new();
    /// <summary>Missions: expected enemy spawn set per available mission id (hex) after a change.</summary>
    public Dictionary<string, string> MissionSets { get; } = new();
    /// <summary>Operations: expected cycles on offer per operation id (hex) after a change.</summary>
    public Dictionary<string, int> OperationAges { get; } = new();
    public Dictionary<int, bool> UnitDead { get; } = new();
    /// <summary>Combat: expected active-effect count per unit index after the edit.</summary>
    public Dictionary<int, int> EffectCounts { get; } = new();
    /// <summary>Combat: expected team tag and friendly flag per unit index after a team change.</summary>
    public Dictionary<int, (string Team, bool Friendly)> UnitTeams { get; } = new();
    /// <summary>
    /// The save's SaveGameSpawnedActors entry (loaded on demand). The game keeps a second copy of each spawned unit's actor
    /// properties there, so a team switch has to patch it too; <see cref="SpawnedActorTeams"/> lists the actors changed
    /// (actor name -> new team) and, when not empty, the entry is written back.
    /// </summary>
    public GvasFile? SpawnedActors { get; set; }
    public Dictionary<string, string> SpawnedActorTeams { get; } = new();
    /// <summary>Combat: expected spawn group per "kind/logic/index" after a group change.</summary>
    public Dictionary<string, string> SpawnGroups { get; } = new();
    /// <summary>Combat: expected active flag per reinforcement trigger (EncounterLogicActorWrappers index).</summary>
    public Dictionary<int, bool> TriggerActive { get; } = new();
    /// <summary>Combat: expected objective status per EncounterLogicActorWrappers index.</summary>
    public Dictionary<int, string> ObjectiveStatuses { get; } = new();
    /// <summary>Combat: expected enemy spawn set after a change.</summary>
    public string? EnemySpawnSet { get; set; }
    /// <summary>Combat: expected turn structure after a change.</summary>
    public string? TurnStructure { get; set; }
}
