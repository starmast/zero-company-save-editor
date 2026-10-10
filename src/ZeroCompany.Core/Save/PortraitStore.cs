using System.IO.Compression;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Save;

/// <summary>
/// Operator portraits stored inside saves (SaveGamePortraits.zip -> *.bin -> ExrImage). Each <c>.bin</c> is a plain property
/// list whose <c>ExrImage</c> byte array is an OpenEXR picture. Decoding that picture is the UI layer's job; this class only
/// finds the pictures.
/// </summary>
public sealed class PortraitStore
{
    public const string Entry = "SaveGamePortraits.zip";

    readonly Dictionary<string, byte[]> _bins = new();

    public PortraitStore(IReadOnlyDictionary<string, byte[]>? blobs)
    {
        if (blobs == null || !blobs.TryGetValue(Entry, out var raw) || raw.Length == 0) return;
        try
        {
            using var z = new ZipArchive(new MemoryStream(raw), ZipArchiveMode.Read);
            foreach (var e in z.Entries)
            {
                var stem = e.FullName.Contains('.') ? e.FullName[..e.FullName.LastIndexOf('.')] : e.FullName;
                var parts = stem.Split('-');
                if (parts.Length < 2 || parts[^2].Length != 32) continue;        // <class>-<GUID>-<hash>.bin
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                _bins[parts[^2].ToUpperInvariant()] = ms.ToArray();
            }
        }
        catch (InvalidDataException) { _bins.Clear(); }
    }

    public IReadOnlyCollection<string> Guids => _bins.Keys;

    public bool Has(string guid) => _bins.ContainsKey(guid.ToUpperInvariant());

    /// <summary>The raw OpenEXR bytes of one operator's portrait, or null.</summary>
    public byte[]? ExrBytes(string guid)
    {
        if (!_bins.TryGetValue(guid.ToUpperInvariant(), out var bin)) return null;
        return ExtractExr(bin);
    }

    /// <summary>Pull the raw EXR out of a portrait <c>.bin</c> (a property list with an ExrImage array).</summary>
    public static byte[]? ExtractExr(byte[] bin)
    {
        try
        {
            var g = GvasFile.FromPropertyList(bin);
            var node = g.Root.FirstOrDefault(p => p.Name == "ExrImage" && p.TName == "ArrayProperty");
            if (node == null || node.Size < 8) return null;
            return g.Data[(node.ValueOffset + 4)..node.End];                    // skip the int32 element count
        }
        catch (Exception e) when (e is GvasException or IndexOutOfRangeException or ArgumentException) { return null; }
    }
}
