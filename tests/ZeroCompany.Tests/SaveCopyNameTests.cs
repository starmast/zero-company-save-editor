using System.Security.Cryptography;
using ZeroCompany.Core.Save;

namespace ZeroCompany.Tests;

/// <summary>"Save as copy" with a new name: the game lists a save by the comment stored in two header entries, and both must change.</summary>
public class SaveCopyNameTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-copyname-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    (SaveService Svc, string Dir, string Name, FieldChange Change) NewRig()
    {
        var src = Repo.CombatSave();
        Skip.If(src == null, "no combat sample save");
        var dir = Path.Combine(_tmp, "saves"); Directory.CreateDirectory(dir);
        var name = Path.GetFileName(src!);
        File.Copy(src!, Path.Combine(dir, name));
        var svc = new SaveService(new Dictionary<string, string> { ["t"] = dir }, Path.Combine(_tmp, "backups"), gameRunning: () => null);
        svc.Open("t", name);
        var health = svc.Current!.Combat!.Units.First(u => u.Team == "Enemy").Attr("Health")!;
        return (svc, dir, name, new FieldChange(health.Id, health.Value - 1));
    }

    static SaveContainer Load(string path) => SaveContainer.Load(File.ReadAllBytes(path));
    static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [SkippableFact]
    public void A_named_copy_changes_the_name_in_both_header_entries_and_nothing_else()
    {
        var (svc, dir, name, change) = NewRig();
        var original = Load(Path.Combine(dir, name));
        var oldName = original.Comment;
        Assert.False(string.IsNullOrEmpty(oldName));
        var before = Sha(Path.Combine(dir, name));

        var res = svc.Apply(new[] { change }, force: true, asCopy: true, copyName: "  My edited copy  ");

        Assert.True(res.Copy);
        var copy = Load(Path.Combine(dir, res.Written));
        Assert.Equal("My edited copy", copy.Comment);                                   // trimmed, in the JSON entry
        var info = copy.Blobs["SaveGameInfo"];                                          // and in the binary one the game's list reads
        Assert.Contains("My edited copy", System.Text.Encoding.ASCII.GetString(info));
        Assert.DoesNotContain(oldName!, System.Text.Encoding.ASCII.GetString(info));
        Assert.Equal(before, Sha(Path.Combine(dir, name)));                             // the original is untouched
        foreach (var (entry, bytes) in original.Blobs.Where(b => b.Key is not ("SaveGameInfo" or "SaveGameInfoJSON.txt" or "SaveGame")))
            Assert.True(bytes.AsSpan().SequenceEqual(copy.Blobs[entry]), $"{entry} changed");
        Assert.Equal(original.SaveInfo()!.Count, copy.SaveInfo()!.Count);               // no other header field lost
        svc.Open("t", res.Written);                                                     // and it opens as a normal save
        Assert.Equal("My edited copy", svc.Current!.Container.Comment);
    }

    [SkippableFact]
    public void A_name_with_quotes_and_non_ASCII_text_survives_in_both_entries()
    {
        var (svc, dir, _, change) = NewRig();
        const string tricky = "Café \"two\" \\ 戦闘";
        var res = svc.Apply(new[] { change }, force: true, asCopy: true, copyName: tricky);
        var copy = Load(Path.Combine(dir, res.Written));
        Assert.Equal(tricky, copy.Comment);
        var wanted = new byte[4];                                                       // UTF-16 string: negative length, then the characters
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wanted, -(tricky.Length + 1));
        var expected = wanted.Concat(System.Text.Encoding.Unicode.GetBytes(tricky)).Concat(new byte[] { 0, 0 }).ToArray();
        Assert.True(copy.Blobs["SaveGameInfo"].AsSpan().IndexOf(expected) >= 0);
        svc.Open("t", res.Written);
    }

    [SkippableFact]
    public void No_name_or_a_blank_one_keeps_the_original_name()
    {
        var (svc, dir, name, change) = NewRig();
        var oldName = Load(Path.Combine(dir, name)).Comment;
        foreach (var blank in new string?[] { null, "", "   " })
        {
            var res = svc.Apply(new[] { change }, force: true, asCopy: true, copyName: blank);
            Assert.Equal(oldName, Load(Path.Combine(dir, res.Written)).Comment);
        }
    }

    [SkippableFact]
    public void Bad_names_are_refused_before_anything_is_written()
    {
        var (svc, dir, name, change) = NewRig();
        var files = Directory.GetFiles(dir).Length;
        Assert.Throws<EditException>(() => svc.Apply(new[] { change }, force: true, asCopy: true, copyName: new string('x', SaveService.MaxCopyName + 1)));
        Assert.Throws<EditException>(() => svc.Apply(new[] { change }, force: true, asCopy: true, copyName: "line\nbreak"));
        Assert.Throws<EditException>(() => svc.Apply(new[] { change }, force: true, asCopy: false, copyName: "not a copy"));   // renaming in place is not offered
        Assert.Equal(files, Directory.GetFiles(dir).Length);
        var ok = svc.Apply(new[] { change }, force: true, asCopy: true, copyName: new string('x', SaveService.MaxCopyName));
        Assert.Equal(new string('x', SaveService.MaxCopyName), Load(Path.Combine(dir, ok.Written)).Comment);
    }
}
