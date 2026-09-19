using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Reaching a tab that is not the active one. Every other viewer tool acts on the ACTIVE tab, so with
/// three FITS files open an agent could read and steer exactly one of them — and blink, the comparison
/// a transient is found with, had no tool at all.
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

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    // ── switch_tab ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchTab_PassesKindAndIndexThrough()
    {
        string? gotKind = null;
        int? gotIndex = null;
        var tool = new SwitchTabTool((kind, index) =>
        {
            gotKind = kind; gotIndex = index;
            return Task.FromResult(new TabActionOutcome(true, kind, index, null));
        });

        var result = await tool.InvokeAsync(Args("""{"kind":"fits","index":2}"""), Ctx(), default);

        Assert.Equal("fits", gotKind);
        Assert.Equal(2, gotIndex);
        Assert.True(Payload<TabActionOutcome>(result).Ok);
    }

    /// <summary>Case is not a thing an agent should have to guess from a heading.</summary>
    [Fact]
    public async Task SwitchTab_KindIsCaseInsensitive()
    {
        var tool = new SwitchTabTool((kind, index) => Task.FromResult(new TabActionOutcome(true, kind, index, null)));
        var result = await tool.InvokeAsync(Args("""{"kind":"CUBE","index":0}"""), Ctx(), default);

        Assert.Equal("cube", Payload<TabActionOutcome>(result).Kind);
    }

    /// <summary>
    /// Notebooks are refused by name and told why: they have their own family that addresses a notebook
    /// by name, and a second way in by index would be two answers to one question.
    /// </summary>
    [Fact]
    public async Task SwitchTab_RefusesNotebook_AndSaysWhatToUseInstead()
    {
        var tool = new SwitchTabTool((_, _) => throw new Xunit.Sdk.XunitException("must not dispatch"));
        var failure = Failure(await tool.InvokeAsync(Args("""{"kind":"notebook","index":0}"""), Ctx(), default));

        Assert.Contains("fits", failure);
        Assert.Contains("cube", failure);
        Assert.Contains("name", failure);
    }

    [Fact]
    public async Task SwitchTab_RefusesANegativeIndex()
    {
        var tool = new SwitchTabTool((_, _) => throw new Xunit.Sdk.XunitException("must not dispatch"));
        Assert.Contains("0-based", Failure(await tool.InvokeAsync(Args("""{"kind":"fits","index":-1}"""), Ctx(), default)));
    }

    /// <summary>A missing index cannot mean anything here — unlike close_tab, where it means "active".</summary>
    [Fact]
    public async Task SwitchTab_RequiresAnIndex()
    {
        var tool = new SwitchTabTool((_, _) => throw new Xunit.Sdk.XunitException("must not dispatch"));
        Assert.Contains("index", Failure(await tool.InvokeAsync(Args("""{"kind":"fits"}"""), Ctx(), default)));
    }

    // ── close_tab ────────────────────────────────────────────────────────────

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

    /// <summary>The description warns that closing shifts the indices after it — a real footgun.</summary>
    [Fact]
    public void CloseTab_DescriptionWarnsThatIndicesShift()
    {
        var tool = new CloseTabTool((_, _) => Task.FromResult(new TabActionOutcome(true, "fits", null, null)));
        Assert.Contains("shifts the indices", tool.Descriptor.Description);
    }

    // ── blink_fits_tabs ──────────────────────────────────────────────────────

    [Fact]
    public async Task Blink_PassesBothIndices()
    {
        (int? A, int? B, bool Stop) got = default;
        var tool = new BlinkFitsTabsTool((a, b, stop) =>
        {
            got = (a, b, stop);
            return Task.FromResult(new BlinkOutcome(true, a, b, null));
        });

        var result = await tool.InvokeAsync(Args("""{"indexA":0,"indexB":2}"""), Ctx(), default);

        Assert.Equal((0, 2, false), got);
        Assert.True(Payload<BlinkOutcome>(result).Blinking);
    }

    /// <summary>Stopping needs no indices — there is only ever one blink running.</summary>
    [Fact]
    public async Task Blink_Stop_NeedsNoIndices()
    {
        var tool = new BlinkFitsTabsTool((a, b, stop) => Task.FromResult(new BlinkOutcome(false, a, b, null)));
        var result = await tool.InvokeAsync(Args("""{"stop":true}"""), Ctx(), default);

        Assert.False(Payload<BlinkOutcome>(result).Blinking);
    }

    /// <summary>Starting one with half the pair named is a mistake, and the refusal says what was wanted.</summary>
    [Theory]
    [InlineData("""{"indexA":0}""")]
    [InlineData("""{"indexB":1}""")]
    [InlineData("{}")]
    public async Task Blink_Start_NeedsBothIndices(string args)
    {
        var tool = new BlinkFitsTabsTool((_, _, _) => throw new Xunit.Sdk.XunitException("must not dispatch"));
        var failure = Failure(await tool.InvokeAsync(Args(args), Ctx(), default));

        Assert.Contains("indexA", failure);
        Assert.Contains("indexB", failure);
        Assert.Contains("stop", failure);
    }

    /// <summary>The host's own refusal reaches the caller rather than being flattened to "failed".</summary>
    [Fact]
    public async Task Blink_HostRefusal_ReachesTheCaller()
    {
        var tool = new BlinkFitsTabsTool((a, b, _) =>
            Task.FromResult(new BlinkOutcome(false, a, b, "both images need a valid WCS")));

        var outcome = Payload<BlinkOutcome>(
            await tool.InvokeAsync(Args("""{"indexA":0,"indexB":1}"""), Ctx(), default));

        Assert.False(outcome.Blinking);
        Assert.Contains("valid WCS", outcome.Message);
    }

    // ── list_open_tabs ───────────────────────────────────────────────────────

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

        Assert.Contains("switch_tab", description);
        Assert.Contains("close_tab", description);
        Assert.Contains("blink_fits_tabs", description);
    }
}
