namespace ZeroCompany.App.Tests;

/// <summary>
/// Regenerates the README screenshots from the real UI. Skipped unless ZC_UPDATE_DOCS=1. Portraits are switched off (initials
/// only), so no game artwork ends up in the repository.
/// </summary>
public class DocsShotsTests
{
    static bool Enabled => Environment.GetEnvironmentVariable("ZC_UPDATE_DOCS") == "1";

    static string Docs => Path.Combine(Repo.Root, "docs", "screenshots");

    static void Save(TestRig rig, string name)
    {
        var from = rig.Shot("doc-" + name);
        Directory.CreateDirectory(Docs);
        File.Copy(from, Path.Combine(Docs, name + ".png"), overwrite: true);
    }

    [SkippableFact]
    public void Regenerate_readme_screenshots()
    {
        Skip.IfNot(Enabled, "set ZC_UPDATE_DOCS=1 to regenerate docs/screenshots");
        Skip.If(Repo.SampleSave() == null, "no sample save");
        using var rig = new TestRig();
        rig.Start(width: 1280, height: 900, portraits: false);
        var op = rig.Main.Session.Current!.View.Personnel.Roster.First(o => o.Bonds.Count > 0 && o.Abilities.Count > 0);

        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "bonds"));
        Save(rig, "personnel-bonds");
        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "focus"));
        Save(rig, "focus-tree");
        Headless.Run(() => rig.Main.Navigate("upgrades", "Facilities"));
        Save(rig, "upgrades");
        Headless.Run(() => rig.Main.Navigate("command"));
        Save(rig, "command");
    }
}
