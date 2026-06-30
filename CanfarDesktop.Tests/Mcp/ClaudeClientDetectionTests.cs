using Xunit;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>Install-detection cores that drive the wizard's "Claude Desktop / Claude Code detected" cards.</summary>
public class ClaudeClientDetectionTests
{
    // ── Desktop ──

    [Fact]
    public void DetectDesktop_StorePackageWithClaudeDir_True()
    {
        var pkg = @"C:\local\Packages\Claude_abc";
        bool DirExists(string p) => p == Path.Combine(pkg, "LocalCache", "Roaming", "Claude");
        Assert.True(ClaudeClientDetection.DetectDesktop(@"C:\local", @"C:\roaming",
            root => new[] { pkg }, DirExists));
    }

    [Fact]
    public void DetectDesktop_TraditionalAppData_True()
    {
        bool DirExists(string p) => p == Path.Combine(@"C:\roaming", "Claude");
        Assert.True(ClaudeClientDetection.DetectDesktop(@"C:\local", @"C:\roaming",
            root => Array.Empty<string>(), DirExists));
    }

    [Fact]
    public void DetectDesktop_NothingPresent_False()
        => Assert.False(ClaudeClientDetection.DetectDesktop(@"C:\local", @"C:\roaming",
            root => Array.Empty<string>(), _ => false));

    // ── Code (CLI on PATH) ──

    [Theory]
    [InlineData("claude.exe")]
    [InlineData("claude.cmd")]
    [InlineData("claude")]
    public void DetectCode_LauncherOnPath_True(string exe)
    {
        var dir = @"C:\tools\claude";
        Assert.True(ClaudeClientDetection.DetectCode(new[] { @"C:\other", dir }, p => p == Path.Combine(dir, exe)));
    }

    [Fact]
    public void DetectCode_NotOnPath_False()
        => Assert.False(ClaudeClientDetection.DetectCode(new[] { @"C:\a", @"C:\b" }, _ => false));

    [Fact]
    public void DetectCode_EmptyAndBlankPathEntries_Tolerated()
        => Assert.False(ClaudeClientDetection.DetectCode(new[] { "", "   " }, _ => true /* never reached for blanks */ ));
}
