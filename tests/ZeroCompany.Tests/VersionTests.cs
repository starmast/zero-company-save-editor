using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ZeroCompany.Tests;

public class VersionTests
{
    static string Version()
    {
        var csproj = Path.Combine(Repo.Root, "src", "ZeroCompany.App", "ZeroCompany.App.csproj");
        return XDocument.Load(csproj).Descendants("Version").Select(e => e.Value.Trim()).First();
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
