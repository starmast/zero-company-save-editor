using ImageMagick;
using ZeroCompany.App.Services;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.Tests;

/// <summary>Portraits come out of save files that may be someone else's, so the decoder must take real EXRs and nothing else.</summary>
public class ExrDecoderTests
{
    static byte[] SmallExr(int w, int h)
    {
        using var img = new MagickImage(MagickColors.Red, (uint)w, (uint)h) { Format = MagickFormat.Exr };
        return img.ToByteArray();
    }

    [Fact]
    public void A_real_exr_decodes()
    {
        Assert.True(ExrDecoder.TryDecode(SmallExr(8, 6), out var w, out var h, out var rgba));
        Assert.Equal((8, 6), (w, h));
        Assert.Equal(8 * 6 * 4, rgba.Length);
    }

    [Fact]
    public void Other_picture_formats_are_refused_without_reaching_the_decoder()
    {
        using var img = new MagickImage(MagickColors.Blue, 4, 4) { Format = MagickFormat.Png };
        foreach (var bytes in new[]
        {
            img.ToByteArray(),                                              // a PNG
            System.Text.Encoding.ASCII.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"),
            System.Text.Encoding.ASCII.GetBytes("push graphic-context\nviewbox 0 0 1 1\npop graphic-context"),
            Array.Empty<byte>(),
            new byte[] { 0x76 },
        })
            Assert.False(ExrDecoder.TryDecode(bytes, out _, out _, out _));
    }

    [Fact]
    public void A_truncated_or_corrupt_exr_fails_cleanly()
    {
        var exr = SmallExr(16, 16);
        Assert.False(ExrDecoder.TryDecode(exr[..(exr.Length / 2)], out _, out _, out _));
        var junk = new byte[256]; new Random(1).NextBytes(junk);
        junk[0] = 0x76; junk[1] = 0x2f; junk[2] = 0x31; junk[3] = 0x01;        // right magic, nonsense after it
        Assert.False(ExrDecoder.TryDecode(junk, out _, out _, out _));
    }

    [SkippableFact]
    public void Every_portrait_in_the_local_sample_saves_still_decodes_within_the_size_cap()
    {
        var saves = new[] { Repo.SampleSave(), Repo.CombatSave() }.Where(p => p != null).ToList();
        Skip.If(saves.Count == 0, "no sample saves");
        int seen = 0;
        foreach (var path in saves)
        {
            var store = new PortraitStore(SaveContainer.Load(File.ReadAllBytes(path!)).Blobs);
            foreach (var guid in store.Guids)
            {
                var exr = store.ExrBytes(guid);
                Assert.NotNull(exr);
                Assert.True(ExrDecoder.TryDecode(exr!, out var w, out var h, out _), $"{Path.GetFileName(path)} {guid}");
                Assert.InRange(w, 1, ExrDecoder.MaxSide); Assert.InRange(h, 1, ExrDecoder.MaxSide);
                seen++;
            }
        }
        Skip.If(seen == 0, "the sample saves have no portraits");
    }
}
