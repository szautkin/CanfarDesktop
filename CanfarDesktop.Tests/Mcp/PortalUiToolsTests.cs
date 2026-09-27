using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>show_launch_form: the Portal's launch dialog, opened for the person to see, and never a launch.</summary>
public class PortalUiToolsTests
{
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static (ShowLaunchFormTool Tool, List<LaunchFormRequest> Asked) Tool()
    {
        var asked = new List<LaunchFormRequest>();
        return (new ShowLaunchFormTool(r => { asked.Add(r); return Task.FromResult(new LaunchFormShown(!r.Close, r.Tab ?? "standard")); }), asked);
    }

    [Fact]
    public async Task ItPassesOnTheTabAndImage_Trimmed_InTheFormsSpelling()
    {
        var (tool, asked) = Tool();

        await tool.InvokeAsync(JsonValue.Parse("""{"tab":" Advanced ","image":" images.canfar.net/me/mine:1 "}"""), Ctx(), default);

        Assert.Equal(new LaunchFormRequest("advanced", "images.canfar.net/me/mine:1", false), Assert.Single(asked));
    }

    [Fact]
    public async Task ATabThatIsNotOne_IsRefused_WithTheTabsThereAre()
    {
        var (tool, asked) = Tool();

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"tab":"batch"}"""), Ctx(), default);

        Assert.Contains("standard, advanced, headless", Assert.IsType<FailedResult>(result).Reason.Description);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Close_ClosesIt()
    {
        var (tool, asked) = Tool();

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"close":true}"""), Ctx(), default);

        Assert.True(Assert.Single(asked).Close);
        var shown = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
        Assert.False(shown.GetProperty("open").GetBoolean());
    }

    [Fact]
    public void ItOnlyShows_ItLaunchesNothing()
        => Assert.Equal(McpVerbClass.ViewState, Tool().Tool.VerbClass);

    [Theory]
    [InlineData("standard", 0)]
    [InlineData("Advanced", 1)]
    [InlineData(" headless ", 2)]
    [InlineData("batch", -1)]
    [InlineData(null, -1)]
    public void Tabs_AreInTheFormsOrder(string? tab, int index)
        => Assert.Equal(index, LaunchFormTabs.IndexOf(tab));
}
