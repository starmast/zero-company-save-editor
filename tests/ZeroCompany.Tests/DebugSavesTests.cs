using System.IO.Compression;
using System.Text;
using ZeroCompany.DebugCli;

namespace ZeroCompany.Tests;

/// <summary><c>zcdebug saves</c>: names as <c>load</c> wants them, the in-game name from the header, and the "game lists it" marker.</summary>
public class DebugSavesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "zc-saves-" + Guid.NewGuid().ToString("N"));
    public DebugSavesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A save-shaped zip whose header says <paramref name="comment"/> (only the write time is set: creation times are not settable everywhere).</summary>
    string MakeSave(string name, string comment, DateTime created, DateTime modified, bool bom = false)
    {
        var path = Path.Combine(_dir, name + ".sav");
        using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var json = $"{{\r\n\t\"comment\": \"{comment}\",\r\n\t\"gameMode\": \"Tactical\"\r\n}}";
            using var s = z.CreateEntry("SaveGameInfoJSON.txt").Open();
            if (bom) s.Write(new byte[] { 0xFF, 0xFE });
            s.Write(Encoding.Unicode.GetBytes(json));
        }
        File.SetLastWriteTime(path, modified);
        return path;
    }

    static string Run(string dir, string filter = "", bool all = false, DateTime? start = null)
    {
        var sw = new StringWriter();
        Saves.List(dir, filter, all, start, sw);
        return sw.ToString();
    }

    [Fact]
    public void Lists_newest_first_with_the_name_the_load_command_wants_and_the_in_game_name()
    {
        var t = new DateTime(2026, 10, 14, 12, 0, 0);
        MakeSave("Older_2026.10.13", "Old one", t.AddDays(-1), t.AddDays(-1));
        MakeSave("Newer_2026.10.14", "New one", t.AddHours(-2), t, bom: true);
        var lines = Run(_dir).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("New one", lines[0]); Assert.EndsWith("Newer_2026.10.14", lines[0].TrimEnd());   // no .sav
        Assert.Contains("Old one", lines[1]);
        Assert.DoesNotContain("listed", string.Join("", lines)); Assert.DoesNotContain("NEW", string.Join("", lines));   // game not running: nothing marked
    }

    [Fact]
    public void Marks_files_created_after_the_game_started_as_new_and_keeps_overwritten_ones_listed()
    {
        var start = new DateTime(2026, 10, 14, 12, 0, 0);
        var entries = new[]
        {
            new Saves.Entry("Known", "Known save", "Tactical", start.AddHours(-3), start.AddHours(-3)),
            new Saves.Entry("Overwritten", "Overwritten slot", "Tactical", start.AddMinutes(30), start.AddHours(-1)),   // created before, written after: still on the game's list
            new Saves.Entry("Fresh", "Fresh copy", "Tactical", start.AddMinutes(10), start.AddMinutes(10)),
        };
        var sw = new StringWriter();
        Saves.List(entries, _dir, "", false, start, sw);
        var text = sw.ToString();
        string Line(string n) => text.Split('\n').First(l => l.TrimEnd().EndsWith(n));
        Assert.Contains("listed", Line("Known"));
        Assert.Contains("listed", Line("Overwritten"));
        Assert.Contains("NEW", Line("Fresh"));
        Assert.Contains("restarts", text);                                                         // the explanation appears when something is new
    }

    [Fact]
    public void Filters_by_name_or_in_game_name_and_skips_files_that_are_not_saves()
    {
        var t = DateTime.Now;
        MakeSave("A_file", "Hawks edit", t, t);
        MakeSave("B_file", "Something else", t, t);
        File.WriteAllText(Path.Combine(_dir, "SharedGameSettings.sav"), "not a zip");
        using (var z = ZipFile.Open(Path.Combine(_dir, "NoHeader.sav"), ZipArchiveMode.Create)) z.CreateEntry("SaveGame");
        Assert.Equal(2, Run(_dir).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        var hawks = Run(_dir, "hawks");
        Assert.Contains("A_file", hawks); Assert.DoesNotContain("B_file", hawks);
        Assert.Contains("B_file", Run(_dir, "b_file"));
        Assert.Contains("no saves match", Run(_dir, "nothing like this"));
    }

    [Fact]
    public void Shows_twenty_unless_asked_for_all()
    {
        var t = DateTime.Now;
        for (int i = 0; i < 25; i++) MakeSave($"S{i:00}", $"Save {i}", t.AddMinutes(-i), t.AddMinutes(-i));
        var some = Run(_dir).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(21, some.Length);
        Assert.Contains("5 more", some[^1]);
        Assert.Equal(25, Run(_dir, all: true).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void A_missing_folder_is_reported_not_a_crash()
    {
        var sw = new StringWriter();
        Assert.Equal(1, Saves.List(Path.Combine(_dir, "nope"), "", false, null, sw));
        Assert.Contains("no saves", sw.ToString());
    }
}
