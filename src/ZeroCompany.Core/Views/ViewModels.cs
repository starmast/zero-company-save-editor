using System.Text.Json.Nodes;
using ZeroCompany.Core.Model;

namespace ZeroCompany.Core.Views;

// Screen-shaped data. Every editable value is a FieldRef: the existing field id plus its current value and limits, so edits
// keep flowing through the one apply pipeline. Property names map 1:1 (snake_case) onto the keys the original editor's JSON
// used, which is what the parity tests compare.

public class FieldRef
{
    public string Id { get; init; } = "";
    public double Value { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public string Kind { get; init; } = "int";

    public static FieldRef? From(Field? f) =>
        f == null ? null : new FieldRef { Id = f.Id, Value = f.Value, Min = f.Min, Max = f.Max, Kind = f.Kind };
}

public sealed class ResourceView : FieldRef
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class LevelView : FieldRef
{
    public int Offset { get; init; }
}

public sealed class HeaderView
{
    public List<ResourceView> Resources { get; init; } = new();
    public LevelView? Level { get; init; }
    public FieldRef? Xp { get; init; }
    public FieldRef? Turn { get; init; }
}

public sealed class SaveInfoView
{
    public JsonNode? Title { get; init; }
    public JsonNode? World { get; init; }
    public JsonNode? Created { get; init; }
    public JsonNode? Mode { get; init; }
    public JsonNode? Type { get; init; }
    public JsonNode? Autosave { get; init; }
    public JsonNode? Difficulty { get; init; }
    public JsonNode? Permadeath { get; init; }
}

public sealed class CommandView
{
    public SaveInfoView Save { get; init; } = new();
    public Dictionary<string, FieldRef?> Progression { get; init; } = new();
}

public sealed class FocusView : FieldRef
{
    public long? Total { get; init; }
    public FieldRef? TotalRef { get; init; }
}

public sealed class AbilityView
{
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public FieldRef? Level { get; init; }
    public FieldRef? Spent { get; init; }
    public int? CurrentLevel { get; init; }
    public int[]? Thresholds { get; init; }
}

public sealed class EffectView
{
    public string Asset { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public FieldRef? Stacks { get; init; }
    public List<FieldRef?> Magnitudes { get; init; } = new();
}

public sealed class BondView
{
    public string Partner { get; init; } = "";
    public string PartnerName { get; init; } = "";
    public bool PartnerInRoster { get; init; }
    public FieldRef? Level { get; init; }
    public FieldRef? Progress { get; init; }
    public FieldRef? Cross { get; init; }
    public FieldRef? Highest { get; init; }
    public double GameLevel { get; init; }
}

public sealed class OperatorView
{
    public string Guid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public bool Dead { get; init; }
    public long Injuries { get; init; }
    public bool HasPortrait { get; init; }
    public FocusView? Focus { get; init; }
    public List<AbilityView> Abilities { get; init; } = new();
    public List<EffectView> Effects { get; init; } = new();
    public List<BondView> Bonds { get; init; } = new();
    public bool TreeIncomplete { get; set; }
}

public sealed class PersonnelView
{
    public List<OperatorView> Roster { get; init; } = new();
    public List<OperatorView> Memorial { get; init; } = new();
    public double CrossTrainingTotal { get; init; }
    public string[] BondWords { get; init; } = Array.Empty<string>();
}

public sealed class InjuredView
{
    public string Guid { get; init; } = "";
    public string Name { get; init; } = "";
    public long Injuries { get; init; }
    public bool HasPortrait { get; init; }
}

public sealed class TreatingView
{
    public string Slot { get; init; } = "";
    public List<string> Operators { get; init; } = new();
    public long? Started { get; init; }
}

public sealed class MedbayCostView
{
    public int Bed { get; init; }
    public int Tank { get; init; }
    public int BedDouble { get; init; }
    public int TankDouble { get; init; }
}

public sealed class MedbayView
{
    public int Beds { get; init; }
    public int Tanks { get; init; }
    public MedbayCostView Cost { get; init; } = new();
    public List<InjuredView> Injured { get; init; } = new();
    public List<TreatingView> Treating { get; init; } = new();
}

public sealed class ArmoryItemView
{
    public string Asset { get; init; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; init; } = "";
    public int? Tier { get; init; }
    public string Rarity { get; init; } = "";
    public string Description { get; init; } = "";
    public FieldRef? Count { get; init; }
}

public sealed class ArmoryView
{
    public List<ArmoryItemView> Items { get; init; } = new();
}

public sealed class RegionView
{
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public FieldRef? Influence { get; init; }
    public FieldRef? Contacts { get; init; }
    public FieldRef? Reward { get; init; }
}

public sealed class GalaxyView
{
    public List<RegionView> Regions { get; init; } = new();
}

public sealed class CoilRowView
{
    public string Id { get; init; } = "";
    public string Tier { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class CoilView
{
    public List<CoilRowView> Active { get; init; } = new();
}

public sealed class UpgradeNodeView
{
    public string Name { get; init; } = "";
    public string? Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status { get; init; } = "";
    public int? Den { get; init; }
    public bool DenMet { get; init; }
    public bool Major { get; init; }
    public int? Duration { get; init; }
    public IReadOnlyDictionary<string, int> Cost { get; init; } = new Dictionary<string, int>();
    public bool CanStart { get; init; }
    public bool CanExpedite { get; init; }
    public string Reason { get; init; } = "";
    public long Started { get; init; }
    public long Progress { get; init; }
    public long? Remaining { get; init; }
}

public sealed class UpgradeRowView
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public List<UpgradeNodeView> Nodes { get; init; } = new();
}

public sealed class UpgradeTabView
{
    public string Key { get; init; } = "";
    public List<UpgradeRowView> Rows { get; init; } = new();
}

public sealed class UpgradesView
{
    public long Turn { get; init; }
    public int InProgress { get; init; }
    public long Level { get; init; }
    public bool Gamedata { get; init; }
    public List<UpgradeTabView> Tabs { get; init; } = new();
    public List<UpgradeNodeView> Slots { get; init; } = new();
}

/// <summary>Everything the screens show for one open save.</summary>
public sealed class SaveView
{
    public HeaderView Header { get; init; } = new();
    public CommandView Command { get; init; } = new();
    public PersonnelView Personnel { get; init; } = new();
    public ArmoryView Armory { get; init; } = new();
    public MedbayView Medbay { get; init; } = new();
    public GalaxyView Galaxy { get; init; } = new();
    public CoilView Coil { get; init; } = new();
    public UpgradesView? Upgrades { get; init; }
}
