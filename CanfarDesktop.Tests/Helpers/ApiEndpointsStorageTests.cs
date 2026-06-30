using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>SCI-12-3: ARC storage URLs are scope-aware — a "projects/&lt;group&gt;/…" path targets the
/// shared group tree, while every other path stays byte-identical to the long-standing personal
/// "home/" form (so existing home navigation/upload/download is provably unchanged).</summary>
public class ApiEndpointsStorageTests
{
    private static ApiEndpoints E() => new();

    // ── home paths are byte-identical to before the scope-aware refactor ──

    [Fact]
    public void HomePaths_AreByteIdentical()
    {
        var e = E();
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/home/szautkin/folder", e.StorageNodeUrl("szautkin/folder"));
        Assert.Equal("https://ws-uv.canfar.net/arc/files/home/szautkin/x.fits", e.StorageFilesUrl("szautkin/x.fits"));
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/home/szautkin?limit=0", e.StorageUrl("szautkin"));
        Assert.Equal("vos://cadc.nrc.ca~arc/home/szautkin/folder", e.VoSpaceNodeUri("szautkin/folder"));
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/home", e.StorageBaseUrl); // health-display label
    }

    [Fact]
    public void HomeNodeList_KeepsDetailMaxAndLimit()
    {
        var e = E();
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/home/szautkin?detail=max", e.StorageNodeListUrl("szautkin"));
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/home/szautkin?detail=max&limit=50", e.StorageNodeListUrl("szautkin", 50));
    }

    // ── group (/projects) paths route to the group tree ──

    [Fact]
    public void ProjectsPaths_RouteToGroupTree()
    {
        var e = E();
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/projects/myteam/data", e.StorageNodeUrl("projects/myteam/data"));
        Assert.Equal("https://ws-uv.canfar.net/arc/files/projects/myteam/x.fits", e.StorageFilesUrl("projects/myteam/x.fits"));
        Assert.Equal("vos://cadc.nrc.ca~arc/projects/myteam/data", e.VoSpaceNodeUri("projects/myteam/data"));
        Assert.Equal("https://ws-uv.canfar.net/arc/nodes/projects/myteam?detail=max", e.StorageNodeListUrl("projects/myteam"));
    }

    // ── ScopeRootedPath edge cases ──

    [Theory]
    [InlineData("szautkin/a", "home/szautkin/a")]
    [InlineData("/szautkin/a", "home/szautkin/a")]    // leading slash tolerated (matches CreateFolder's Trim)
    [InlineData("", "home/")]
    [InlineData("projects/grp/a", "projects/grp/a")]
    [InlineData("/projects/grp/a", "projects/grp/a")]
    [InlineData("projects", "projects")]
    [InlineData("PROJECTS/grp", "PROJECTS/grp")]       // case-insensitive scope marker
    public void ScopeRootedPath_RootsByScope(string input, string expected)
        => Assert.Equal(expected, ApiEndpoints.ScopeRootedPath(input));
}
