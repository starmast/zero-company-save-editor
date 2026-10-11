using System.Security.Cryptography;
using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.Tests;

/// <summary>A downloaded Oodle library is loaded into the editor's process, so only the pinned build may be.</summary>
public class OodleCheckTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "zc-oodle-" + Guid.NewGuid().ToString("N"));

    public OodleCheckTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    string Write(string name, byte[] data)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, data);
        return p;
    }

    static string Hex(byte[] d) => Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();

    [Fact]
    public void The_pinned_hashes_are_well_formed_and_cover_the_names_CUE4Parse_downloads()
    {
        Assert.Contains("oodle-data-shared.dll", OodleCheck.Pinned.Keys);
        Assert.Contains("liboodle-data-shared.so", OodleCheck.Pinned.Keys);
        Assert.All(OodleCheck.Pinned.Values, h => Assert.Matches("^[0-9a-f]{64}$", h));
    }

    /// <summary>
    /// The pins are for one specific Oodle release. If CUE4Parse is updated to download a different one, this fails, which is the
    /// reminder to re-hash the new files and update <see cref="OodleCheck.Pinned"/> (otherwise extraction would refuse the download).
    /// </summary>
    [Fact]
    public void The_pins_are_for_the_oodle_release_the_referenced_cue4parse_downloads()
    {
        const string pinnedRelease = "WorkingRobot/OodleUE/releases/download/2026-06-04-1357/";
        var dll = typeof(CUE4Parse.Compression.OodleHelper).Assembly.Location;
        var text = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(dll));      // .NET string literals are UTF-16
        Assert.Contains(pinnedRelease, text);
        var all = System.Text.RegularExpressions.Regex.Matches(text, @"WorkingRobot/OodleUE/releases/download/[^/]+/").Select(m => m.Value).Distinct();
        Assert.Equal(new[] { pinnedRelease }, all);
    }

    [Fact]
    public void The_pinned_build_is_accepted_and_left_in_place()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        var path = Write("oodle-data-shared.dll", data);
        OodleCheck.VerifyDownloaded(path, new Dictionary<string, string> { ["oodle-data-shared.dll"] = Hex(data) });
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Any_other_download_is_deleted_and_refused()
    {
        var path = Write("oodle-data-shared.dll", new byte[] { 9, 9, 9 });
        var ex = Assert.Throws<ExtractionException>(() => OodleCheck.VerifyDownloaded(path));
        Assert.Contains("not the build", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_file_name_with_no_pinned_build_is_refused_too()
    {
        var data = new byte[] { 5, 6 };
        var path = Write("liboodle-data-shared.dylib", data);
        Assert.Throws<ExtractionException>(() => OodleCheck.VerifyDownloaded(path, new Dictionary<string, string> { ["oodle-data-shared.dll"] = Hex(data) }));
        Assert.False(File.Exists(path));
    }
}
