using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>The tab-management tools: close_active_tab (ViewState) + list_open_tabs (read).</summary>
public class TabToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    [Fact]
    public async Task CloseActiveTab_PassesKindToDelegate()
    {
        string? got = null;
        var tool = new CloseActiveTabTool(kind => { got = kind; return Task.FromResult(new TabCloseOutcome(true, kind, null)); });

        var result = await tool.InvokeAsync(Args("""{"kind":"notebook"}"""), Ctx(), default);
        var json = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

        Assert.Equal("notebook", got);
        Assert.True(json.GetProperty("closed").GetBoolean());
        Assert.Equal("notebook", json.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CloseActiveTab_BadKind_IsInvalidArgument()
    {
        var tool = new CloseActiveTabTool(_ => Task.FromResult(new TabCloseOutcome(true, "x", null)));
        var result = await tool.InvokeAsync(Args("""{"kind":"portal"}"""), Ctx(), default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public void CloseActiveTab_IsViewState()
        => Assert.Equal(McpVerbClass.ViewState,
            new CloseActiveTabTool(_ => Task.FromResult(new TabCloseOutcome(true, "x", null))).VerbClass);

    [Fact]
    public async Task ListOpenTabs_ReturnsCounts()
    {
        var tool = new ListOpenTabsTool(() => Task.FromResult(new OpenTabsState(2, 1, 3)));
        var result = await tool.InvokeAsync(JsonValue.Null, Ctx(), default);
        var json = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

        Assert.Equal(2, json.GetProperty("notebooks").GetInt32());
        Assert.Equal(1, json.GetProperty("fitsViewers").GetInt32());
        Assert.Equal(3, json.GetProperty("cubes").GetInt32());
    }

    [Fact]
    public async Task ListOpenTabs_SurfacesPerTabDetail()
    {
        // The cube/FITS tab lists feed switch_cube_tab / switch_fits_tab.
        var tool = new ListOpenTabsTool(() => Task.FromResult(new OpenTabsState(0, 2, 1,
            CubeTabs: new[] { new ViewerTabInfo(0, "M31 cube", true) },
            FitsTabs: new[] { new ViewerTabInfo(0, "m51.fits", false), new ViewerTabInfo(1, "hst.fits", true) })));
        var result = await tool.InvokeAsync(JsonValue.Null, Ctx(), default);
        var json = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

        Assert.Equal("M31 cube", json.GetProperty("cubeTabs")[0].GetProperty("name").GetString());
        Assert.True(json.GetProperty("fitsTabs")[1].GetProperty("active").GetBoolean());
        Assert.Equal(1, json.GetProperty("fitsTabs")[1].GetProperty("index").GetInt32());
    }
}
