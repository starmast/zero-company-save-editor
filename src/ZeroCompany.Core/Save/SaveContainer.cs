using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ZeroCompany.Core.Save;

public class SaveFormatException : Exception
{
    public SaveFormatException(string message) : base(message) { }
}

/// <summary>One ZIP entry as found in the original file; enough to write it back the same way.</summary>
public sealed record ZipEntryInfo(string Name, DateTimeOffset LastWrite, bool Deflated, uint ExternalAttributes);

/// <summary>
/// Reading and rebuilding Zero Company .sav containers. Most saves are ZIP archives (SaveGame + metadata);
/// settings/databank saves are raw GVAS.
/// </summary>
public sealed class SaveContainer
{
    public const string MainEntry = "SaveGame";
    /// <summary>Largest entry we unpack. Real payloads are a few MB; a save may be someone else's, so a bogus giant entry is refused.</summary>
    public const int MaxEntryBytes = 64 << 20;
    static readonly byte[] GvasMagic = "GVAS"u8.ToArray();

    public bool IsZip { get; }
    public byte[] Raw { get; }                      // whole file as read
    public byte[] Gvas { get; }                     // the GVAS payload
    public IReadOnlyList<ZipEntryInfo> Infos { get; }
    public IReadOnlyDictionary<string, byte[]> Blobs { get; }

    SaveContainer(bool isZip, byte[] raw, byte[] gvas, IReadOnlyList<ZipEntryInfo> infos, IReadOnlyDictionary<string, byte[]> blobs)
    {
        IsZip = isZip; Raw = raw; Gvas = gvas; Infos = infos; Blobs = blobs;
    }

    public string? MetadataJson =>
        Blobs.TryGetValue("SaveGameMetaData.json", out var b) ? TryDecodeUtf16(b) : null;

    public static SaveContainer Load(byte[] raw)
    {
        if (StartsWith(raw, "PK\x03\x04"u8))
        {
            List<ZipEntryInfo> infos;
            var blobs = new Dictionary<string, byte[]>();
            try
            {
                var methods = ReadCentralDirectoryMethods(raw);
                using var z = new ZipArchive(new MemoryStream(raw), ZipArchiveMode.Read);
                infos = new List<ZipEntryInfo>();
                for (int i = 0; i < z.Entries.Count; i++)
                {
                    var e = z.Entries[i];
                    bool deflated = methods.Count > i ? methods[i] == 8 : true;
                    infos.Add(new ZipEntryInfo(e.FullName, e.LastWriteTime, deflated, (uint)e.ExternalAttributes));
                    if (e.Length > MaxEntryBytes) throw new SaveFormatException($"zip entry {e.FullName} is too large");
                    using var s = e.Open();
                    blobs[e.FullName] = ReadCapped(s, e.FullName);      // throws InvalidDataException on a corrupt entry
                }
            }
            catch (InvalidDataException e) { throw new SaveFormatException($"bad zip: {e.Message}"); }
            if (!blobs.TryGetValue(MainEntry, out var main) || !StartsWith(main, GvasMagic))
                throw new SaveFormatException("zip has no GVAS 'SaveGame' entry");
            return new SaveContainer(true, raw, main, infos, blobs);
        }
        if (StartsWith(raw, GvasMagic))
            return new SaveContainer(false, raw, raw, Array.Empty<ZipEntryInfo>(), new Dictionary<string, byte[]>());
        throw new SaveFormatException("not a ZIP or GVAS file");
    }

