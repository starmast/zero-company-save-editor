using System.IO.Compression;
using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.Sdk;

namespace ZeroCompany.Tests;

/// <summary>The debug report users paste into a bug report, and the bundle that adds the save file.</summary>
public class DebugReportTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "zc-report-" + Guid.NewGuid().ToString("N"));
    public DebugReportTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    ZcWorkspace Ws() => ZcWorkspace.Open(Path.Combine(_tmp, "data"), useSystemSaveDirs: false);

    string Sample()
    {
        var src = Repo.SampleSave();
        Skip.If(src == null, "no sample save");
        var path = Path.Combine(_tmp, Path.GetFileName(src!));
        File.Copy(src!, path);
        return path;
    }

    static Exception Thrown()
    {
        try { throw new InvalidOperationException("the save has no utility item to copy"); }
        catch (Exception e) { return e; }
    }

    [SkippableFact]
    public void The_report_has_the_error_the_pending_changes_and_the_save_as_counts()
    {
        var save = Sample();
        var pending = new[] { new PendingLine("Armory", "Add", null, null, "Add 1 x Bacta Stim (T4)"), new PendingLine("Stockpile", "Credits", 100, 5000, null) };
        var text = DebugReport.Build(Ws(), "test 1.0", save, pending, new DebugIncident("Applying failed", Thrown(), DateTime.Now));

        Assert.Contains("tool:    test 1.0", text);
        Assert.Contains("Applying failed", text);
        Assert.Contains("System.InvalidOperationException: the save has no utility item to copy", text);
        Assert.Contains(nameof(Thrown), text);                                                       // the stack trace
        Assert.Contains("== Pending changes (2)", text);
        Assert.Contains("- Armory: Add 1 x Bacta Stim (T4)", text);
        Assert.Contains("- Stockpile: Credits 100 -> 5000", text);
        Assert.Matches(@"game:      changelist \d+", text);
        Assert.Matches(@"inventory: resources \d+, utility items \d+ \(\d+ equipped\)", text);
        Assert.Contains("[ok] container", text);                                                     // the save checks
        Assert.Contains("[ok] data folder", text);                                                   // the setup checks

        var model = new SaveService(new Dictionary<string, string> { ["t"] = _tmp }, Path.Combine(_tmp, "b"), gameRunning: () => null)
            .Open("t", Path.GetFileName(save)).Model;
        foreach (var op in model.Operators.Values.Where(o => o.Name.Length > 3))
            Assert.DoesNotContain(op.Name, text);                                                    // counts, not names
        foreach (var guid in model.Operators.Keys) Assert.DoesNotContain(guid, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_report_works_without_a_save_and_hides_the_home_folder()
    {
        var text = DebugReport.Build(Ws(), "test 1.0", null);
        Assert.Contains("== Save\nnone open".Replace("\n", Environment.NewLine), text);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (_tmp.StartsWith(home, StringComparison.OrdinalIgnoreCase)) Assert.DoesNotContain(home, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("see ~/x and ~\\y", DebugReport.Redact($"see {home.Replace('\\', '/')}/x and {home}\\y"));
    }

    [SkippableFact]
    public void A_bundle_holds_the_report_the_save_and_the_crash_log()
    {
        var save = Sample();
        var ws = Ws();
        File.WriteAllText(Path.Combine(ws.Env.DataDir, "crash.log"), "---- earlier\nboom\n");
        var zipPath = Path.Combine(_tmp, "bundle.zip");
        Assert.Equal("the report, the save and the crash log", DebugReport.WriteBundle(ws, zipPath, "REPORT", save));

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(new[] { "crash.log", DebugReport.ReportEntry, Path.GetFileName(save) }.Order(StringComparer.Ordinal), zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using (var r = new StreamReader(zip.GetEntry(DebugReport.ReportEntry)!.Open())) Assert.Equal("REPORT", r.ReadToEnd());
        using (var s = zip.GetEntry(Path.GetFileName(save))!.Open())
        using (var m = new MemoryStream()) { s.CopyTo(m); Assert.Equal(File.ReadAllBytes(save), m.ToArray()); }
        Assert.False(File.Exists(zipPath + ".tmp"));
    }

    [Fact]
    public void A_bundle_without_a_save_or_crash_log_holds_just_the_report()
    {
        var zipPath = Path.Combine(_tmp, "bundle.zip");
        Assert.Equal("the report", DebugReport.WriteBundle(Ws(), zipPath, "REPORT", null));
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(DebugReport.ReportEntry, Assert.Single(zip.Entries).FullName);
    }
}
