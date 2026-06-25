using Xunit;
using CanfarDesktop.Mcp;

namespace CanfarDesktop.Tests.Mcp;

public class AppViewStateServiceTests
{
    [Fact]
    public void Default_IsLanding()
    {
        var snap = new AppViewStateService().Capture();
        Assert.Equal("landing", snap.Mode);
        Assert.Equal("Home", snap.ModeTitle);
        Assert.Null(snap.SearchFocusRA);
        Assert.Empty(snap.OpenFitsPaths);
    }

    [Fact]
    public void NotifyAgentActivity_RaisesEvent_WithToolAndModule()
    {
        var svc = new AppViewStateService();
        AppViewStateService.AgentActivitySignal? got = null;
        svc.AgentActivity += s => got = s;

        svc.NotifyAgentActivity("list_sessions", "portal");

        Assert.NotNull(got);
        Assert.Equal("list_sessions", got!.ToolName);
        Assert.Equal("portal", got.Module);
    }

    [Fact]
    public void NotifyAgentActivity_NullModule_ForMetaTools()
    {
        var svc = new AppViewStateService();
        AppViewStateService.AgentActivitySignal? got = null;
        svc.AgentActivity += s => got = s;

        svc.NotifyAgentActivity("describe_app", null);

        Assert.Equal("describe_app", got!.ToolName);
        Assert.Null(got.Module);
    }

    [Fact]
    public void Push_ReflectedInSnapshot()
    {
        var svc = new AppViewStateService();
        svc.SetMode("search", "Search");
        svc.SetSearchFocus(180.0, -0.5);
        svc.SetOpenFitsPaths(new[] { "/tmp/a.fits", "/tmp/b.fits" });

        var snap = svc.Capture();
        Assert.Equal("search", snap.Mode);
        Assert.Equal(180.0, snap.SearchFocusRA);
        Assert.Equal(-0.5, snap.SearchFocusDec);
        Assert.Equal(2, snap.OpenFitsPaths.Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(10.0, null)]
    [InlineData(null, 10.0)]
    public void SearchFocus_PartialOrAbsent_IsNull(double? ra, double? dec)
    {
        var svc = new AppViewStateService();
        svc.SetSearchFocus(12.0, 34.0); // set, then clear via a partial/absent push
        svc.SetSearchFocus(ra, dec);

        var snap = svc.Capture();
        Assert.Null(snap.SearchFocusRA);
        Assert.Null(snap.SearchFocusDec);
    }
}
