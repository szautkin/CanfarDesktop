using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// export_fits_figure. The tool's job is to refuse a request that cannot mean anything and to hand a
/// well-formed one on; resolving which pixels a region covers is the page's, because only the page
/// knows what is on screen.
/// </summary>
public class FitsFigureToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    /// <summary>A tool that records what it was asked for and reports success.</summary>
    private static (ExportFitsFigureTool Tool, Func<FitsFigureRequest?> Seen) Recording()
    {
        FitsFigureRequest? seen = null;
        var tool = new ExportFitsFigureTool(request =>
        {
            seen = request;
            return Task.FromResult(new FitsFigureOutcome(true, request.Path, request.Format, request.Scale, "region", 2));
        });
        return (tool, () => seen);
    }

    // ── The path and the format ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFormatIsTakenFromThePathWhenUnsaid()
    {
        var (tool, seen) = Recording();

        await tool.InvokeAsync(Args("""{"path":"C:\\figs\\m31.pdf"}"""), Ctx(), default);
        Assert.Equal("pdf", seen()!.Format);

        await tool.InvokeAsync(Args("""{"path":"C:\\figs\\m31.png"}"""), Ctx(), default);
        Assert.Equal("png", seen()!.Format);
    }

    [Fact]
    public async Task AFormatThatContradictsTheExtensionIsRefused()
    {
        var (tool, _) = Recording();
        var message = Failure(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png","format":"pdf"}"""), Ctx(), default));

        Assert.Contains(".pdf", message);
    }

    [Theory]
    [InlineData("""{"path":"m31.png"}""", "absolute")]
    [InlineData("""{"path":"   "}""", "required")]
    [InlineData("""{"path":"C:\\figs\\m31.tiff"}""", "must end in")]
    public async Task APathThatCannotBeWrittenIsRefused(string args, string expected)
    {
        var (tool, _) = Recording();
        Assert.Contains(expected, Failure(await tool.InvokeAsync(Args(args), Ctx(), default)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task AScaleTheFigureDoesNotOfferIsRefused(int scale)
    {
        var (tool, _) = Recording();
        var message = Failure(await tool.InvokeAsync(
            Args($$"""{"path":"C:\\figs\\m31.png","scale":{{scale}}}"""), Ctx(), default));

        Assert.Contains("scale must be 1, 2 or 4", message);
    }

    [Fact]
    public async Task TheDefaultsAreTheOnesMostFiguresWant()
    {
        var (tool, seen) = Recording();
        await tool.InvokeAsync(Args("""{"path":"C:\\figs\\m31.png"}"""), Ctx(), default);

        var request = seen()!;
        Assert.Equal(2, request.Scale);                       // the size most figures actually want
        Assert.Equal(FigureRegionKind.View, request.RegionKind); // what the user is looking at
        Assert.True(request.Dark);
        Assert.True(request.ShowMarks);
        Assert.True(request.Annotate);
    }

    // ── The four ways to say which region ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("view", FigureRegionKind.View)]
    [InlineData("current", FigureRegionKind.View)]
    [InlineData("image", FigureRegionKind.Image)]
    [InlineData("frame", FigureRegionKind.Image)]
    public async Task AWholeViewOrWholeFrameNeedsNothingElse(string region, FigureRegionKind expected)
    {
        var (tool, seen) = Recording();
        await tool.InvokeAsync(Args($$"""{"path":"C:\\figs\\m31.png","region":"{{region}}"}"""), Ctx(), default);

        Assert.Equal(expected, seen()!.RegionKind);
    }

    [Fact]
    public async Task APixelBoxIsPassedThroughAsGiven()
    {
        var (tool, seen) = Recording();
        await tool.InvokeAsync(Args(
            """{"path":"C:\\figs\\m31.png","region":"box","x":100,"y":200,"width":512,"height":256}"""),
            Ctx(), default);

        var request = seen()!;
        Assert.Equal(FigureRegionKind.PixelBox, request.RegionKind);
        Assert.Equal(100, request.X);
        Assert.Equal(512, request.Width);
    }

    /// <summary>
    /// Saying "box" without a box is a mistake worth naming. Falling back to the current view would
    /// export a figure of something other than what was asked for — and look like it had worked.
    /// </summary>
    [Fact]
    public async Task ABoxWithoutABoxIsRefusedRatherThanFallingBack()
    {
        var (tool, _) = Recording();
        var message = Failure(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png","region":"box","x":10,"y":10}"""), Ctx(), default));

        Assert.Contains("needs x, y, width and height", message);
    }

    [Fact]
    public async Task ABoxWithNoAreaIsRefused()
    {
        var (tool, _) = Recording();
        var message = Failure(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png","region":"box","x":10,"y":10,"width":0,"height":10}"""),
            Ctx(), default));

        Assert.Contains("greater than zero", message);
    }

    [Fact]
    public async Task ASkyCircleIsPassedThroughAsGiven()
    {
        var (tool, seen) = Recording();
        await tool.InvokeAsync(Args(
            """{"path":"C:\\figs\\m31.png","region":"sky","raDeg":10.6847,"decDeg":41.2687,"radiusDeg":0.05}"""),
            Ctx(), default);

        var request = seen()!;
        Assert.Equal(FigureRegionKind.SkyCircle, request.RegionKind);
        Assert.Equal(10.6847, request.RaDeg);
        Assert.Equal(0.05, request.RadiusDeg);
    }

    [Theory]
    [InlineData("""{"path":"C:\\figs\\m31.png","region":"sky","raDeg":10,"decDeg":41}""", "needs raDeg, decDeg and radiusDeg")]
    [InlineData("""{"path":"C:\\figs\\m31.png","region":"sky","raDeg":400,"decDeg":41,"radiusDeg":0.1}""", "raDeg must be in")]
    [InlineData("""{"path":"C:\\figs\\m31.png","region":"sky","raDeg":10,"decDeg":120,"radiusDeg":0.1}""", "decDeg must be in")]
    [InlineData("""{"path":"C:\\figs\\m31.png","region":"sky","raDeg":10,"decDeg":41,"radiusDeg":0}""", "radiusDeg must be greater")]
    public async Task ASkyCircleThatIsNotOneIsRefused(string args, string expected)
    {
        var (tool, _) = Recording();
        Assert.Contains(expected, Failure(await tool.InvokeAsync(Args(args), Ctx(), default)));
    }

    [Fact]
    public async Task AMarkIsNamedByItsId()
    {
        var (tool, seen) = Recording();
        await tool.InvokeAsync(Args("""{"path":"C:\\figs\\m31.png","region":"mark","markId":"m1234"}"""), Ctx(), default);

        Assert.Equal(FigureRegionKind.Mark, seen()!.RegionKind);
        Assert.Equal("m1234", seen()!.MarkId);
    }

    [Fact]
    public async Task AMarkRegionWithoutAnIdSaysWhereToGetOne()
    {
        var (tool, _) = Recording();
        var message = Failure(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png","region":"mark"}"""), Ctx(), default));

        Assert.Contains("list_fits_annotations", message);
    }

    [Fact]
    public async Task AnUnknownRegionKindNamesTheOnesThatExist()
    {
        var (tool, _) = Recording();
        var message = Failure(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png","region":"lasso"}"""), Ctx(), default));

        Assert.Contains("view, image, box, sky, mark", message);
    }

    // ── The answer ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheOutcomeSaysWhatWasWrittenAndOfWhat()
    {
        var tool = new ExportFitsFigureTool(r =>
            Task.FromResult(new FitsFigureOutcome(true, r.Path, r.Format, r.Scale, "the view on screen", 3)));

        var outcome = Payload<FitsFigureOutcome>(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png","scale":4}"""), Ctx(), default));

        Assert.True(outcome.Exported);
        Assert.Equal(4, outcome.Scale);
        Assert.Equal("the view on screen", outcome.Region);
        Assert.Equal(3, outcome.Marks);
    }

    /// <summary>Nothing open is an answer, not a failure — the same bargain the other viewer tools make.</summary>
    [Fact]
    public async Task WithNoImageOpenItSaysSo()
    {
        var tool = new ExportFitsFigureTool(_ =>
            Task.FromResult(FitsFigureOutcome.Unavailable("no FITS image is open")));

        var outcome = Payload<FitsFigureOutcome>(await tool.InvokeAsync(
            Args("""{"path":"C:\\figs\\m31.png"}"""), Ctx(), default));

        Assert.False(outcome.Exported);
        Assert.Contains("no FITS image is open", outcome.Message);
    }

    [Fact]
    public void ItIsAViewStateToolAndAgentSafe()
    {
        var (tool, _) = Recording();

        Assert.Equal(McpVerbClass.ViewState, tool.VerbClass);
        Assert.True(tool.AgentSafe);
        Assert.Equal("export_fits_figure", tool.Descriptor.Name);
    }
}
