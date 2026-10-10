using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>
/// Healing removes exactly the injury effect and keeps every size consistent. The sample save only has an injured *fallen* operator;
/// the machinery is the same, so (as in the original tests) the model is told they are alive and the action runs on them.
/// </summary>
public class HealTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-heal-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (SaveService Svc, string Name, string Path) NewRig()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), gameRunning: () => null);
        svc.Open("t", name);
        return (svc, name, Path.Combine(dir, name));
    }

    /// <summary>Each saved effect of one operator as hex, counted (the effects form a multiset).</summary>
    static Dictionary<string, int> Bag(GvasFile g, string guid, out List<string> names)
    {
        var arr = MedbayOps.EffectsNode(g, guid)!;
        names = arr.Children!.Select(e => MedbayOps.EffectName(g, e)).ToList();
        var bag = new Dictionary<string, int>();
        foreach (var e in arr.Children!)
        {
            var k = Convert.ToHexString(g.Slice(e.Start, e.End));
            bag[k] = bag.GetValueOrDefault(k) + 1;
        }
        return bag;
    }

    static int Total(IEnumerable<KeyValuePair<string, int>> bag) => bag.Sum(kv => kv.Value);

    static Dictionary<string, int> Minus(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Select(kv => (kv.Key, N: kv.Value - b.GetValueOrDefault(kv.Key))).Where(x => x.N > 0).ToDictionary(x => x.Key, x => x.N);

    static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) => Total(Minus(a, b)) == 0 && Total(Minus(b, a)) == 0;

    [SkippableFact]
    public void Heal_removes_only_the_injury_and_keeps_every_size_consistent()
    {
        var (svc, name, path) = NewRig();
        var model = svc.Current!.Model;
        var op = model.Operators.Values.FirstOrDefault(o => o.Injuries != 0);
        Skip.If(op == null, "no injured operator in the sample");
        var beforeLoaded = SaveContainer.Load(File.ReadAllBytes(path));
        var g0 = new GvasFile(beforeLoaded.Gvas);
        var fx0 = Bag(g0, op!.Guid, out var names0);
        var others = MedbayOps.Characters(g0).Keys.Where(k => k != op.Guid).ToDictionary(k => k, k => Bag(g0, k, out _));

        op.Dead = false;                                                       // let the machinery run on this save
        var res = svc.Apply(Array.Empty<FieldChange>(), force: true, actions: new SaveAction[] { new SaveAction.HealOperator(op.Guid) });
        Assert.Equal(1, res.Count);
        Assert.False(string.IsNullOrEmpty(res.Backup));

        var loaded = SaveContainer.Load(File.ReadAllBytes(path));
        var g = new GvasFile(loaded.Gvas);
        var fx = Bag(g, op.Guid, out var names);
        Assert.DoesNotContain(names, n => n.StartsWith("GE_Injured"));
        Assert.Equal(names0.Where(n => !n.StartsWith("GE_Injured")).OrderBy(x => x, StringComparer.Ordinal), names.OrderBy(x => x, StringComparer.Ordinal));
        // only the injury element is gone; every remaining element is byte-for-byte what it was
        Assert.Equal(1, Total(Minus(fx0, fx)));
        Assert.Equal(0, Total(Minus(fx, fx0)));
        // no other operator was touched
        foreach (var (guid, bag0) in others) Assert.True(Same(bag0, Bag(g, guid, out _)));
        // the payload shrank by exactly the removed element, and the metadata mirrors the new sizes
        var removed = Minus(fx0, fx).Keys.Single();
        Assert.Equal(beforeLoaded.Gvas.Length - removed.Length / 2, loaded.Gvas.Length);
        var (total, per) = MedbayOps.CharacterSizes(g);
        var (metaTotal, metaPer) = loaded.ReadCharacterSizes();
        Assert.Equal(total, metaTotal);
        foreach (var (k, v) in per) Assert.Equal(v, metaPer[k]);
        Assert.Equal(g0.OpaqueCount, g.OpaqueCount);
    }

    [SkippableFact]
    public void Heal_combines_with_a_scalar_edit_in_one_apply()
    {
        var (svc, name, path) = NewRig();
        var model = svc.Current!.Model;
        var op = model.Operators.Values.FirstOrDefault(o => o.Injuries != 0);
        Skip.If(op == null, "no injured operator in the sample");
        op!.Dead = false;
        var credits = svc.Current.View.Header.Resources.First(r => r.Key == "Credits");
        svc.Apply(new[] { new FieldChange(credits.Id, credits.Value - 500) }, force: true,
                  actions: new SaveAction[] { new SaveAction.HealOperator(op.Guid) });
        Assert.Equal(credits.Value - 500, svc.Current!.View.Header.Resources.First(r => r.Key == "Credits").Value);
        Assert.Equal(0, MedbayOps.InjuryCount(new GvasFile(SaveContainer.Load(File.ReadAllBytes(path)).Gvas), op.Guid));
    }
}
