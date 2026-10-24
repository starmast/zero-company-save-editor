using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Actions;

/// <summary>
/// The game's profile file, <c>SharedGameSettings.sav</c>: one per player, shared by every campaign. It is a plain GVAS file (not a save
/// archive) holding the achievement counters (AchievementStatData.Stats: stat tag -> Int64 count), the profile's unlocks
/// (UserProfileFactTags, such as story outfits and "campaign complete"), the best reward tier reached in each galaxy zone
/// (UserProfileFactValues), the language and first-run flags. The game adds to a counter as you play and unlocks an achievement once
/// it reaches the target in <see cref="Achievements"/> (from the game's DefaultGame.ini).
/// </summary>
public static class ProfileOps
{
    public const string FileName = "SharedGameSettings.sav";
    const string StatPrefix = "br.Online.Stats.";

    /// <summary>An achievement: its id (as the platforms know it), the counter it watches and the count that unlocks it.</summary>
    public sealed record Achievement(string Id, string Stat, int Target)
    {
        /// <summary>"There Will Be No Bargain" from the id "ThereWillBeNoBargain".</summary>
        public string Name => Spaced(Id);
    }

    static Achievement A(string id, string stat, int target) => new(id, stat, target);

    /// <summary>Every achievement the game counts locally, as its DefaultGame.ini lists them (BitReactorCoreAchievementManagerSettings).</summary>
    public static readonly IReadOnlyList<Achievement> Achievements = new[]
    {
        A("InAGalaxyFarFarAway", "CompletedMissions", 1), A("AnEndOfTheInfinite", "FinalMissionCompleted", 1),
        A("IfYouKnowYouKnow", "EnemiesKilled", 1138), A("Forceful", "TelForcePushUsed", 1),
        A("ANaturalPartOfLife", "ANaturalPartOfLife", 1), A("WhereCreditIsDue", "NeeshStorePurchases", 1),
        A("HeroesOnBothSides", "TrickLucoBond", 4), A("UpstairsDownstairs", "KabbJaeBond", 4), A("LostCauses", "HawksTelBond", 4),
        A("WordOfHonor", "HawksClyBond", 4), A("ClassAndDistinction", "HawksJaeBond", 4), A("WarHorses", "HawksKabbBond", 4),
        A("SharpShooters", "HawksLucoBond", 4), A("OldFriends", "HawksTrickBond", 4),
        A("YourFocusDeterminesYourReality", "YourFocusDeterminesYourReality", 1), A("ScumAndVillainy", "ScumAndVillainyComplete", 1),
        A("FathomRevealed", "FathomRevealedComplete", 1), A("OneLessClanker", "OneLessClankerComplete", 1),
        A("SkyHigh", "SkyHighComplete", 1), A("AWayToPassTheTime", "AWayToPassTheTimeComplete", 1),
        A("MordantReclaimed", "MordantReclaimedComplete", 1), A("ANewOrder", "ANewOrderComplete", 1),
        A("JungleWarfare", "JungleWarfareComplete", 1), A("WithFriendsLikeThese", "WithFriendsLikeTheseComplete", 1),
        A("SignHere", "SignHere", 1), A("FullRoster", "FullRoster", 20), A("ToTheVictorTheSpoils", "ToTheVictorTheSpoils", 1),
        A("AcrossTheStars", "AcrossTheStars", 36), A("ThereWillBeNoBargain", "ThereWillBeNoBargain", 3),
        A("SignatureFlair", "SignatureFlair", 1), A("FullyGeared", "FullyGeared", 4),
        A("CompleteSergeant", "CompleteSergeant", 1), A("CompleteLieutenant", "CompleteLieutenant", 1),
        A("CompleteCaptain", "CompleteCaptain", 1), A("CompleteMajor", "CompleteMajor", 1),
        A("WatchThatFirstStep", "WatchThatFirstStep", 1), A("SeeYourOwnDemise", "SeeYourOwnDemise", 1),
        A("NeverTellMeTheOdds", "NeverTellMeTheOdds", 90), A("DoOrDoNot", "DoOrDoNot", 90), A("AngryBeeps", "AngryBeeps", 4),
        A("TheTrooper", "TheTrooper", 1), A("TheCommando", "TheCommando", 1), A("TheFighter", "TheFighter", 1),
        A("TheBaroness", "TheBaroness", 1), A("TheExile", "TheExile", 1), A("ThePadawan", "ThePadawan", 1),
        A("Legend", "Legend", 1), A("ThickAsTheives", "ThickAsTheives", 1), A("BeepBooooop", "BeepBooooop", 1),
        A("ActiveDuty", "ActiveDuty", 50), A("CustomizationDressCode", "CustomizationDressCode", 1), A("TheHighGround", "TheHighGround", 10),
    };

