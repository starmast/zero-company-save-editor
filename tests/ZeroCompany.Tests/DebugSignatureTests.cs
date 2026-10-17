using System.Text;
using ZeroCompany.DebugCli;

namespace ZeroCompany.Tests;

/// <summary>`zcdebug fix` adds a new game build to the SWZC UE4SS signature scripts: reading the exe's identity and editing the builds table.</summary>
public class DebugSignatureTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("zc-sig-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    const string Guid = "95cbb367982c2dcc87556130fa3a7f05";
    const string Identity = "52534453" + Guid + "01000000" + "5465737" + "42e70646200";     // RSDS + guid + age 1 + "Test.pdb\0"

    /// <summary>A minimal 64-bit PE: one section, a debug directory with one CodeView entry.</summary>
    static byte[] FakeExe(uint stamp = 0x2c6a1fd5, uint imageSize = 0x107c8000)
    {
        var b = new byte[0x1000];
        void W16(int at, int v) => BitConverter.GetBytes((ushort)v).CopyTo(b, at);
        void W32(int at, uint v) => BitConverter.GetBytes(v).CopyTo(b, at);
        b[0] = (byte)'M'; b[1] = (byte)'Z'; W32(0x3c, 0x80);
        int nt = 0x80, opt = nt + 24;
        W32(nt, 0x00004550); W16(nt + 4, 0x8664); W16(nt + 6, 1); W32(nt + 8, stamp); W16(nt + 20, 240);
        W16(opt, 0x20b); W32(opt + 56, imageSize);
        W32(opt + 112 + 6 * 8, 0x1000); W32(opt + 112 + 6 * 8 + 4, 28);                     // debug directory at RVA 0x1000, one entry
        int sec = opt + 240;                                                                 // section: RVA 0x1000, raw size 0x1000, raw pointer 0x400
        W32(sec + 8, 0x1000); W32(sec + 12, 0x1000); W32(sec + 16, 0x1000); W32(sec + 20, 0x400);
        W32(0x400 + 12, 2); W32(0x400 + 16, 0x40); W32(0x400 + 20, 0x1100); W32(0x400 + 24, 0x500);   // type 2 = CodeView, data at file 0x500
        Encoding.ASCII.GetBytes("RSDS").CopyTo(b, 0x500);
        Convert.FromHexString(Guid).CopyTo(b, 0x504);
        W32(0x514, 1);
        Encoding.ASCII.GetBytes("Test.pdb\0").CopyTo(b, 0x518);
        return b.Concat(new byte[0x400]).ToArray();                                          // pad so the file is longer than the headers
    }

    const string Script =
        "-- pack script\nlocal label = \"X\"\nlocal function f()\n    local builds = {\n" +
        "        {version = \"196985\", length = 269863568, stamp = 0x29323a64, image_size = 0x1078b000,\n" +
        "         codeview_offset = 0xeb84420, codeview_rva = 0xeb85620,\n" +
        "         identity = \"52534453f58d2b49e67d7fa8c66e3c136ba4a8da0100000053575a65726f436f6d70616e792e70646200\"},\n" +
        "        {version = \"197649\", length = 269872272, stamp = 0x1fc2bc86, image_size = 0x1078c000,\n" +
        "         codeview_offset = 0xeb861a0, codeview_rva = 0xeb86ba0,\n" +
        "         identity = \"5253445369a4be52c2dd0d37915cfed2e6b034b60100000053575a65726f436f6d70616e792e70646200\"}\n" +
        "    }\n    local build_profile\nend\n";

    [Fact]
    public void The_build_identity_is_read_from_the_pe_headers()
    {
        var exe = Path.Combine(_dir, "SWZeroCompany.exe");
        File.WriteAllBytes(exe, FakeExe());
        var id = SignaturePack.ReadIdentity(exe)!;
        Assert.Equal(0x1400L, id.Length);
        Assert.Equal(0x2c6a1fd5u, id.Stamp);
        Assert.Equal(0x107c8000u, id.ImageSize);
        Assert.Equal(0x500u, id.CodeViewOffset);
        Assert.Equal(0x1100u, id.CodeViewRva);
        Assert.Equal(Identity, id.IdentityHex);

        File.WriteAllText(exe, "not an exe");
        Assert.Null(SignaturePack.ReadIdentity(exe));
        Assert.Null(SignaturePack.ReadIdentity(Path.Combine(_dir, "missing.exe")));
    }

    [Fact]
    public void A_new_build_is_added_to_the_builds_table_and_nothing_else_changes()
    {
        var exe = Path.Combine(_dir, "SWZeroCompany.exe");
        File.WriteAllBytes(exe, FakeExe());
        var id = SignaturePack.ReadIdentity(exe)!;

        Assert.Equal(SignaturePack.Status.Added, SignaturePack.Patch(Script, id, out var patched));
        Assert.Contains(Identity, patched);
        Assert.Contains("length = 5120", patched);
        Assert.Contains("0x2c6a1fd5", patched);
        Assert.Contains("197649", patched);                                   // the older builds stay
        Assert.EndsWith("    }\n    local build_profile\nend\n", patched);    // the code after the table is untouched
        Assert.StartsWith(Script[..Script.IndexOf("identity = \"5253445369a4", StringComparison.Ordinal)], patched);

        Assert.Equal(SignaturePack.Status.AlreadyListed, SignaturePack.Patch(patched, id, out var again));
        Assert.Equal(patched, again);
        Assert.Equal(SignaturePack.Status.NotAPackScript, SignaturePack.Patch("print('hello')", id, out _));

        Assert.Equal(SignaturePack.Status.Added, SignaturePack.Patch(Script.Replace("\n", "\r\n"), id, out var crlf));
        Assert.DoesNotContain("\n", crlf.Replace("\r\n", ""));                // line endings stay consistent
    }

    [Fact]
    public void Run_backs_up_then_patches_every_pack_script_and_undo_puts_them_back()
    {
        var bin = Path.Combine(_dir, "Win64");
        var sigs = Path.Combine(bin, "ue4ss", "UE4SS_Signatures");
        Directory.CreateDirectory(sigs);
        File.WriteAllBytes(Path.Combine(bin, "SWZeroCompany.exe"), FakeExe());
        File.WriteAllText(Path.Combine(sigs, "A.lua"), Script);
        File.WriteAllText(Path.Combine(sigs, "B.lua"), Script);
        File.WriteAllText(Path.Combine(sigs, "Other.lua"), "-- somebody else's signature\nreturn 1\n");

        var sw = new StringWriter();
        Assert.False(SignaturePack.Run(bin, sw, dryRun: true));               // a dry run only reports
        Assert.Equal(Script, File.ReadAllText(Path.Combine(sigs, "A.lua")));
        Assert.Contains("missing from 2 script(s)", sw.ToString());

        sw = new StringWriter();
        Assert.True(SignaturePack.Run(bin, sw));
        Assert.Contains(Identity, File.ReadAllText(Path.Combine(sigs, "A.lua")));
        Assert.Contains(Identity, File.ReadAllText(Path.Combine(sigs, "B.lua")));
        Assert.Equal("-- somebody else's signature\nreturn 1\n", File.ReadAllText(Path.Combine(sigs, "Other.lua")));    // not a pack script: untouched
        Assert.Contains("not checked this build", sw.ToString());
        var backup = Directory.GetDirectories(Path.Combine(bin, "ue4ss", SignaturePack.BackupFolder)).Single();
        Assert.Equal(Script, File.ReadAllText(Path.Combine(backup, "A.lua")));
        Assert.False(File.Exists(Path.Combine(backup, "Other.lua")));

        sw = new StringWriter();
        Assert.True(SignaturePack.Run(bin, sw));                              // second run: nothing to do
        Assert.Contains("is listed in 2", sw.ToString());
        Assert.Single(Directory.GetDirectories(Path.Combine(bin, "ue4ss", SignaturePack.BackupFolder)));

        Assert.True(SignaturePack.Undo(bin, new StringWriter()));
        Assert.Equal(Script, File.ReadAllText(Path.Combine(sigs, "A.lua")));
        Assert.Equal(Script, File.ReadAllText(Path.Combine(sigs, "B.lua")));
    }

    [Fact]
    public void Without_a_readable_exe_or_a_signature_folder_nothing_is_touched()
    {
        var bin = Path.Combine(_dir, "Win64");
        Directory.CreateDirectory(bin);
        var sw = new StringWriter();
        Assert.True(SignaturePack.Run(bin, sw));
        Assert.Contains("no UE4SS_Signatures folder", sw.ToString());

        var sigs = Path.Combine(bin, "ue4ss", "UE4SS_Signatures");
        Directory.CreateDirectory(sigs);
        File.WriteAllText(Path.Combine(sigs, "A.lua"), Script);
        File.WriteAllText(Path.Combine(bin, "SWZeroCompany.exe"), "");
        sw = new StringWriter();
        Assert.True(SignaturePack.Run(bin, sw));
        Assert.Contains("cannot read the build identity", sw.ToString());
        Assert.Equal(Script, File.ReadAllText(Path.Combine(sigs, "A.lua")));
        Assert.False(SignaturePack.Undo(Path.Combine(_dir, "none"), new StringWriter()));
    }
}
