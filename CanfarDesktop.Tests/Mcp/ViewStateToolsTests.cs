using System.Text;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.ViewState;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ViewStateToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("test-client", Guid.Empty);

    private static JsonValue Args(string json) => JsonValue.Parse(json);

    // ── get_current_view ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentView_ReturnsSnapshotJson()
    {
        var snap = new AppViewSnapshot("search", "Search", true, "alice", 180.0, -0.5,
            new[] { "/tmp/a.fits" }, AgentsEnabled: true);
        var tool = new GetCurrentViewTool(() => Task.FromResult(snap));

        var result = await tool.InvokeAsync(JsonValue.Null, Ctx, default);

        var data = Assert.IsType<DataResult>(result);
        var doc = JsonDocument.Parse(Encoding.UTF8.GetString(data.Json)).RootElement;
        Assert.Equal("search", doc.GetProperty("mode").GetString());
        Assert.Equal("alice", doc.GetProperty("username").GetString());
        Assert.Equal(180.0, doc.GetProperty("searchFocusRA").GetDouble());
        Assert.Equal("/tmp/a.fits", doc.GetProperty("openFitsPaths")[0].GetString());
        Assert.True(doc.GetProperty("agentsEnabled").GetBoolean());
    }

    // ── get_preview_image ─────────────────────────────────────────────────────

    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private static GetPreviewImageTool Tool(
        IReadOnlyList<PreviewArtifact> previews,
        Func<Uri, int, Task<PreviewBytes>>? fetch = null)
        => new(
            _ => Task.FromResult(previews),
            fetch ?? ((_, _) => Task.FromResult(new PreviewBytes(PngBytes, "image/png"))));

    private static PreviewArtifact Png(string? band = null)
        => new(band, new Uri("https://www.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/preview"), "image/png", null, "prev.png");

    [Fact]
    public async Task PreviewImage_HappyPath_ReturnsImageWithCaption()
    {
        var tool = Tool(new[] { Png() });

        var result = await tool.InvokeAsync(Args("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default);

        var img = Assert.IsType<ImageToolResult>(result);
        Assert.Equal("image/png", img.MimeType);
        Assert.Equal(PngBytes, img.Data);
        var meta = JsonDocument.Parse(img.Caption!).RootElement;
        Assert.Equal("prev.png", meta.GetProperty("filename").GetString());
        Assert.Equal(PngBytes.Length, meta.GetProperty("byteSize").GetInt32());
    }

    [Fact]
    public async Task PreviewImage_EmptyPublisherId_InvalidArgument()
    {
        var result = await Tool(new[] { Png() }).InvokeAsync(Args("""{"publisherId":"  "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task PreviewImage_NoPreviews_PreviewNotFound()
    {
        var result = await Tool(Array.Empty<PreviewArtifact>()).InvokeAsync(Args("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default);
        Assert.IsType<PreviewNotFound>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task PreviewImage_BandMiss_PreviewNotFoundListsBands()
    {
        var tool = Tool(new[] { Png("G.MP9401") });
        var result = await tool.InvokeAsync(Args("""{"publisherId":"ivo://cadc/X","band":"R.MP9999"}"""), Ctx, default);
        var reason = Assert.IsType<PreviewNotFound>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Contains("G.MP9401", reason.Description);
    }

    [Fact]
    public async Task PreviewImage_DeclaredLengthOverCap_PreviewTooLarge()
    {
        var huge = new PreviewArtifact(null, new Uri("https://host/p.png"), "image/png", 5_000_000, "p.png");
        var result = await Tool(new[] { huge }).InvokeAsync(Args("""{"publisherId":"ivo://cadc/X","maxBytes":1000}"""), Ctx, default);
        Assert.IsType<PreviewTooLarge>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task PreviewImage_TextErrorBody_ContentTypeMismatch()
    {
        // A 403 error body shipped with an image content type must not pass as an image.
        var errorBody = Encoding.UTF8.GetBytes("<html>403 host_not_allowed</html>");
        var tool = Tool(new[] { Png() }, (_, _) => Task.FromResult(new PreviewBytes(errorBody, "image/png")));
        var result = await tool.InvokeAsync(Args("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default);
        Assert.IsType<ContentTypeMismatch>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task PreviewImage_FetchAuthFailure_AuthRequired()
    {
        var tool = Tool(new[] { Png() }, (_, _) => throw new PreviewFetchException(new AuthRequired()));
        var result = await tool.InvokeAsync(Args("""{"publisherId":"ivo://cadc/X"}"""), Ctx, default);
        Assert.IsType<AuthRequired>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── ImageMagic ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x42, 0x4D, 0x00 }, "image/bmp")]
    public void ImageMagic_KnownMagic_Wins(byte[] data, string expected)
        => Assert.Equal(expected, ImageMagic.ResolveMime(data, declared: null));

    [Fact]
    public void ImageMagic_UnknownBinary_WithDeclaredImage_AcceptsDeclared()
    {
        var jp2 = new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0xFF, 0x4F };
        Assert.Equal("image/jp2", ImageMagic.ResolveMime(jp2, "image/jp2"));
    }

    [Fact]
    public void ImageMagic_TextBody_WithDeclaredImage_Rejected()
        => Assert.Null(ImageMagic.ResolveMime(Encoding.UTF8.GetBytes("{\"error\":\"nope\"}"), "image/png"));
}
