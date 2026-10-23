using System.Text;
using System.Text.Json.Nodes;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>Game 1.2's campaign options: read from the save and the header, and changed in all four places a save keeps them.</summary>
public class CampaignOptionTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-options-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    const string Crits = "US_Gameplay_EnemyCrits", Death = "US_Gameplay_HeroDeath", Injuries = "US_Injury_OperatorInjuries";

    static string? Save12() => Repo.CombatSaveNamed("Hub_1_2.sav");

    (ZcWorkspace Ws, string Path) Rig(string? src)
    {
        Skip.If(src == null, "no game 1.2 sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(src!));
        File.Copy(src!, path);
        return (ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false), path);
    }

    static SaveEditor Open(ZcWorkspace ws, string path) => SaveEditor.Open(ws, path, () => null);
    static string CopyOf(SaveEditor ed, ApplyResult r) => Path.Combine(Path.GetDirectoryName(ed.Entry.Path)!, r.Written);

    static int Occurrences(byte[] hay, string text)
    {
        var needle = Encoding.ASCII.GetBytes(text); int n = 0;
        for (int i = hay.AsSpan().IndexOf(needle); i >= 0; )
        {
            n++;
            int next = hay.AsSpan(i + 1).IndexOf(needle);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return n;
    }

    [Fact]
    public void Every_choice_is_a_distinct_campaign_option_tag()
    {
        Assert.Equal(15, CampaignOptionOps.Settings.Count);
        Assert.Equal(15, CampaignOptionOps.Settings.Select(s => s.Key).Distinct().Count());
        var tags = CampaignOptionOps.Settings.SelectMany(s => s.Choices).Select(c => c.Tag).ToList();
        Assert.Equal(tags.Count, tags.Distinct().Count());
        Assert.All(tags, t => Assert.StartsWith("Facts.CampaignOptions.", t));
        Assert.All(CampaignOptionOps.Settings, s => Assert.InRange(s.Choices.Count, 2, 3));
    }

    [Fact]
    public void Permadeath_is_read_from_the_campaign_options_or_the_old_flag()
    {
        static JsonObject Opt(string tag) => new()
        {
            ["campaignOptions"] = new JsonObject { ["factTags"] = new JsonObject
                { [CampaignOptionOps.HeaderPrefix + Death] = new JsonObject { ["tagName"] = tag } } },
        };
        Assert.True(CampaignOptionOps.Permadeath(Opt("Facts.CampaignOptions.Death.PermaDeath.On")));
        Assert.False(CampaignOptionOps.Permadeath(Opt("Facts.CampaignOptions.Death.PermaDeath.Off")));
        Assert.True(CampaignOptionOps.Permadeath(new JsonObject { ["bPermaDeath"] = true }));       // saves made before game 1.2
        Assert.Null(CampaignOptionOps.Permadeath(new JsonObject()));
        Assert.Null(CampaignOptionOps.Permadeath(null));
    }

    [SkippableFact]
    public void A_1_2_save_lists_all_fifteen_options_in_the_body_and_the_header()
    {
        var (ws, path) = Rig(Save12());
        var ed = Open(ws, path);
        var body = CampaignOptionOps.Read(ed.Save.Gvas)!;
        Assert.Equal(15, body.Count);
        Assert.Equal(body, CampaignOptionOps.ReadHeader(ed.Save.Container.SaveInfo()));
        Assert.All(body.Values, t => Assert.Contains(t, CampaignOptionOps.PermanentTags(ed.Save.Gvas)!));
        Assert.True(CampaignOptionOps.Permadeath(ed.Save.Container.SaveInfo()));
        Assert.Equal("true", ed.Save.View.Command.Save.Permadeath?.ToString());
    }

    [SkippableFact]
    public void A_save_without_options_offers_none_and_refuses_the_action()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var ed = Open(ws, path);
        Assert.Null(CampaignOptionOps.Read(ed.Save.Gvas));
        Assert.Throws<ZcException>(() => ed.Do("set-option", new[] { "EnemyCrits", "On" }));
        Assert.Equal(0, ed.PendingCount);
    }

    [SkippableFact]
    public void Changing_options_rewrites_the_body_the_permanent_facts_and_both_header_entries_and_nothing_else()
    {
        var (ws, path) = Rig(Save12());
        var ed = Open(ws, path);
        var before = CampaignOptionOps.Read(ed.Save.Gvas)!;
        var oldGvas = ed.Save.Container.Gvas;
        var oldInfo = ed.Save.Container.Blobs["SaveGameInfo"];
        Assert.Throws<ZcException>(() => ed.Do("set-option", new[] { "EnemyCrits", "Sometimes" }));
        Assert.Throws<ZcException>(() => ed.Do("set-option", new[] { "NoSuchOption", "On" }));
        ed.Do("set-option", new[] { "EnemyCrits", "On" });
        ed.Do("set-option", new[] { "Permadeath", "Off" });
        ed.Do("set-option", new[] { "OperatorInjuries", "Two strikes" });
        Assert.Equal(3, ed.PendingCount);
        var res = ed.Apply(asCopy: true, copyName: "OPTIONS COPY");

        var after = Open(ws, CopyOf(ed, res)).Save;
        var want = new Dictionary<string, string>(before)
        {
            [Crits] = "Facts.CampaignOptions.Tactical.EnemiesCanCrit.On",
            [Death] = "Facts.CampaignOptions.Death.PermaDeath.Off",
            [Injuries] = "Facts.CampaignOptions.Death.TwoStrikes",
        };
        Assert.Equal(want, CampaignOptionOps.Read(after.Gvas));
        Assert.Equal(want, CampaignOptionOps.ReadHeader(after.Container.SaveInfo()));
        var perm = CampaignOptionOps.PermanentTags(after.Gvas)!;
        Assert.All(want.Values, t => Assert.Single(perm, t));
        Assert.DoesNotContain(before[Crits], perm);
        Assert.Equal("OPTIONS COPY", after.Container.Comment);                        // a new name and the options share the header entries
        Assert.False(CampaignOptionOps.Permadeath(after.Container.SaveInfo()));

        var info = after.Container.Blobs["SaveGameInfo"];
        foreach (var (key, tag) in want) Assert.Equal(1, Occurrences(info, tag));
        Assert.Equal(0, Occurrences(info, before[Crits]));
        Assert.Equal(Occurrences(oldInfo, "Facts.CampaignOptions."), Occurrences(info, "Facts.CampaignOptions."));
        // the customization conditions that mention option tags are left alone: only the two option lists changed
        var newGvas = after.Container.Gvas;
        Assert.Equal(Occurrences(oldGvas, "Facts.CampaignOptions.Tactical.EnemiesCanCrit.On") + 2, Occurrences(newGvas, "Facts.CampaignOptions.Tactical.EnemiesCanCrit.On"));
        Assert.Equal(Occurrences(oldGvas, "Facts.CampaignOptions.Death.StaticRisk"), Occurrences(newGvas, "Facts.CampaignOptions.Death.StaticRisk"));
    }

    [SkippableFact]
    public void Choosing_what_the_save_already_has_queues_nothing()
    {
        var (ws, path) = Rig(Save12());
        var ed = Open(ws, path);
        var current = CampaignOptionOps.Find(Crits)!.LabelOf(CampaignOptionOps.Read(ed.Save.Gvas)![Crits])!;
        Assert.Contains("already", ed.Do("set-option", new[] { "EnemyCrits", current }));
        Assert.Equal(0, ed.PendingCount);
    }

    [SkippableFact]
    public void A_changed_option_reads_back_through_the_service_and_the_original_is_untouched()
    {
        var (ws, path) = Rig(Save12());
        var bytes = File.ReadAllBytes(path);
        var ed = Open(ws, path);
        ed.Pending.SetCampaignOption("US_Progression_OperatorHealth", "Facts.CampaignOptions.Health.NoPassiveGain");
        var line = Assert.Single(ed.Preview());
        Assert.Contains("Operator health gain: None", line.Text);
        var res = ed.Apply(asCopy: true);
        Assert.Equal("Facts.CampaignOptions.Health.NoPassiveGain", CampaignOptionOps.Read(Open(ws, CopyOf(ed, res)).Save.Gvas)!["US_Progression_OperatorHealth"]);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void Core_refuses_a_tag_that_is_not_a_choice_of_the_option()
    {
        var src = Save12();
        Skip.If(src == null, "no game 1.2 sample save");
        var g = new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
        Assert.Throws<GvasException>(() => CampaignOptionOps.Set(g, Crits, "Facts.CampaignOptions.Death.TwoStrikes", new CutInfo()));
        Assert.Throws<GvasException>(() => CampaignOptionOps.Set(g, "US_Nope", "Facts.CampaignOptions.Death.TwoStrikes", new CutInfo()));
    }
}