    /// <summary>Return a new file with the main GVAS payload (and any <paramref name="replace"/> entries) swapped.</summary>
    public byte[] Rebuild(byte[] newGvas, IReadOnlyDictionary<string, byte[]>? replace = null)
    {
        if (!IsZip) return newGvas;
        using var @out = new MemoryStream();
        using (var z = new ZipArchive(@out, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var info in Infos)
            {
                byte[] data = info.Name == MainEntry
                    ? newGvas
                    : replace != null && replace.TryGetValue(info.Name, out var r) ? r : Blobs[info.Name];
                var e = z.CreateEntry(info.Name, info.Deflated ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                e.LastWriteTime = info.LastWrite;
                e.ExternalAttributes = (int)info.ExternalAttributes;
                using var s = e.Open();
                s.Write(data, 0, data.Length);
            }
        }
        return @out.ToArray();
    }

    /// <summary>Read a whole entry, refusing one that unpacks to more than <see cref="MaxEntryBytes"/> (whatever its header claims).</summary>
    static byte[] ReadCapped(Stream s, string name)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > MaxEntryBytes) throw new SaveFormatException($"zip entry {name} is too large");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>Compression method (0 = stored, 8 = deflate) of each central-directory record, in order.</summary>
    static List<int> ReadCentralDirectoryMethods(byte[] raw)
    {
        var methods = new List<int>();
        int eocd = -1;
        for (int i = raw.Length - 22; i >= Math.Max(0, raw.Length - 22 - 65535); i--)
            if (raw[i] == 0x50 && raw[i + 1] == 0x4B && raw[i + 2] == 5 && raw[i + 3] == 6) { eocd = i; break; }
        if (eocd < 0) return methods;
        int total = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(eocd + 10));
        long start = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(eocd + 16));
        if (start > raw.Length) return methods;                 // malformed: ZipArchive will report it
        int p = (int)start;
        for (int i = 0; i < total && p >= 0 && p + 46 <= raw.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(p)) != 0x02014B50) break;
            methods.Add(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(p + 10)));
            int n = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(p + 28));
            int m = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(p + 30));
            int k = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(p + 32));
            p += 46 + n + m + k;
        }
        return methods;
    }

    static bool StartsWith(byte[] data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    // ---- metadata helpers --------------------------------------------------
    static string? TryDecodeUtf16(byte[] raw)
    {
        try { return new UnicodeEncoding(false, true, true).GetString(raw); }
        catch (ArgumentException) { return null; }
    }

    internal static JsonNode? DecodeUtf16Json(byte[] raw)
    {
        try
        {
            int skip = raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE ? 2 : 0;
            var text = Encoding.Unicode.GetString(raw, skip, raw.Length - skip).TrimStart('﻿').TrimEnd('\0', ' ', '\r', '\n', '\t');
            return JsonNode.Parse(text);
        }
        catch (Exception e) when (e is JsonException or ArgumentException) { return null; }
    }

    /// <summary>Parsed SaveGameInfoJSON.txt (title, world, turn, difficulty ...) or null.</summary>
    public JsonObject? SaveInfo() =>
        Blobs.TryGetValue("SaveGameInfoJSON.txt", out var b) ? DecodeUtf16Json(b) as JsonObject : null;

    /// <summary>The name the game lists the save under, or null when the save has none.</summary>
    public string? Comment => SaveInfo()?["comment"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// The two header entries with a new comment, ready for <see cref="Rebuild"/>. The comment is stored twice: in
    /// <c>SaveGameInfoJSON.txt</c> (UTF-16 JSON) and in the binary <c>SaveGameInfo</c> as a length-prefixed string, and the game's
    /// load list reads the binary one, so both must change. Throws <see cref="SaveFormatException"/> when the header is not in the
    /// expected shape (nothing is guessed).
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> WithComment(string comment)
    {
        var old = Comment;
        if (string.IsNullOrEmpty(old)) throw new SaveFormatException("this save has no name to replace");
        if (!Blobs.TryGetValue("SaveGameInfoJSON.txt", out var json) || !Blobs.TryGetValue("SaveGameInfo", out var info))
            throw new SaveFormatException("this save has no header entries to rename");

        // the JSON entry: keep a byte order mark if there is one, replace the first "comment" value
        int skip = json.Length >= 2 && json[0] == 0xFF && json[1] == 0xFE ? 2 : 0;
        var text = Encoding.Unicode.GetString(json, skip, json.Length - skip);
        var rx = new Regex("(\"comment\"\\s*:\\s*\")((?:[^\"\\\\]|\\\\.)*)(\")");
        if (rx.Matches(text).Count != 1) throw new SaveFormatException("the save's name was not found exactly once in its header");
        var escaped = JsonEncodedText.Encode(comment).ToString();
        var newText = rx.Replace(text, m => m.Groups[1].Value + escaped + m.Groups[3].Value);
        var newJson = json.Take(skip).Concat(Encoding.Unicode.GetBytes(newText)).ToArray();

        // the binary entry: an Unreal string is an int32 length (including the terminator) then the characters; a negative length
        // means UTF-16. Find the old one exactly once and splice in the new one.
        var oldBytes = UnrealString(old);
        int at = IndexOf(info, oldBytes);
        if (at < 0 || IndexOf(info, oldBytes, at + 1) >= 0) throw new SaveFormatException("the save's name was not found exactly once in its header");
        var newInfo = info.Take(at).Concat(UnrealString(comment)).Concat(info.Skip(at + oldBytes.Length)).ToArray();

        return new Dictionary<string, byte[]> { ["SaveGameInfoJSON.txt"] = newJson, ["SaveGameInfo"] = newInfo };
    }

    static byte[] UnrealString(string s)
    {
        bool ascii = s.All(c => c < 128);
        var body = ascii ? Encoding.ASCII.GetBytes(s).Append((byte)0) : Encoding.Unicode.GetBytes(s).Concat(new byte[] { 0, 0 });
        var len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, ascii ? s.Length + 1 : -(s.Length + 1));
        return len.Concat(body).ToArray();
    }

    static int IndexOf(byte[] hay, byte[] needle, int from = 0)
    {
        for (int i = from; i <= hay.Length - needle.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    /// <summary>(characterDataWrapperSize, {GUID: wrappedSize}) as recorded in SaveGameMetaData.json.</summary>
    public (int? WrapperSize, Dictionary<string, int?> Characters) ReadCharacterSizes()
    {
        var chars = new Dictionary<string, int?>();
        if (!Blobs.TryGetValue("SaveGameMetaData.json", out var raw)) return (null, chars);
        var gi = (DecodeUtf16Json(raw) as JsonObject)?["GameInstanceMetaData"] as JsonObject;
        if (gi == null) return (null, chars);
        if (gi["characterMetaData"] is JsonArray arr)
            foreach (var c in arr.OfType<JsonObject>())
                if (c["guid"]?.GetValue<string>() is string g)
                    chars[g.ToUpperInvariant()] = c["wrappedSize"] is JsonValue v && v.TryGetValue<int>(out var w) ? w : null;
        int? wrapper = gi["characterDataWrapperSize"] is JsonValue wv && wv.TryGetValue<int>(out var ws) ? ws : null;
        return (wrapper, chars);
    }

    /// <summary>
    /// Return a new SaveGameMetaData.json with <c>GameInstanceSize</c> / <c>StrategySize</c> (and each character's
    /// <c>wrappedSize</c>) updated. Only touches a field whose current value differs, so we never "fix" something we
    /// do not understand. Returns null if nothing changes.
    /// </summary>
    public byte[]? SyncMetadataSizes(IReadOnlyDictionary<string, int> sizes, IReadOnlyDictionary<string, int>? characters = null)
    {
        if (!Blobs.TryGetValue("SaveGameMetaData.json", out var raw)) return null;
        var text = Encoding.Unicode.GetString(raw);
        bool changed = false;
        foreach (var (key, neu) in sizes)
        {
            var m = Regex.Match(text, "(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(\\d+)");
            if (m.Success && int.Parse(m.Groups[2].Value) != neu)
            {
                text = text[..m.Groups[2].Index] + neu + text[(m.Groups[2].Index + m.Groups[2].Length)..];
                changed = true;
            }
        }
        if (characters != null)
            foreach (var (guid, neu) in characters)
            {
                int gi = text.IndexOf($"\"guid\": \"{guid}\"", StringComparison.Ordinal);
                if (gi < 0) continue;
                int end = text.IndexOf('}', gi);
                if (end < 0) continue;
                var m = Regex.Match(text[gi..end], "(\"wrappedSize\"\\s*:\\s*)(\\d+)");
                if (m.Success && int.Parse(m.Groups[2].Value) != neu)
                {
                    int a = gi + m.Groups[2].Index;
                    text = text[..a] + neu + text[(gi + m.Groups[2].Index + m.Groups[2].Length)..];
                    changed = true;
                }
            }
        return changed ? Encoding.Unicode.GetBytes(text) : null;
    }

    // ---- cheap listing ------------------------------------------------------
    public sealed record Summary(long Size, DateTime MTimeUtc, string Kind, bool Editable,
        string? Title = null, string? World = null, string? Created = null, long? Turn = null,
        string? Difficulty = null, string? AutoSave = null, string? Error = null,
        string? GameMode = null, DateTime? FileCreated = null);

    /// <summary>Cheap listing info; only reads the small info entry, not the 7 MB payload.</summary>
    public static Summary Summarize(string path)
    {
        var fi = new FileInfo(path);
        var s = new Summary(fi.Length, fi.LastWriteTimeUtc, "raw", false, FileCreated: fi.CreationTime);
        try
        {
            var head = new byte[4];
            using (var f = File.OpenRead(path)) { if (f.Read(head, 0, 4) < 4) return s; }
            if (StartsWith(head, "PK\x03\x04"u8))
            {
                using var z = ZipFile.OpenRead(path);
                s = s with { Kind = "save", Editable = z.GetEntry(MainEntry) != null };
                var info = z.GetEntry("SaveGameInfoJSON.txt");
                if (info != null)
                {
                    using var es = info.Open();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    if (DecodeUtf16Json(ms.ToArray()) is JsonObject j)
                        s = s with
                        {
                            Title = Str(j, "comment"), World = Str(j, "worldName"), Created = Str(j, "creationTime"),
                            Turn = j["strategyTurn"] is JsonValue tv && tv.TryGetValue<long>(out var t) ? t : null,
                            Difficulty = Str(j, "currentDifficultyLevel"), AutoSave = Str(j, "autoSaveType"), GameMode = Str(j, "gameMode"),
                        };
                }
            }
            else if (StartsWith(head, GvasMagic)) s = s with { Kind = "settings" };
        }
        catch (Exception e) { s = s with { Error = e.Message }; }       // listing must never crash
        return s;
    }

    static string? Str(JsonObject o, string key) => o[key] is JsonValue v ? v.ToString() : null;

    /// <summary>The save's screenshot (SaveGame.jpg) without loading the 7 MB payload.</summary>
    public static byte[]? ReadThumbnail(string path)
    {
        try
        {
            using var z = ZipFile.OpenRead(path);
            var e = z.GetEntry("SaveGame.jpg");
            if (e == null) return null;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
    }
}
