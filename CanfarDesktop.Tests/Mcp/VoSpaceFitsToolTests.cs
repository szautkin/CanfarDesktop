using System.Net;
using System.Text;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class VoSpaceFitsToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonObject Data(ToolResult r) =>
        (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(Assert.IsType<DataResult>(r).Json));

    private static ToolFailureReason Fail(ToolResult r) => Assert.IsType<FailedResult>(r).Reason;

    // ---- list_vospace_path ----------------------------------------------------------------

    [Fact]
    public async Task ListVoSpacePath_ReturnsNodeSummaries()
    {
        (string Path, int? Limit) captured = default;
        var tool = new ListVoSpacePathTool(req =>
        {
            captured = req;
            return Task.FromResult(new List<VoSpaceNode>
            {
                new() { Name = "sub", Path = "/home/u/sub", Type = VoSpaceNodeType.Container },
                new() { Name = "image.fits", Path = "/home/u/image.fits", Type = VoSpaceNodeType.DataNode, SizeBytes = 4096, ContentType = "application/fits" },
            });
        });

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/home/u","limit":50}"""), Ctx, default));

        Assert.Equal("/home/u", captured.Path);
        Assert.Equal(50, captured.Limit);
        Assert.Equal(2, ((JsonInt)data["count"]!).Value);

        var nodes = ((JsonArray)data["nodes"]!).Items;
        var folder = (JsonObject)nodes[0];
        Assert.Equal("sub", ((JsonString)folder["name"]!).Value);
        Assert.Equal("Container", ((JsonString)folder["type"]!).Value);

        var file = (JsonObject)nodes[1];
        Assert.Equal("DataNode", ((JsonString)file["type"]!).Value);
        Assert.Equal(4096, ((JsonInt)file["sizeBytes"]!).Value);
    }

    [Fact]
    public async Task ListVoSpacePath_MissingPath_InvalidArgument()
    {
        var tool = new ListVoSpacePathTool(_ => Task.FromResult(new List<VoSpaceNode>()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"  "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Fail(result));
    }

    [Fact]
    public async Task ListVoSpacePath_AuthFailure_MapsToAuthRequired()
    {
        var tool = new ListVoSpacePathTool(_ =>
            throw new HttpRequestException("denied", null, HttpStatusCode.Unauthorized));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/private"}"""), Ctx, default);
        Assert.IsType<AuthRequired>(Fail(result));
    }

    // ---- read_vospace_file ----------------------------------------------------------------

    [Fact]
    public async Task ReadVoSpaceFile_Utf8_ReturnsText()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        var tool = new ReadVoSpaceFileTool(_ => Task.FromResult<Stream>(new MemoryStream(payload)));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/home/u/readme.txt"}"""), Ctx, default));

        Assert.Equal("utf8", ((JsonString)data["encoding"]!).Value);
        Assert.Equal("hello world", ((JsonString)data["content"]!).Value);
        Assert.Equal(payload.Length, ((JsonInt)data["bytesRead"]!).Value);
        Assert.False(((JsonBool)data["truncated"]!).Value);
    }

    [Fact]
    public async Task ReadVoSpaceFile_RespectsMaxBytesAndReportsTruncation()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghij"); // 10 bytes
        var tool = new ReadVoSpaceFileTool(_ => Task.FromResult<Stream>(new MemoryStream(payload)));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/big","maxBytes":4}"""), Ctx, default));

        Assert.Equal("abcd", ((JsonString)data["content"]!).Value);
        Assert.Equal(4, ((JsonInt)data["bytesRead"]!).Value);
        Assert.True(((JsonBool)data["truncated"]!).Value);
    }

    [Fact]
    public async Task ReadVoSpaceFile_Base64_ReturnsEncoded()
    {
        var payload = new byte[] { 0x00, 0xFF, 0x10, 0x42 }; // not valid utf8
        var tool = new ReadVoSpaceFileTool(_ => Task.FromResult<Stream>(new MemoryStream(payload)));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/bin","encoding":"base64"}"""), Ctx, default));

        Assert.Equal("base64", ((JsonString)data["encoding"]!).Value);
        Assert.Equal(Convert.ToBase64String(payload), ((JsonString)data["content"]!).Value);
        Assert.Equal(4, ((JsonInt)data["bytesRead"]!).Value);
    }

    [Fact]
    public async Task ReadVoSpaceFile_BinaryAsUtf8_ContentTypeMismatch()
    {
        var payload = new byte[] { 0x00, 0xFF, 0xFE }; // invalid utf8
        var tool = new ReadVoSpaceFileTool(_ => Task.FromResult<Stream>(new MemoryStream(payload)));

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/bin"}"""), Ctx, default);
        Assert.IsType<ContentTypeMismatch>(Fail(result));
    }

    [Fact]
    public async Task ReadVoSpaceFile_BadEncoding_InvalidArgument()
    {
        var tool = new ReadVoSpaceFileTool(_ => Task.FromResult<Stream>(new MemoryStream()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/x","encoding":"hex"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Fail(result));
    }

    [Fact]
    public async Task ReadVoSpaceFile_AuthFailure_MapsToAuthRequired()
    {
        var tool = new ReadVoSpaceFileTool(_ =>
            throw new HttpRequestException("denied", null, HttpStatusCode.Forbidden));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/private"}"""), Ctx, default);
        Assert.IsType<AuthRequired>(Fail(result));
    }

    // ---- get_fits_header ------------------------------------------------------------------

    private static FitsHeader HeaderWithWcs()
    {
        var h = new FitsHeader();
        h.Add(new FitsCard("SIMPLE", "T", "conforms to FITS standard"));
        h.Add(new FitsCard("BITPIX", "-32", "bits per pixel"));
        h.Add(new FitsCard("NAXIS", "2", "number of axes"));
        h.Add(new FitsCard("NAXIS1", "100", ""));
        h.Add(new FitsCard("NAXIS2", "100", ""));
        h.Add(new FitsCard("CRPIX1", "50.0", ""));
        h.Add(new FitsCard("CRPIX2", "50.0", ""));
        h.Add(new FitsCard("CRVAL1", "150.0", ""));
        h.Add(new FitsCard("CRVAL2", "2.5", ""));
        h.Add(new FitsCard("CD1_1", "-0.0002", ""));
        h.Add(new FitsCard("CD1_2", "0.0", ""));
        h.Add(new FitsCard("CD2_1", "0.0", ""));
        h.Add(new FitsCard("CD2_2", "0.0002", ""));
        h.Add(new FitsCard("CTYPE1", "RA---TAN", ""));
        h.Add(new FitsCard("CTYPE2", "DEC--TAN", ""));
        return h;
    }

    [Fact]
    public async Task GetFitsHeader_ReturnsCards()
    {
        string? capturedPath = null;
        var tool = new GetFitsHeaderTool(path =>
        {
            capturedPath = path;
            return Task.FromResult(new List<FitsHeader> { HeaderWithWcs() });
        });

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"localPath":"C:/data/img.fits"}"""), Ctx, default));

        Assert.Equal("C:/data/img.fits", capturedPath);
        Assert.Equal(0, ((JsonInt)data["hdu"]!).Value);
        Assert.Equal(15, ((JsonInt)data["count"]!).Value);

        var first = (JsonObject)((JsonArray)data["cards"]!).Items[0];
        Assert.Equal("SIMPLE", ((JsonString)first["keyword"]!).Value);
        Assert.Equal("T", ((JsonString)first["value"]!).Value);
    }

    [Fact]
    public async Task GetFitsHeader_HduOutOfRange_UnknownTarget()
    {
        var tool = new GetFitsHeaderTool(_ =>
            Task.FromResult(new List<FitsHeader> { HeaderWithWcs() }));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"localPath":"x.fits","hdu":3}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Fail(result));
    }

    [Fact]
    public async Task GetFitsHeader_UnparseableFile_UnknownTarget()
    {
        var tool = new GetFitsHeaderTool(_ =>
            throw new FileNotFoundException("missing"));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"localPath":"nope.fits"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Fail(result));
    }

    [Fact]
    public async Task GetFitsHeader_MissingPath_InvalidArgument()
    {
        var tool = new GetFitsHeaderTool(_ => Task.FromResult(new List<FitsHeader>()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"localPath":""}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Fail(result));
    }

    // ---- get_fits_wcs ---------------------------------------------------------------------

    [Fact]
    public async Task GetFitsWcs_ReturnsSolution()
    {
        var tool = new GetFitsWcsTool(_ =>
            Task.FromResult(new List<FitsHeader> { HeaderWithWcs() }));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"localPath":"img.fits"}"""), Ctx, default));

        Assert.True(((JsonBool)data["isValid"]!).Value);
        Assert.False(((JsonBool)data["isApproximate"]!).Value);
        Assert.Equal("RA---TAN", ((JsonString)data["cType1"]!).Value);
        Assert.Equal("Tan", ((JsonString)data["projection"]!).Value);
        // crVal1 is an integral double (150.0) -> serialized as JsonInt by the wire decoder.
        Assert.Equal(150, ((JsonInt)data["crVal1"]!).Value);
        // pixelScaleArcsec is present (non-null) because the WCS is valid.
        Assert.NotNull(data["pixelScaleArcsec"]);
        Assert.IsType<JsonDouble>(data["pixelScaleArcsec"]);
    }

    [Fact]
    public async Task GetFitsWcs_NoHdus_UnknownTarget()
    {
        var tool = new GetFitsWcsTool(_ => Task.FromResult(new List<FitsHeader>()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"localPath":"empty.fits"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Fail(result));
    }
}