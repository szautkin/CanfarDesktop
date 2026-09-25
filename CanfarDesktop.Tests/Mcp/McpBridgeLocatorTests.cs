using Xunit;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Which bridge path an assistant's client is given. A bridge that ships with the app is registered as
/// a copy at one fixed per-user path — never where the app keeps it, which a Store update or a Visual
/// Studio deploy replaces — and that copy has to be refreshable while the client is running it.
/// </summary>
public sealed class McpBridgeLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vb-bridge-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "app", "mcp-bridge", McpBridgeLocator.BridgeExeName);
    private string Stable => Path.Combine(_root, "LocalCache", "Verbinal", "mcp-bridge", McpBridgeLocator.BridgeExeName);

    public McpBridgeLocatorTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        File.WriteAllText(Source, "bridge 1.4.0");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string[] MovedAside()
        => Directory.Exists(Path.GetDirectoryName(Stable))
            ? Directory.GetFiles(Path.GetDirectoryName(Stable)!, McpBridgeLocator.BridgeExeName + ".old-*")
            : [];

    // ── Which bridge is copied ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Beside the app or in its mcp-bridge folder — the Store's WindowsApps folder and Visual Studio's
    /// AppX layout alike, which is where a registered bridge blocked every deploy (DEP0500).
    /// </summary>
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\CodeBG.Verbinal_1.4.0.0_x64__zjqkjyb5296v2", @"mcp-bridge\CanfarDesktop.McpBridge.exe")]
    [InlineData(@"C:\src\CanfarDesktop\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppX", @"mcp-bridge\CanfarDesktop.McpBridge.exe")]
    [InlineData(@"C:\apps\Verbinal", "CanfarDesktop.McpBridge.exe")]
    [InlineData(@"C:\apps\Verbinal", @"MCP-BRIDGE\canfardesktop.mcpbridge.exe")]
    public void ABridgeThatShipsWithTheAppIsCopied(string app, string bridge)
        => Assert.True(McpBridgeLocator.ShipsWithApp(Path.Combine(app, bridge), app));

    /// <summary>The bridge project's own build output is used in place: devs iterate on it.</summary>
    [Fact]
    public void TheBridgeProjectsOwnBuildIsNot()
        => Assert.False(McpBridgeLocator.ShipsWithApp(
            @"C:\src\CanfarDesktop\CanfarDesktop.McpBridge\bin\Debug\net8.0-windows\CanfarDesktop.McpBridge.exe",
            @"C:\src\CanfarDesktop\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AppX"));

    [Fact]
    public void ResolveStableHandsBackTheBridgeProjectsBuildAsItIs()
    {
        var app = Path.Combine(_root, "repo", "bin", "x64", "Debug", "AppX");
        var dev = Path.Combine(_root, "repo", "CanfarDesktop.McpBridge", "bin", "Debug", McpBridgeLocator.BridgeExeName);
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.GetDirectoryName(dev)!);
        File.WriteAllText(dev, "dev bridge");

        Assert.Equal(dev, McpBridgeLocator.ResolveStable(app));
    }

    // ── Refreshing the copy ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFirstRefreshPutsTheBridgeAtTheFixedPath()
    {
        Assert.Equal(Stable, McpBridgeLocator.Refresh(Source, Stable));
        Assert.Equal("bridge 1.4.0", File.ReadAllText(Stable));
    }

    [Fact]
    public void AnUpToDateCopyIsLeftAlone()
    {
        McpBridgeLocator.Refresh(Source, Stable);
        var written = File.GetLastWriteTimeUtc(Stable);

        Assert.Equal(Stable, McpBridgeLocator.Refresh(Source, Stable));
        Assert.Equal(written, File.GetLastWriteTimeUtc(Stable));
        Assert.Empty(MovedAside());
    }

    /// <summary>
    /// After an update, while the client is still running the old bridge: it cannot be overwritten,
    /// but it can be renamed — so the new one takes its place, and the client starts it next time.
    /// The handle stands in for a running exe: no writing through it, renaming allowed.
    /// </summary>
    [Fact]
    public void ACopyThatIsRunningIsReplacedAllTheSame()
    {
        McpBridgeLocator.Refresh(Source, Stable);
        File.WriteAllText(Source, "bridge 1.4.1, longer");

        using (new FileStream(Stable, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            Assert.Equal(Stable, McpBridgeLocator.Refresh(Source, Stable));
            Assert.Equal("bridge 1.4.1, longer", File.ReadAllText(Stable));
        }
    }

    /// <summary>What was moved aside goes once nothing runs it, on the next refresh.</summary>
    [Fact]
    public void BridgesMovedAsideAreDeletedOnceReleased()
    {
        McpBridgeLocator.Refresh(Source, Stable);
        var aside = Stable + ".old-0123456789abcdef";
        File.WriteAllText(aside, "bridge 1.3.3");

        McpBridgeLocator.Refresh(Source, Stable);

        Assert.Empty(MovedAside());
    }

    /// <summary>
    /// When the copy cannot be replaced, the client stays pointed at it — an older bridge still
    /// connects, and the install folder would break at the next update.
    /// </summary>
    [Fact]
    public void ACopyThatCannotBeReplacedIsStillTheOneRegistered()
    {
        McpBridgeLocator.Refresh(Source, Stable);
        File.WriteAllText(Source, "bridge 1.4.1, longer");

        using (new FileStream(Stable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(Stable, McpBridgeLocator.Refresh(Source, Stable));
            Assert.Equal("bridge 1.4.0", File.ReadAllText(Stable));
        }
    }

    [Fact]
    public void TheAppsOwnBridgeIsRegisteredOnlyWhenNoCopyCanBeMade()
    {
        var blocked = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(blocked, "a file where the folder would go");

        Assert.Equal(Source, McpBridgeLocator.Refresh(Source, Path.Combine(blocked, McpBridgeLocator.BridgeExeName)));
    }
}
