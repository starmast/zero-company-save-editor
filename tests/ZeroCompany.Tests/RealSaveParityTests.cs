using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>Numbers below come from the Python reference implementation run on the same sample save.</summary>
public class RealSaveParityTests
{
    [SkippableFact]
    public void Index_matches_the_python_reference()
    {
        var path = Repo.SampleSave();
        Skip.If(path == null, "no sample save in saves/");
        var c = SaveContainer.Load(File.ReadAllBytes(path!));
        Assert.True(c.IsZip);
        Assert.Equal(8, c.Infos.Count);
        Assert.Equal(7462119, c.Gvas.Length);
        var g = new GvasFile(c.Gvas);
        Assert.Equal("/Script/Bruno.BrunoSaveGame", g.SaveClass);
        Assert.Equal("++ProjectBruno+Stable", g.Branch);
        Assert.Equal(1933, g.PropsStart);
        Assert.Equal(7462115, g.PropsEnd);
        Assert.Equal(2, g.Root.Count);
        Assert.Equal(83734, g.Walk().Count());
        Assert.Equal(648, g.OpaqueCount);
        var (wrapper, chars) = c.ReadCharacterSizes();
        Assert.Equal(5801293, wrapper);
        Assert.Equal(9, chars.Count);
    }

    [SkippableFact]
    public void Unchanged_rebuild_round_trips_every_entry()
    {
        var path = Repo.SampleSave();
        Skip.If(path == null, "no sample save in saves/");
        var c = SaveContainer.Load(File.ReadAllBytes(path!));
        var again = SaveContainer.Load(c.Rebuild(c.Gvas));
        Assert.Equal(c.Infos.Select(i => (i.Name, i.Deflated)), again.Infos.Select(i => (i.Name, i.Deflated)));
        foreach (var (name, blob) in c.Blobs) Assert.Equal(blob, again.Blobs[name]);
    }
}
