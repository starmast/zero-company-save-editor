using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>A galaxy zone's claimed reward tiers: read from the save and changed so a reward can be claimed again.</summary>
public class ZoneRewardTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-zones-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    const string Jenth = "BitReactor.MapRegions.OuterSouth";

    (ZcWorkspace Ws, string Path) Rig(string? src)
    {
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(src!));
        File.Copy(src!, path);
        return (ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false), path);
    }

    static SaveEditor Open(ZcWorkspace ws, string path) => SaveEditor.Open(ws, path, () => null);
    static string CopyOf(SaveEditor ed, ApplyResult r) => Path.Combine(Path.GetDirectoryName(ed.Entry.Path)!, r.Written);
    static string? Late() => Repo.CombatSaveNamed("Hub_Traits.sav");

    [SkippableFact]
    public void Every_zone_of_a_finished_campaign_has_all_three_tiers_claimed()
    {
        var (ws, path) = Rig(Late());
        var ed = Open(ws, path);
        var regions = ed.Save.View.Galaxy.Regions;
        Assert.Equal(12, regions.Count);
        Assert.All(regions, r => Assert.Equal(new[] { 0, 1, 2 }, r.Claimed));
    }

    [SkippableFact]
    public void Unclaiming_the_Hutt_Roulette_Prize_changes_only_Jenths_list()
    {
        var (ws, path) = Rig(Late());
        var ed = Open(ws, path);
        var g = ed.Save.Gvas;
        var others = ed.Save.View.Galaxy.Regions.Where(r => r.Tag != Jenth).ToDictionary(r => r.Tag, r => ZoneRewardOps.Claimed(g, r.Tag));
        Assert.Throws<ZcException>(() => ed.Do("zone-claimed", new[] { "Nowhere", "3", "false" }));
        Assert.Contains("claimable again", ed.Do("zone-claimed", new[] { "Jenth", "3", "false" }));
        Assert.Contains(ed.Preview(), l => l.Text?.Contains("claimable again") == true);
        var res = ed.Apply(asCopy: true);

        var g2 = Open(ws, CopyOf(ed, res)).Save.Gvas;
        Assert.Equal(new[] { 0, 1 }, ZoneRewardOps.Claimed(g2, Jenth));
        Assert.All(others, kv => Assert.Equal(kv.Value, ZoneRewardOps.Claimed(g2, kv.Key)));
    }

    [SkippableFact]
    public void A_tier_can_be_marked_claimed_again_and_the_same_state_queues_nothing()
    {
        var (ws, path) = Rig(Late());
        var ed = Open(ws, path);
        ed.Do("zone-claimed", new[] { "Jenth", "1", "false" });
        ed.Do("zone-claimed", new[] { "Jenth", "1", "true" });                    // back where it was
        Assert.Equal(0, ed.PendingCount);
        ed.Do("zone-claimed", new[] { "OuterSouth", "1", "false" });
        ed.Do("zone-claimed", new[] { "OuterSouth", "2", "false" });
        var res = ed.Apply(asCopy: true);
        Assert.Equal(new[] { 2 }, ZoneRewardOps.Claimed(Open(ws, CopyOf(ed, res)).Save.Gvas, Jenth));
    }

    [SkippableFact]
    public void Core_refuses_bad_tiers_and_unknown_zones()
    {
        var src = Late();
        Skip.If(src == null, "no sample save");
        var g = new GvasFile(SaveContainer.Load(File.ReadAllBytes(src!)).Gvas);
        Assert.Throws<GvasException>(() => ZoneRewardOps.SetClaimed(g, Jenth, new[] { 0, 0 }, new CutInfo()));
        Assert.Throws<GvasException>(() => ZoneRewardOps.SetClaimed(g, Jenth, new[] { -1 }, new CutInfo()));
        Assert.Throws<GvasException>(() => ZoneRewardOps.SetClaimed(g, "BitReactor.MapRegions.Nowhere", new[] { 0 }, new CutInfo()));
        Assert.Null(ZoneRewardOps.Claimed(g, "BitReactor.MapRegions.Nowhere"));
    }

    [SkippableFact]
    public void An_early_campaign_reads_its_claims_too()
    {
        var (ws, path) = Rig(Repo.SampleSave());
        var regions = Open(ws, path).Save.View.Galaxy.Regions;
        Assert.NotEmpty(regions);
        Assert.All(regions, r => Assert.NotNull(r.Claimed));
    }
}
