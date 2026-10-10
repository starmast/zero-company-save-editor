using System.IO.Compression;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;
using static ZeroCompany.Tests.Synth;

namespace ZeroCompany.Tests;

public class GvasSyntheticTests
{
    static GvasNode Find(GvasFile g, params string[] names)
    {
        var nodes = g.Root; GvasNode? cur = null;
        foreach (var n in names) { cur = nodes.First(c => c.Name == n); nodes = cur.Children ?? new(); }
        return cur!;
    }

    [Fact]
    public void Parses_the_tree_and_reads_values()
    {
        var g = new GvasFile(Sample());
        Assert.Equal(new[] { "Turn", "Squad", "Wrapper", "Name" }, g.Root.Select(n => n.Name));
        Assert.Equal(7, g.Get(Find(g, "Turn")));
        Assert.Equal("Alpha", g.Get(Find(g, "Name")));
        Assert.Equal(3, Find(g, "Squad").Count);
        Assert.Equal(1000, g.Get(Find(g, "Wrapper", "ArchiveBytes", "Data", "Credits")));
        Assert.Equal(0, g.OpaqueCount);
        Assert.True(Find(g, "Wrapper", "ArchiveBytes").Nested);
    }

    [Fact]
    public void In_place_edit_changes_only_that_value()
    {
        var raw = Sample();
        var g = new GvasFile(raw);
        g.Set(Find(g, "Wrapper", "ArchiveBytes", "Data", "Credits"), 99999);
        var neu = g.ToBytes();
        Assert.Equal(raw.Length, neu.Length);
        var diff = Enumerable.Range(0, raw.Length).Where(i => raw[i] != neu[i]).ToList();
        Assert.NotEmpty(diff);
        Assert.True(diff.Max() - diff.Min() < 4);
        var g2 = new GvasFile(neu);
        Assert.Equal(99999, g2.Get(Find(g2, "Wrapper", "ArchiveBytes", "Data", "Credits")));
    }

    [Fact]
    public void SetString_resizes_and_fixes_every_ancestor()
    {
        var g = new GvasFile(Sample());
        var node = Find(g, "Wrapper", "ArchiveBytes", "Data", "Title");
        const string longTitle = "A much longer title than before";
        var g2 = g.SetString(node, longTitle);
        int delta = g2.Data.Length - g.Data.Length;
        Assert.Equal(longTitle.Length - 2, delta);
        Assert.Equal(0, g2.OpaqueCount);
        Assert.Equal(longTitle, g2.Get(Find(g2, "Wrapper", "ArchiveBytes", "Data", "Title")));
        foreach (var names in new[] { new[] { "Wrapper" }, new[] { "Wrapper", "ArchiveBytes" }, new[] { "Wrapper", "ArchiveBytes", "Data" } })
            Assert.Equal(Find(g, names).Size + delta, Find(g2, names).Size);
        Assert.Equal(Find(g, "Wrapper", "ArchiveBytes").Count + delta, Find(g2, "Wrapper", "ArchiveBytes").Count);
        Assert.Equal("Alpha", g2.Get(Find(g2, "Name")));
        Assert.Equal(7, g2.Get(Find(g2, "Turn")));
    }

