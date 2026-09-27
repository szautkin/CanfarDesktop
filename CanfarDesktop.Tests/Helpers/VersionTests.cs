using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// One version, stated three times: the package identity (what the app, describe_app and an agent's
/// handshake report), the assembly's, and the release notes being written. When they disagree, which
/// build is running cannot be told from what it says about itself.
/// </summary>
public class VersionTests
{
    private static string PackageVersion()
    {
        var manifest = XDocument.Load(RepoFiles.PathTo("Package.appxmanifest"));
        return manifest.Descendants().First(e => e.Name.LocalName == "Identity").Attribute("Version")!.Value;
    }

    [Fact]
    public void ThePackageAndTheAssembly_AreTheSameVersion()
    {
        var project = XDocument.Load(RepoFiles.PathTo("CanfarDesktop.csproj"));
        var assembly = project.Descendants("Version").Single().Value;

        Assert.Equal(PackageVersion(), assembly);
    }

    /// <summary>The release notes at the top of the changelog are for the version the package says it is.</summary>
    [Fact]
    public void TheNewestReleaseNotes_AreForThePackagesVersion()
    {
        var changelog = File.ReadAllText(RepoFiles.PathTo("CHANGELOG.md"));
        var newest = Regex.Match(changelog, @"^## \[(?<v>\d+\.\d+\.\d+)\]", RegexOptions.Multiline).Groups["v"].Value;

        Assert.Equal(newest + ".0", PackageVersion());
    }
}
