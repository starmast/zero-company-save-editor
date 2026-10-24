using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Sdk;

/// <summary>
/// Looks a save over without writing anything: that it parses, that its container rebuilds unchanged, that the sizes recorded in
/// its metadata match the data, that its name is stored consistently, and that the editor can build its screens from it.
/// </summary>
public static class SaveCheck
{
    public static IReadOnlyList<Check> Run(ZcWorkspace ws, string spec)
    {
        var entry = SaveCatalog.Resolve(ws, spec);
        var list = new List<Check>();

        byte[] raw;
        try { raw = File.ReadAllBytes(entry.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new[] { Check.Fail("file", e.Message) }; }

        SaveContainer c;
        try { c = SaveContainer.Load(raw); list.Add(Check.Ok("container", c.IsZip ? $"{c.Blobs.Count} entries" : "raw GVAS (not a game save)")); }
        catch (Exception e) when (e is SaveFormatException or GvasException or InvalidDataException) { return new[] { Check.Fail("container", e.Message) }; }

        GvasFile g;
        try { g = new GvasFile(c.Gvas); list.Add(Check.Ok("gvas", $"{g.Root.Count} roots, {g.Walk().Count():N0} nodes, {g.OpaqueCount} left raw")); }
        catch (Exception e) when (e is GvasException or ArgumentException or InvalidDataException or IndexOutOfRangeException)
        { list.Add(Check.Fail("gvas", "cannot be read: " + e.Message)); return list; }

        list.Add(Rebuild(c));
        if (c.IsZip)
        {
            list.Add(Header(c));
            list.Add(Metadata(c, g));
        }
        // settings and character-pool files are plain GVAS the editor does not open: nothing more to check, and nothing wrong
        list.Add(c.IsZip || ProfileOps.IsProfile(g) ? Screens(ws, entry) : Check.Ok("screens", "not a game save (settings or character pool); the editor leaves these alone"));
        return list;
    }

    static Check Rebuild(SaveContainer c)
    {
        try
        {
            var again = SaveContainer.Load(c.Rebuild(c.Gvas));
            if (!again.Gvas.AsSpan().SequenceEqual(c.Gvas)) return Check.Fail("rebuild", "the rebuilt container holds different game data");
            foreach (var (name, bytes) in c.Blobs)
                if (!again.Blobs.TryGetValue(name, out var b) || !b.AsSpan().SequenceEqual(bytes)) return Check.Fail("rebuild", $"entry {name} changed when the container was rebuilt");
            return Check.Ok("rebuild", "the container rebuilds with every entry unchanged");
        }
        catch (Exception e) when (e is SaveFormatException or InvalidDataException or IOException) { return Check.Fail("rebuild", e.Message); }
    }

    static Check Header(SaveContainer c)
    {
        if (string.IsNullOrEmpty(c.Comment)) return Check.Warn("header", "the save has no name (comment) in its header");
        try { c.WithComment(c.Comment); return Check.Ok("header", $"name \"{c.Comment}\" is stored in both header entries"); }
        catch (SaveFormatException e) { return Check.Fail("header", e.Message); }
    }

    static Check Metadata(SaveContainer c, GvasFile g)
    {
        if (!c.Blobs.TryGetValue("SaveGameMetaData.json", out var bytes)) return Check.Warn("metadata", "no SaveGameMetaData.json entry");
        var text = Encoding.Unicode.GetString(bytes);
        var problems = new List<string>();
        foreach (var (key, size) in SaveService.BlobSizes(g))
        {
            var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)");
            if (m.Success && int.Parse(m.Groups[1].Value) != size) problems.Add($"{key} says {m.Groups[1].Value}, the data is {size}");
        }
        var (_, per) = MedbayOps.CharacterSizes(g);
        var (_, metaPer) = c.ReadCharacterSizes();
        foreach (var (guid, size) in per)
            if (metaPer.TryGetValue(guid, out var ms) && ms != null && ms != size) problems.Add($"character {guid[..Math.Min(8, guid.Length)]} says {ms}, the data is {size}");
        return problems.Count == 0 ? Check.Ok("metadata", "recorded sizes match the data")
            : Check.Fail("metadata", string.Join("; ", problems), "a save written by the game or the editor keeps these in step");
    }

    static Check Screens(ZcWorkspace ws, CatalogEntry entry)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "zc-check-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var svc = new SaveService(SaveCatalog.DirsFor(ws, entry), tmp, ws.LoadDatabase(), () => null);
            var open = svc.Open(entry.DirId, entry.Name);
            var fields = FieldCatalog.Build(open);
            if (open.Profile is { } p)
                return Check.Ok("screens", $"the editor reads it as the profile: {p.Achievements.Count(a => a.Count != null)} achievement counters, {p.Unlocks.Count} unlocks, {fields.Entries.Count} editable values");
            return Check.Ok("screens", $"the editor reads it: {open.Model.Operators.Count} operators, {fields.Entries.Count} editable values{(open.Combat != null ? $", combat with {open.Combat.Units.Count} units" : "")}");
        }
        catch (Exception e) { return Check.Fail("screens", "the editor cannot read it: " + e.Message); }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { } }
    }
}
