using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ZeroCompany.DebugCli;

/// <summary>What the signature scripts of the "SWZC" UE4SS pack use to recognise one build of the game exe.</summary>
public sealed record PeIdentity(long Length, uint Stamp, uint ImageSize, uint CodeViewOffset, uint CodeViewRva, string IdentityHex)
{
    /// <summary>A label for the new entry: the pack names builds by Steam/EA build number, which the exe does not carry.</summary>
    public string Label => "auto-" + Stamp.ToString("x", CultureInfo.InvariantCulture);
}

/// <summary>
/// UE4SS finds its engine functions through the signature scripts of the SWZC pack (<c>ue4ss/UE4SS_Signatures/*.lua</c>). Each script only
/// accepts game builds it lists by exe length, PE timestamp, image size and CodeView GUID, so after a game update UE4SS stops at start-up
/// with "loaded fallback has no verified build profile; signature rejected" and no mod runs. This reads the new exe's identity from its PE
/// header and adds it to each script's <c>builds</c> table. Nothing else in the scripts changes: they still require the pattern to match
/// exactly once at a real function start in the loaded game, so a build where the functions really changed is still rejected (with a
/// different message). The pack's author has not verified such a build; originals are backed up first and can be restored.
/// </summary>
public static class SignaturePack
{
    public const string BackupFolder = "zcdebug-signature-backup";
    static readonly Regex BuildsEnd = new(@"(local builds = \{.*?identity = ""[0-9a-fA-F]+""\})(\r?\n[ \t]*\})", RegexOptions.Singleline | RegexOptions.Compiled);

    // ---- the exe ------------------------------------------------------------------------------------------------------

