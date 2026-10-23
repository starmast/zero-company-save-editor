using System.Text;
using System.Text.Json.Nodes;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// The campaign options game 1.2 added (Difficulty Settings > Additional Campaign Options, chosen when a campaign starts). A save keeps
/// each choice as a fact tag in four places: StrategyData.CampaignOptions.FactTags (setting -> tag), StrategyData.PermanentFactTags (the
/// same tags among the campaign's permanent facts), and the two header entries (SaveGameInfoJSON.txt and SaveGameInfo, see
/// <c>SaveContainer.WithCampaignOptions</c>). <see cref="Set"/> changes the first two; the header follows in <c>SaveService.Apply</c>.
/// Campaigns started before 1.2 have none of these, even when saved again in 1.2, so there is nothing to change there.
/// </summary>
public static class CampaignOptionOps
{
    public sealed record Choice(string Tag, string Label);
    public sealed record Setting(string Key, string Label, string Note, IReadOnlyList<Choice> Choices)
    {
        public string? LabelOf(string tag) => Choices.FirstOrDefault(c => c.Tag == tag)?.Label;
    }

    /// <summary>The setting key the header uses in front of each setting name.</summary>
    public const string HeaderPrefix = "BitReactorUserSettingDefinition:";
    const string P = "Facts.CampaignOptions.";

    static Choice[] OffOn(string stem) => new[] { new Choice($"{P}{stem}.Off", "Off"), new Choice($"{P}{stem}.On", "On") };
    static Choice[] Of(params (string Tag, string Label)[] c) => c.Select(x => new Choice(P + x.Tag, x.Label)).ToArray();

    /// <summary>Every option the game offers, in its menu order, with the tags its own tag list defines for each choice.</summary>
    public static readonly IReadOnlyList<Setting> Settings = new Setting[]
    {
        new("US_Gameplay_HeroDeath", "Permadeath", "Operators who die stay dead.", OffOn("Death.PermaDeath")),
        new("US_Injury_OperatorInjuries", "Operator injuries", "How many injuries an operator can carry before a defeat in combat kills them.",
            Of(("Death.ThreeStrikes", "Three strikes"), ("Death.TwoStrikes", "Two strikes"), ("Death.InstantDeath", "Instant death"))),
        new("US_Injury_RiskOfDeath", "Risk of death", "A chance that a defeated operator dies whatever their injuries.",
            Of(("Death.NoRisk", "None"), ("Death.StaticRisk", "Some risk"), ("Death.EscalatingRisk", "Rising risk"))),
        new("US_Advantage_AdvantageGeneration", "Advantage generation", "Half: actions that give Advantage only do so half the time.",
            Of(("Advantage.ADVChance50.Off", "Full"), ("Advantage.ADVChance50.On", "Half"))),
        new("US_Advantage_AOE", "No Advantage from area attacks", "", OffOn("Advantage.NoADVFromAoE")),
        new("US_Advantage_Assists", "No Advantage from assist shots", "", OffOn("Advantage.NoADVFromAssist")),
        new("US_Gameplay_AssistShotChance", "Assist shots can miss", "", OffOn("Tactical.AssistCanMiss")),
        new("US_Gameplay_EnemyCrits", "Enemies can critically hit", "", OffOn("Tactical.EnemiesCanCrit")),
        new("US_Gameplay_NoPerfectShots", "No perfect shots", "On: hit chance stops short of 100%.", OffOn("Tactical.PerfectShotsCanMiss")),
        new("US_Progression_OperatorHealth", "Operator health gain", "How much health operators gain as they spend focus points.",
            Of(("Health.FullPassiveGain", "Full"), ("Health.HalfPassiveGain", "Half"), ("Health.NoPassiveGain", "None"))),
        new("US_Progression_NoCTFromLowBond", "No cross training from low bonds", "", OffOn("Progression.NoCTFromLowBond")),
        new("US_Specializations_RecruitmentRefresh", "Locked recruitment refresh", "", OffOn("Gameplay.NoRecruitmentRefresh")),
        new("US_Specializations_LockedTalent", "Locked talents", "", OffOn("Specializations.LockTalents")),
        new("US_Specializations_LockedSpecializations", "Locked specializations", "", OffOn("Specializations.LockSpecializations")),
        new("US_Specializations_LockedWeapon", "Locked weapon respec", "", OffOn("Specializations.LockWeaponSpec")),
    };

    public static Setting? Find(string key) => Settings.FirstOrDefault(s => s.Key == key);

    static GvasNode? Map(GvasFile g) => g.Child(g.Child(g.StrategyDataOrNull(), "CampaignOptions"), "FactTags");
    static GvasNode? Permanent(GvasFile g) => g.Child(g.StrategyDataOrNull(), "PermanentFactTags");

    static (string Key, GvasNode Tag)? EntryOf(GvasFile g, GvasNode entry)
    {
        if (entry.Children is not { Count: 2 } kv) return null;
        var name = g.Child(kv[0], "PrimaryAssetName");
        var tag = g.Child(kv[1], "TagName");
        return name != null && tag != null ? (g.GetStr(name), tag) : null;
    }

