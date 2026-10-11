using System.Security.Cryptography;

namespace ZeroCompany.GameData.Extractor;

/// <summary>
/// The first time game data is extracted, CUE4Parse downloads the Oodle library from a third-party GitHub project and the editor
/// then loads it into its own process. This pins the exact build (the release CUE4Parse 1.2.2 fetches), so a replaced or tampered
/// download is deleted and never loaded. A library the user placed themselves is not checked: that is their choice.
/// When CUE4Parse is updated and points at a newer Oodle build, the pins below need updating with it.
/// </summary>
public static class OodleCheck
{
    /// <summary>
    /// SHA-256 of the library inside the OodleUE release 2026-06-04-1357: <c>bin/oodle-data-shared.dll</c> from
    /// clang-cl-x64-release.zip (Windows) and <c>lib/liboodle-data-shared.so</c> from gcc-x64-release.zip (Linux).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pinned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["oodle-data-shared.dll"] = "cba19529d0a3b5ec9c630e95652af01e123ae29a34a8a5f7507f5bcf23d9e82b",
        ["liboodle-data-shared.so"] = "f21d70299bbe873f63e8aef778b30f4dee6dec1fc09140c185caf1f8d153f771",
    };

    /// <summary>Returns normally when the downloaded library at <paramref name="path"/> is the pinned build; otherwise deletes it and throws.</summary>
    public static void VerifyDownloaded(string path, IReadOnlyDictionary<string, string>? pins = null)
    {
        pins ??= Pinned;
        var name = Path.GetFileName(path);
        var have = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (pins.TryGetValue(name, out var want) && string.Equals(have, want, StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(path); } catch (IOException) { }
        throw new ExtractionException(
            $"The Oodle library that was downloaded ({name}, SHA-256 {have[..16]}...) is not the build this version of the editor was "
            + $"checked against, so it was deleted and not loaded. Put your own copy of {name} in {Path.GetDirectoryName(path)} and run the "
            + "extraction again, or update the editor.");
    }
}
