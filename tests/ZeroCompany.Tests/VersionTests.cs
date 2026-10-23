using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ZeroCompany.Tests;

public class VersionTests
{
    static string Version()
    {
        var props = Path.Combine(Repo.Root, "Directory.Build.props");
        return XDocument.Load(props).Descendants("Version").Select(e => e.Value.Trim()).First();
    }

    [Fact]
    public void No_project_sets_its_own_version()
    {
        // the editor, zc and zcdebug must report the same version: only Directory.Build.props sets it
        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.csproj", SearchOption.AllDirectories))
            Assert.Empty(XDocument.Load(csproj).Descendants("Version"));
    }

    [Fact]
    public void Version_is_semver()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), Version());
    }

    [Fact]
    public void Changelog_documents_the_current_version()
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root, "CHANGELOG.md"));
        Assert.Contains($"## [{Version()}]", text);
    }
}
