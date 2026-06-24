using Xunit;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Tests.Mcp;

public class ClaudeConfigLocatorTests
{
    [Fact]
    public void Resolve_PrefersStoreClaudeContainer_WhenItHasAClaudeDir()
    {
        const string local = @"C:\local";
        const string roaming = @"C:\roaming";
        var pkg = Path.Combine(local, "Packages", "Claude_abc");
        var claudeDir = Path.Combine(pkg, "LocalCache", "Roaming", "Claude");

        var result = ClaudeConfigLocator.Resolve(
            local, roaming,
            packagesRoot => new[] { pkg },
            dir => dir == claudeDir);

        Assert.Equal(Path.Combine(claudeDir, "claude_desktop_config.json"), result);
    }

    [Fact]
    public void Resolve_FallsBackToRealRoaming_WhenNoStoreClaude()
    {
        var result = ClaudeConfigLocator.Resolve(
            @"C:\local", @"C:\roaming",
            _ => Array.Empty<string>(),
            _ => false);

        Assert.Equal(Path.Combine(@"C:\roaming", "Claude", "claude_desktop_config.json"), result);
    }

    [Fact]
    public void Resolve_FallsBackWhenStorePackageHasNoClaudeDirYet()
    {
        // A Claude_* package exists but its config dir hasn't been created — use the real %APPDATA%.
        var result = ClaudeConfigLocator.Resolve(
            @"C:\local", @"C:\roaming",
            _ => new[] { @"C:\local\Packages\Claude_abc" },
            _ => false);

        Assert.Equal(Path.Combine(@"C:\roaming", "Claude", "claude_desktop_config.json"), result);
    }
}