    /// <summary>Read the identity fields from the exe's PE headers without loading the (270 MB) file; null when it is not a 64-bit PE with a CodeView entry.</summary>
    public static PeIdentity? ReadIdentity(string exePath)
    {
        try
        {
            using var f = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[4096];
            if (f.Read(head, 0, head.Length) < 0x100 || head[0] != 'M' || head[1] != 'Z') return null;
            int nt = BitConverter.ToInt32(head, 0x3c);
            if (nt < 0x40 || nt + 0x200 > head.Length || BitConverter.ToUInt32(head, nt) != 0x00004550) return null;
            if (BitConverter.ToUInt16(head, nt + 4) != 0x8664) return null;
            int sections = BitConverter.ToUInt16(head, nt + 6), optSize = BitConverter.ToUInt16(head, nt + 20), opt = nt + 24;
            if (BitConverter.ToUInt16(head, opt) != 0x20b || sections is < 1 or > 96 || opt + optSize + sections * 40 > head.Length) return null;
            uint stamp = BitConverter.ToUInt32(head, nt + 8), imageSize = BitConverter.ToUInt32(head, opt + 56);
            uint dbgRva = BitConverter.ToUInt32(head, opt + 112 + 6 * 8), dbgSize = BitConverter.ToUInt32(head, opt + 112 + 6 * 8 + 4);
            long? Offset(uint rva)
            {
                for (int i = 0; i < sections; i++)
                {
                    int at = opt + optSize + i * 40;
                    uint va = BitConverter.ToUInt32(head, at + 12), raw = BitConverter.ToUInt32(head, at + 16), ptr = BitConverter.ToUInt32(head, at + 20);
                    if (rva >= va && rva < va + raw) return ptr + (rva - va);
                }
                return null;
            }
            if (dbgRva == 0 || dbgSize < 28 || Offset(dbgRva) is not { } dbgAt) return null;
            var dir = new byte[Math.Min(dbgSize, 28 * 16)];
            f.Seek(dbgAt, SeekOrigin.Begin);
            if (f.Read(dir, 0, dir.Length) != dir.Length) return null;
            for (int e = 0; e + 28 <= dir.Length; e += 28)
            {
                if (BitConverter.ToUInt32(dir, e + 12) != 2) continue;            // IMAGE_DEBUG_TYPE_CODEVIEW
                uint rva = BitConverter.ToUInt32(dir, e + 20), ptr = BitConverter.ToUInt32(dir, e + 24);
                var cv = new byte[24 + 260];
                f.Seek(ptr, SeekOrigin.Begin);
                int got = f.Read(cv, 0, cv.Length);
                if (got < 26 || cv[0] != 'R' || cv[1] != 'S' || cv[2] != 'D' || cv[3] != 'S') return null;
                int end = Array.IndexOf(cv, (byte)0, 24, got - 24);                // the pdb path, with its terminating zero
                if (end < 0) return null;
                return new PeIdentity(f.Length, stamp, imageSize, ptr, rva, Convert.ToHexString(cv, 0, end + 1).ToLowerInvariant());
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---- the scripts --------------------------------------------------------------------------------------------------

    public static string Entry(PeIdentity id) =>
        $"{{version = \"{id.Label}\", length = {id.Length}, stamp = 0x{id.Stamp:x}, image_size = 0x{id.ImageSize:x},\n" +
        $"         codeview_offset = 0x{id.CodeViewOffset:x}, codeview_rva = 0x{id.CodeViewRva:x},\n" +
        $"         identity = \"{id.IdentityHex}\"}}";

    public enum Status { AlreadyListed, Added, NotAPackScript }

    /// <summary>Add the build to a script's builds table. <paramref name="patched"/> is the new text when the status is <see cref="Status.Added"/>.</summary>
    public static Status Patch(string script, PeIdentity id, out string patched)
    {
        patched = script;
        if (!script.Contains("local builds = {") || !script.Contains("codeview_offset")) return Status.NotAPackScript;
        if (script.Contains(id.IdentityHex, StringComparison.OrdinalIgnoreCase)) return Status.AlreadyListed;
        var m = BuildsEnd.Match(script);
        if (!m.Success) return Status.NotAPackScript;
        var nl = m.Groups[2].Value.StartsWith("\r\n") ? "\r\n" : "\n";
        var entry = Entry(id).Replace("\n", nl);
        patched = script[..m.Groups[1].Index] + m.Groups[1].Value + "," + nl + "        " + entry + script[(m.Groups[1].Index + m.Groups[1].Length)..];
        return Status.Added;
    }

    public static string? SignaturesDir(string binaries)
    {
        foreach (var d in new[] { Path.Combine(binaries, "ue4ss", "UE4SS_Signatures"), Path.Combine(binaries, "UE4SS_Signatures") })
            if (Directory.Exists(d)) return d;
        return null;
    }

    /// <summary>Check every signature script against the game exe and add the exe's build where it is missing. Returns false when something needs attention.</summary>
    public static bool Run(string binaries, TextWriter output, bool dryRun = false)
    {
        var dir = SignaturesDir(binaries);
        if (dir == null) { output.WriteLine("  Signatures      no UE4SS_Signatures folder (this UE4SS has no signature pack): nothing to do"); return true; }
        var scripts = Directory.GetFiles(dir, "*.lua");
        var exe = Path.Combine(binaries, "SWZeroCompany.exe");
        var id = ReadIdentity(exe);
        if (id == null) { output.WriteLine($"  Signatures      cannot read the build identity of {exe}; left alone"); return true; }

        var work = new List<(string Path, string Text)>();
        int listed = 0, other = 0;
        foreach (var s in scripts)
        {
            var text = File.ReadAllText(s);
            switch (Patch(text, id, out var patched))
            {
                case Status.AlreadyListed: listed++; break;
                case Status.Added: work.Add((s, patched)); break;
                default: other++; break;
            }
        }
        if (work.Count == 0)
        {
            output.WriteLine($"  Signatures      this game build ({id.Length} bytes) is listed in {listed} signature script(s)" + (other > 0 ? $"; {other} other script(s) not recognised and left alone" : ""));
            return true;
        }
        if (dryRun)
        {
            output.WriteLine($"  Signatures      this game build is missing from {work.Count} script(s): " + string.Join(", ", work.Select(w => System.IO.Path.GetFileName(w.Path))));
            return false;
        }

        var root = Directory.GetParent(dir)!.FullName;
        var backup = System.IO.Path.Combine(root, BackupFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backup);
        foreach (var (path, _) in work) File.Copy(path, System.IO.Path.Combine(backup, System.IO.Path.GetFileName(path)));
        foreach (var (path, text) in work) File.WriteAllText(path, text, new UTF8Encoding(false));
        output.WriteLine($"  Signatures      added this game build ({id.Length} bytes, {id.Label}) to {work.Count} script(s); originals saved in {backup}");
        output.WriteLine("                  The pack's author has not checked this build. Only its identity list changed: UE4SS still has to match every function");
        output.WriteLine("                  pattern in the running game, and logs \"no validated loaded function entry\" if one really changed. Undo: zcdebug fix --undo-signatures");
        return true;
    }

    /// <summary>Put the newest backup of the signature scripts back.</summary>
    public static bool Undo(string binaries, TextWriter output)
    {
        var dir = SignaturesDir(binaries);
        if (dir == null) { output.WriteLine("no UE4SS_Signatures folder"); return false; }
        var root = Path.Combine(Directory.GetParent(dir)!.FullName, BackupFolder);
        var newest = Directory.Exists(root) ? Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal).LastOrDefault() : null;
        if (newest == null) { output.WriteLine("no signature backup to restore"); return false; }
        var files = Directory.GetFiles(newest, "*.lua");
        foreach (var f in files) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
        output.WriteLine($"restored {files.Length} signature script(s) from {newest}");
        return true;
    }
}