    [Fact]
    public void ArrayAppendInt_updates_count_and_sizes()
    {
        var g = new GvasFile(Sample());
        var g2 = g.ArrayAppendInt(Find(g, "Squad"), 42);
        var arr = Find(g2, "Squad");
        Assert.Equal(4, arr.Count);
        Assert.Equal(Find(g, "Squad").Size + 4, arr.Size);
        var vals = Enumerable.Range(0, 4).Select(i => BitConverter.ToInt32(g2.Data, arr.ValueOffset + 4 + 4 * i)).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 42 }, vals);
        Assert.Equal(0, g2.OpaqueCount);
        Assert.Equal("Alpha", g2.Get(Find(g2, "Name")));
    }

    [Fact]
    public void Rejects_non_gvas_and_unsupported_edits()
    {
        Assert.Throws<GvasException>(() => new GvasFile(Cat("NOPE"u8.ToArray(), new byte[64])));
        var g = new GvasFile(Sample());
        Assert.Throws<GvasException>(() => g.Set(Find(g, "Name"), 5));
        Assert.Throws<GvasException>(() => g.SetString(Find(g, "Turn"), "x"));
        Assert.Throws<GvasException>(() => g.SetString(Find(g, "Name"), "naïve"));
    }

    [Fact]
    public void Zip_container_roundtrip_keeps_other_entries_identical()
    {
        var payload = Sample();
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, byte[] data, CompressionLevel lvl)
            {
                using var s = z.CreateEntry(name, lvl).Open();
                s.Write(data);
            }
            Add("SaveGameInfo", "info"u8.ToArray(), CompressionLevel.Optimal);
            Add("SaveGame", payload, CompressionLevel.Optimal);
            Add("SaveGamePortraits.zip", new byte[] { 1, 2, 3 }, CompressionLevel.NoCompression);
        }
        var loaded = SaveContainer.Load(ms.ToArray());
        Assert.True(loaded.IsZip);
        Assert.Equal(payload, loaded.Gvas);
        var g = new GvasFile(loaded.Gvas);
        g.Set(Find(g, "Turn"), 8);
        var again = SaveContainer.Load(loaded.Rebuild(g.ToBytes()));
        Assert.Equal("info"u8.ToArray(), again.Blobs["SaveGameInfo"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, again.Blobs["SaveGamePortraits.zip"]);
        var g2 = new GvasFile(again.Gvas);
        Assert.Equal(8, g2.Get(Find(g2, "Turn")));
        Assert.Equal(new[] { "SaveGameInfo", "SaveGame", "SaveGamePortraits.zip" }, again.Infos.Select(i => i.Name));
        Assert.False(again.Infos[2].Deflated);
        Assert.True(again.Infos[0].Deflated);
    }

    [Fact]
    public void Corrupt_inputs_are_rejected_cleanly()
    {
        Assert.Throws<SaveFormatException>(() => SaveContainer.Load("not a save at all"u8.ToArray()));
        Assert.Throws<SaveFormatException>(() => SaveContainer.Load(Cat("PK\u0003\u0004"u8.ToArray(), "garbage"u8.ToArray())));
    }

    [Fact]
    public void ArrayRemoveElement_shrinks_every_ancestor_and_keeps_the_tail()
    {
        var raw = EffectsSample();
        var g = new GvasFile(raw);
        var arr = Find(g, "Wrapper", "ArchiveBytes", "Data", "Effects");
        var victim = arr.Children![1];
        int cut = victim.End - victim.Start;
        var g2 = g.ArrayRemoveElement(arr, 1);
        Assert.Equal(raw.Length - cut, g2.Data.Length);
        Assert.Equal(0, g2.OpaqueCount);
        var arr2 = Find(g2, "Wrapper", "ArchiveBytes", "Data", "Effects");
        Assert.Equal(2, arr2.Count);
        Assert.Equal(arr.Size - cut, arr2.Size);
        var names = arr2.Children!.Select(e => ((string)g2.Get(GvasFile.Child(e, "Def")!)).Split('.').Last());
        Assert.Equal(new[] { "GE_A", "GE_C" }, names);
        Assert.Equal(5, g2.Get(Find(g2, "Wrapper", "ArchiveBytes", "Data", "After")));
        Assert.Equal("Alpha", g2.Get(Find(g2, "Name")));
        Assert.Equal(3, g2.Get(Find(g2, "Turn")));
        foreach (var n in new[] { new[] { "Wrapper" }, new[] { "Wrapper", "ArchiveBytes" }, new[] { "Wrapper", "ArchiveBytes", "Data" } })
            Assert.Equal(Find(g, n).Size - cut, Find(g2, n).Size);
        Assert.Equal(Find(g, "Wrapper", "ArchiveBytes").Count - cut, Find(g2, "Wrapper", "ArchiveBytes").Count);
        Assert.Equal(raw[victim.End..], g2.Data[victim.Start..]);
    }

    [Fact]
    public void ArrayRemoveElement_rejects_bad_requests()
    {
        var g = new GvasFile(EffectsSample());
        var arr = Find(g, "Wrapper", "ArchiveBytes", "Data", "Effects");
        foreach (var bad in new[] { -1, 3, 99 }) Assert.Throws<GvasException>(() => g.ArrayRemoveElement(arr, bad));
        Assert.Throws<GvasException>(() => g.ArrayRemoveElement(Find(g, "Turn"), 0));
    }
}
