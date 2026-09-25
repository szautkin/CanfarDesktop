using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Closing a tab that is not the active one. close_active_tab could only ever close what was in
/// front, so tidying up after a run that opened five files meant switching to each one first.
///
/// Switching is switch_fits_tab / switch_cube_tab, and blink is blink_fits_tabs; both are the
/// viewers' own tools and are covered with them.
/// </summary>
public class ViewerTabToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
    {
        var data = Assert.IsType<DataResult>(result);
        return JsonSerializer.Deserialize<T>(data.Json, McpJson.Options)!;
    }

    [Fact]
    public async Task CloseTab_WithAnIndex_ClosesThatOne()
    {
        int? gotIndex = -99;
        var tool = new CloseTabTool((kind, index) =>
        {
            gotIndex = index;
            return Task.FromResult(new TabActionOutcome(true, kind, index, null));
        });

        await tool.InvokeAsync(Args("""{"kind":"cube","index":3}"""), Ctx(), default);
        Assert.Equal(3, gotIndex);
    }

    /// <summary>Omitting the index means the active tab — what close_active_tab always did.</summary>
    [Fact]
    public async Task CloseTab_WithoutAnIndex_MeansTheActiveTab()
    {
        int? gotIndex = -99;
        var tool = new CloseTabTool((kind, index) =>
        {
            gotIndex = index;
            return Task.FromResult(new TabActionOutcome(true, kind, index, null));
        });

        await tool.InvokeAsync(Args("""{"kind":"fits"}"""), Ctx(), default);
        Assert.Null(gotIndex);
    }

    /// <summary>
    /// Notebooks are refused by name and told why: they have their own family that addresses a
    /// notebook by name, and a second way in by index would be two answers to one question.
    /// </summary>
    [Fact]
    public async Task CloseTab_RefusesNotebook_AndSaysWhatToUseInstead()
    {
        var tool = new CloseTabTool((_, _) => throw new Xunit.Sdk.XunitException("must not dispatch"));
        var failure = Assert.IsType<FailedResult>(
            await tool.InvokeAsync(Args("""{"kind":"notebook"}"""), Ctx(), default)).Reason.Description;

        Assert.Contains("fits", failure);
        Assert.Contains("cube", failure);
        Assert.Contains("name", failure);
    }

    /// <summary>The description warns that closing shifts the indices after it — a real footgun.</summary>
    [Fact]
    public void CloseTab_DescriptionWarnsThatIndicesShift()
    {
        var tool = new CloseTabTool((_, _) => Task.FromResult(new TabActionOutcome(true, "fits", null, null)));
        Assert.Contains("shifts the indices", tool.Descriptor.Description);
    }

    /// <summary>
    /// The counts alone were enough to know there was more than one tab and not enough to reach it.
    /// The per-viewer lists carry the index the other tools take, and a REAL path — a listing that
    /// showed the display name gave an agent something it could not reopen.
    /// </summary>
    [Fact]
    public async Task ListOpenTabs_CarriesIndexNamePathAndWhichIsActive()
    {
        var tool = new ListOpenTabsTool(() => Task.FromResult(new OpenTabsState(
            Notebooks: 0, FitsViewers: 2, Cubes: 0,
            FitsTabs:
            [
                new ViewerTabInfo(0, "a.fits", @"C:\data\a.fits", false),
                new ViewerTabInfo(1, "b.fits", @"C:\data\b.fits", true),
            ],
            CubeTabs: [])));

        var state = Payload<OpenTabsState>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(2, state.FitsViewers);
        Assert.Equal(2, state.FitsTabs!.Count);
        Assert.Equal(@"C:\data\b.fits", state.FitsTabs[1].Path);
        Assert.True(state.FitsTabs[1].Active);
        Assert.False(state.FitsTabs[0].Active);
    }

    [Fact]
    public void ListOpenTabs_DescriptionNamesTheIndexTheOtherToolsTake()
    {
        var tool = new ListOpenTabsTool(() => Task.FromResult(new OpenTabsState(0, 0, 0)));
        var description = tool.Descriptor.Description;

        Assert.Contains("switch_fits_tab", description);
        Assert.Contains("switch_cube_tab", description);
        Assert.Contains("close_tab", description);
        Assert.Contains("blink_fits_tabs", description);
    }
}