    /// <summary>Whether <paramref name="g"/> is the profile file (it has achievement counters and no campaign).</summary>
    public static bool IsProfile(GvasFile g) => g.Root.Any(n => n.Name == "AchievementStatData");

    static GvasNode? StatsMap(GvasFile g) => g.Child(g.Root.FirstOrDefault(n => n.Name == "AchievementStatData"), "Stats");

    /// <summary>
    /// Start a counter the profile does not have yet (the game adds one the first time it counts): a copy of an existing counter entry
    /// with both of its stat tags renamed and its count set. Only the counters of the game's achievements can be started.
    /// </summary>
    public static GvasFile AddCounter(GvasFile g, string stat, long count, CutInfo cut)
    {
        if (Achievements.All(a => a.Stat != stat)) throw new GvasException($"{stat} is not one of the game's achievement counters");
        if (count < 0) throw new GvasException("a count cannot be negative");
        if (Counters(g).Any(c => c.Stat == stat)) throw new GvasException($"the profile already counts {stat}");
        var map = StatsMap(g) ?? throw new GvasException("this file has no achievement counters");
        var donor = map.Children?.FirstOrDefault(e => e.Children is { Count: 2 }) ?? throw new GvasException("the profile has no counter to copy the layout from");
        cut.Prefixes.Add(PathKey.Of(GvasFile.PathOf(map)));
        int index = map.Children!.Count;
        g = g.MapAppendRaw(map, g.Slice(donor.Start, donor.End));
        GvasNode Entry() => StatsMap(g)!.Children![index];
        g = g.SetString(g.Child(Entry().Children![0], "TagName")!, StatPrefix + stat);
        g = g.SetString(g.Child(g.Child(g.Child(Entry().Children![1], "Data"), "Identifier"), "TagName")!, StatPrefix + stat);
        g.Set(g.Child(g.Child(Entry().Children![1], "Data"), "Stat")!, count);
        cut.ProfileCounters[stat] = count;
        return g;
    }

    /// <summary>Each counter in the file: stat name (without "br.Online.Stats."), and the node holding its count.</summary>
    public static List<(string Stat, GvasNode Count)> Counters(GvasFile g)
    {
        var @out = new List<(string, GvasNode)>();
        foreach (var e in StatsMap(g)?.Children ?? new List<GvasNode>())
        {
            if (e.Children is not { Count: 2 } kv || g.Child(kv[0], "TagName") is not { } t) continue;
            var count = g.Child(g.Child(kv[1], "Data"), "Stat");
            var tag = g.GetStr(t);
            if (count != null && tag.StartsWith(StatPrefix, StringComparison.Ordinal)) @out.Add((tag[StatPrefix.Length..], count));
        }
        return @out;
    }

    /// <summary>The profile's unlock facts (story outfits, campaign complete ...); empty when they are not a plain tag list.</summary>
    public static List<string> Unlocks(GvasFile g)
    {
        var n = g.Root.FirstOrDefault(r => r.Name == "UserProfileFactTags");
        if (n == null) return new List<string>();
        try { return CoilOps.ReadTags(g, n); }
        catch (GvasException) { return new List<string>(); }
    }

    /// <summary>Per galaxy zone, the best influence reward tier reached in any campaign (the profile's fact values keyed by zone tag).</summary>
    public static List<(string Tag, GvasNode Value)> ZoneBests(GvasFile g)
    {
        var @out = new List<(string, GvasNode)>();
        foreach (var e in g.Root.FirstOrDefault(r => r.Name == "UserProfileFactValues")?.Children ?? new List<GvasNode>())
            if (e.Children is { Count: 2 } kv && g.Child(kv[0], "TagName") is { } t && GvasFile.Scalars.ContainsKey(kv[1].TName))
                @out.Add((g.GetStr(t), kv[1]));
        return @out;
    }

    /// <summary>"ThereWillBeNoBargain" -> "There Will Be No Bargain"; "AWayToPassTheTime" -> "A Way To Pass The Time".</summary>
    public static string Spaced(string id)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            if (i > 0 && char.IsUpper(id[i]) && (char.IsLower(id[i - 1]) || i + 1 < id.Length && char.IsLower(id[i + 1]))) sb.Append(' ');
            sb.Append(id[i]);
        }
        return sb.ToString();
    }
}