    /// <summary>Setting key (US_...) -> the tag the campaign holds, in save order; null when the campaign has no options.</summary>
    public static Dictionary<string, string>? Read(GvasFile g)
    {
        var map = Map(g);
        if (map?.Children is not { Count: > 0 } entries) return null;
        var @out = new Dictionary<string, string>();
        foreach (var e in entries)
            if (EntryOf(g, e) is { } kv) @out[kv.Key] = g.GetStr(kv.Tag);
        return @out.Count > 0 ? @out : null;
    }

    /// <summary>The options a save's header lists (setting key -> tag), or null when it has none.</summary>
    public static Dictionary<string, string>? ReadHeader(JsonObject? info)
    {
        if (info?["campaignOptions"]?["factTags"] is not JsonObject tags) return null;
        var @out = new Dictionary<string, string>();
        foreach (var (k, v) in tags)
            if (v?["tagName"] is JsonValue t && t.TryGetValue<string>(out var s))
                @out[k.StartsWith(HeaderPrefix, StringComparison.Ordinal) ? k[HeaderPrefix.Length..] : k] = s;
        return @out.Count > 0 ? @out : null;
    }

    /// <summary>
    /// Whether the campaign has permadeath, from the header: game 1.2 keeps it as a campaign option, older saves as <c>bPermaDeath</c>.
    /// Null when the header says neither.
    /// </summary>
    public static bool? Permadeath(JsonObject? info)
    {
        if (ReadHeader(info) is { } opts && opts.TryGetValue("US_Gameplay_HeroDeath", out var tag)) return tag.EndsWith(".On", StringComparison.Ordinal);
        return info?["bPermaDeath"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    }

    /// <summary>Each option the campaign holds with the label of its choice, known settings first in menu order (unknown ones by their raw tag).</summary>
    public static IEnumerable<(Setting Setting, string Tag, string Label)> Describe(IReadOnlyDictionary<string, string> held)
    {
        foreach (var s in Settings)
            if (held.TryGetValue(s.Key, out var tag)) yield return (s, tag, s.LabelOf(tag) ?? tag);
        foreach (var (key, tag) in held)
            if (Find(key) == null) yield return (new Setting(key, key, "", Array.Empty<Choice>()), tag, tag);
    }

    /// <summary>
    /// Change one option to <paramref name="tag"/>: its entry in the campaign's options and the same tag among its permanent facts.
    /// The expected end state goes into <paramref name="cut"/> (including the old tag, which the header rewrite needs).
    /// </summary>
    public static GvasFile Set(GvasFile g, string key, string tag, CutInfo cut)
    {
        var def = Find(key) ?? throw new GvasException($"unknown campaign option {key}");
        if (def.Choices.All(c => c.Tag != tag)) throw new GvasException($"{def.Label}: not one of its choices");
        var map = Map(g) ?? throw new GvasException("this campaign has no campaign options (it was started before game 1.2)");
        var hit = (map.Children ?? new List<GvasNode>()).Select(e => EntryOf(g, e)).FirstOrDefault(kv => kv?.Key == key)
                  ?? throw new GvasException($"this campaign does not list {def.Label}");
        var old = g.GetStr(hit.Tag);
        if (old == tag) throw new GvasException($"{def.Label} is already {def.LabelOf(tag)}");
        var perm = Permanent(g) ?? throw new GvasException("the campaign's permanent facts were not found");
        var tags = CoilOps.ReadTags(g, perm);
        if (tags.Count(t => t == old) != 1 || tags.Contains(tag))
            throw new GvasException($"{def.Label}: the campaign's permanent facts do not hold the option as expected");

        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(map)));
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(perm)));
        g = g.SetString(hit.Tag, tag);
        perm = Permanent(g)!;
        var neu = tags.Select(t => t == old ? tag : t).ToList();
        g = g.ReplaceValue(perm, EncodeTags(neu));
        cut.CampaignOptions[key] = (cut.CampaignOptions.TryGetValue(key, out var was) ? was.From : old, tag);
        cut.PermanentFactTags = neu;
        return g;
    }

    /// <summary>A native tag list: int32 count, then one FString per tag (the layout <see cref="CoilOps.ReadTags"/> reads).</summary>
    public static byte[] EncodeTags(IReadOnlyList<string> tags)
    {
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(tags.Count));
        foreach (var t in tags)
        {
            ms.Write(BitConverter.GetBytes(t.Length + 1));
            ms.Write(Encoding.ASCII.GetBytes(t));
            ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    /// <summary>The campaign's permanent facts, or null when the save has none (or they are not a plain tag list).</summary>
    public static List<string>? PermanentTags(GvasFile g)
    {
        var perm = Permanent(g);
        if (perm == null) return null;
        try { return CoilOps.ReadTags(g, perm); }
        catch (GvasException) { return null; }
    }
}
