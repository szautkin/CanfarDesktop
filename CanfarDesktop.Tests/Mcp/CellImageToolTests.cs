using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models.Notebook;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// get_cell_image, and the richTypes that tell an agent there is one to fetch.
///
/// The gap this closes: text alone cannot tell a figure from a printed number, because matplotlib's
/// plain-text fallback for a plot is the string "&lt;Figure size 640x480&gt;".
/// </summary>
public class CellImageToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    /// <summary>A one-pixel PNG, so the bytes coming back are checkable.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // ── The tool ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFigureComesBackAsAnImageNotAsText()
    {
        var tool = new GetCellImageTool((index, _) =>
            Task.FromResult(new NotebookCellImage(Png, "image/png", index)));

        var result = await tool.InvokeAsync(Args("""{"index":2}"""), Ctx(), default);

        // Image content, not a JSON string of base64: the protocol has a content type for pictures, and
        // an image handed back as text is one the client has to be told how to decode.
        var image = Assert.IsType<ImageToolResult>(result);
        Assert.Equal(Png, image.Data);
        Assert.Equal("image/png", image.MimeType);
        Assert.Contains("cell 2", image.Caption);
    }

    /// <summary>
    /// "There is no picture" carries the reason, because each reason has a different next step: run the
    /// cell, look at its text, or pick a different cell.
    /// </summary>
    [Theory]
    [InlineData("cell 3 has not been run yet")]
    [InlineData("cell 3 ran but produced no image")]
    [InlineData("cell 3 is a markdown cell, which produces no output")]
    public async Task NoImageIsAnswerdWithTheReason(string reason)
    {
        var tool = new GetCellImageTool((_, _) => Task.FromResult(NotebookCellImage.None(reason)));

        var result = await tool.InvokeAsync(Args("""{"index":3}"""), Ctx(), default);

        Assert.Contains(reason, Assert.IsType<FailedResult>(result).Reason.Description);
    }

    [Theory]
    [InlineData("{}", "index is required")]
    [InlineData("""{"index":-1}""", "index must be >= 0")]
    public async Task AnIndexThatCannotBeOneIsRefused(string args, string expected)
    {
        var tool = new GetCellImageTool((_, _) => Task.FromResult(new NotebookCellImage(Png, "image/png", 0)));

        Assert.Contains(expected, Assert.IsType<FailedResult>(await tool.InvokeAsync(Args(args), Ctx(), default)).Reason.Description);
    }

    [Fact]
    public async Task TheNotebookSelectorIsPassedThrough()
    {
        string? seen = null;
        var tool = new GetCellImageTool((index, notebook) =>
        {
            seen = notebook;
            return Task.FromResult(new NotebookCellImage(Png, "image/png", index));
        });

        await tool.InvokeAsync(Args("""{"index":0,"notebook":"nb-2"}"""), Ctx(), default);

        Assert.Equal("nb-2", seen);
    }

    [Fact]
    public void ItIsAReadAndAgentSafe()
    {
        var tool = new GetCellImageTool((_, _) => Task.FromResult(NotebookCellImage.None("x")));

        Assert.Equal(McpVerbClass.Read, tool.VerbClass);
        Assert.True(tool.AgentSafe);
        Assert.Equal("get_cell_image", tool.Descriptor.Name);
    }

    // ── richTypes ───────────────────────────────────────────────────────────────────────────────

    private static CellOutputViewModel Output(params (string Mime, string Value)[] data) => new(new CellOutput
    {
        OutputType = "display_data",
        Data = data.ToDictionary(d => d.Mime, d => JsonDocument.Parse($"\"{d.Value}\"").RootElement),
    });

    /// <summary>
    /// Richest first, and it is deliberately NOT html-first: nbconvert emits both text/html and
    /// image/png for a matplotlib figure, and preferring the html there describes the plot instead of
    /// showing it.
    /// </summary>
    [Fact]
    public void RichTypesArePicturesFirstThenDocumentsThenText()
    {
        var output = Output(("text/plain", "<Figure size 640x480>"), ("text/html", "<div/>"), ("image/png", "iVBOR"));

        Assert.Equal(["image/png", "text/html", "text/plain"], output.RichTypes);
    }

    [Fact]
    public void AnOutputWithNoDataHasNoRichTypes()
        => Assert.Empty(new CellOutputViewModel(new CellOutput { OutputType = "stream" }).RichTypes);

    /// <summary>A MIME type nobody planned for is still reported, rather than vanishing from the list.</summary>
    [Fact]
    public void AnUnknownMimeTypeIsStillListed()
    {
        var output = Output(("application/vnd.plotly.v1+json", "{}"), ("text/plain", "Figure"));

        Assert.Equal(["text/plain", "application/vnd.plotly.v1+json"], output.RichTypes);
    }

    // ── The MIME types that used to be dropped ──────────────────────────────────────────────────

    /// <summary>
    /// A library emitting SVG or LaTeX had its output silently dropped: the cell ran, said nothing, and
    /// looked like code that produced no result.
    /// </summary>
    [Fact]
    public void SvgMarkdownAndLatexAreAllCarriedNow()
    {
        var output = Output(
            ("image/svg+xml", "<svg/>"),
            ("text/markdown", "**bold**"),
            ("text/latex", "\\\\frac{a}{b}"));

        Assert.True(output.HasSvg);
        Assert.Equal("<svg/>", output.SvgContent);
        Assert.True(output.HasMarkdown);
        Assert.Equal("**bold**", output.MarkdownContent);
        Assert.True(output.HasLatex);
        Assert.Contains("frac", output.LatexContent);
    }

    [Fact]
    public void TheOnesThatAlreadyWorkedStillDo()
    {
        var output = Output(("text/html", "<b/>"), ("text/plain", "42"), ("image/png", "iVBOR"));

        Assert.True(output.HasHtml);
        Assert.True(output.HasImage);
        Assert.Equal("42", output.TextContent);
    }
}
