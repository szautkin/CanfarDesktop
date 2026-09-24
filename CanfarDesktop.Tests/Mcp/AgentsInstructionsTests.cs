using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Xunit;
using CanfarDesktop.Mcp.Config;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// AGENTS.md tells any assistant, on any machine, where Verbinal's bridge is and how to register it.
/// Nothing reads it but agents, so nothing else would notice when it stops being true: these hold it
/// to the manifest and to the code that registers and copies the bridge.
/// </summary>
public class AgentsInstructionsTests
{
    private static string Instructions => File.ReadAllText(RepoFiles.PathTo("AGENTS.md"));

    /// <summary>
    /// The package family name is the identity name plus a hash of the publisher — the same for every
    /// build, version and architecture, and changed only by a change of publisher, which would make
    /// Verbinal a different app. If that ever happens, the path every assistant was given goes with it.
    /// </summary>
    [Fact]
    public void ItNamesThePackageFamilyTheManifestProduces()
    {
        var identity = XDocument.Load(RepoFiles.PathTo("Package.appxmanifest")).Root!
            .Elements().Single(e => e.Name.LocalName == "Identity");
        var name = identity.Attribute("Name")!.Value;
        var family = $"{name}_{PublisherId(identity.Attribute("Publisher")!.Value)}";

        Assert.Contains(family, Instructions);
        Assert.Contains($"Get-AppxPackage -Name {name}", Instructions);
    }

    /// <summary>Where <see cref="McpBridgeLocator.ResolveStable"/> puts the bridge, under the package's LocalCache.</summary>
    [Fact]
    public void ItPointsWhereTheAppCopiesTheBridge()
        => Assert.Contains($@"\LocalCache\Verbinal\mcp-bridge\{McpBridgeLocator.BridgeExeName}", Instructions);

    /// <summary>The same name and argument the connect wizard registers, so either way of connecting agrees.</summary>
    [Fact]
    public void ItRegistersTheServerAsTheWizardDoes()
    {
        Assert.Contains($"`{ClaudeConfigMerge.ServerKey}`", Instructions);
        Assert.Contains($"[\"{string.Join("\", \"", ClaudeConfigMerge.DefaultArgs)}\"]", Instructions);
    }

    [Fact]
    public void ThePackageCarriesIt()
        => Assert.Contains(@"<Content Include=""AGENTS.md""", File.ReadAllText(RepoFiles.PathTo("CanfarDesktop.csproj")));

    /// <summary>
    /// Windows' publisher ID: the first eight bytes of the SHA-256 of the UTF-16 publisher string, in
    /// Crockford base32.
    /// </summary>
    private static string PublisherId(string publisher)
    {
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher)).AsSpan(0, 8);
        var bits = new StringBuilder();
        foreach (var b in hash) bits.Append(Convert.ToString(b, 2).PadLeft(8, '0'));
        bits.Append('0');   // 64 bits, padded to 13 groups of 5

        const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        var id = new StringBuilder();
        for (var i = 0; i < bits.Length; i += 5) id.Append(alphabet[Convert.ToInt32(bits.ToString(i, 5), 2)]);
        return id.ToString();
    }
}
