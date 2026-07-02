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
        var tool = new ListVoSpacePathTool((req, _) =>
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
        var tool = new ListVoSpacePathTool((_, _) => Task.FromResult(new List<VoSpaceNode>()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"  "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Fail(result));
    }

    [Fact]
    public async Task ListVoSpacePath_AuthFailure_MapsToAuthRequired()
    {
        var tool = new ListVoSpacePathTool((_, _) =>
            throw new HttpRequestException("denied", null, HttpStatusCode.Unauthorized));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/private"}"""), Ctx, default);
        Assert.IsType<AuthRequired>(Fail(result));
    }

    // ---- read_vospace_file ----------------------------------------------------------------

    [Fact]
    public async Task ReadVoSpaceFile_Utf8_ReturnsText()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        var tool = new ReadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream(payload)));

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
        var tool = new ReadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream(payload)));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/big","maxBytes":4}"""), Ctx, default));

        Assert.Equal("abcd", ((JsonString)data["content"]!).Value);
        Assert.Equal(4, ((JsonInt)data["bytesRead"]!).Value);
        Assert.True(((JsonBool)data["truncated"]!).Value);
    }

    [Fact]
    public async Task ReadVoSpaceFile_Base64_ReturnsEncoded()
    {
        var payload = new byte[] { 0x00, 0xFF, 0x10, 0x42 }; // not valid utf8
        var tool = new ReadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream(payload)));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/bin","encoding":"base64"}"""), Ctx, default));

        Assert.Equal("base64", ((JsonString)data["encoding"]!).Value);
        Assert.Equal(Convert.ToBase64String(payload), ((JsonString)data["content"]!).Value);
        Assert.Equal(4, ((JsonInt)data["bytesRead"]!).Value);
    }

    [Fact]
    public async Task ReadVoSpaceFile_BinaryAsUtf8_ContentTypeMismatch()
    {
        var payload = new byte[] { 0x00, 0xFF, 0xFE }; // invalid utf8
        var tool = new ReadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream(payload)));

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/bin"}"""), Ctx, default);
        Assert.IsType<ContentTypeMismatch>(Fail(result));
    }

    [Fact]
    public async Task ReadVoSpaceFile_BadEncoding_InvalidArgument()
    {
        var tool = new ReadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new MemoryStream()));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/x","encoding":"hex"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Fail(result));
    }

    [Fact]
    public async Task ReadVoSpaceFile_AuthFailure_MapsToAuthRequired()
    {
        var tool = new ReadVoSpaceFileTool((_, _) =>
            throw new HttpRequestException("denied", null, HttpStatusCode.Forbidden));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/private"}"""), Ctx, default);
        Assert.IsType<AuthRequired>(Fail(result));
    }

    /// <summary>A read stream that blocks until its token is cancelled — models a stalled VOSpace body.</summary>
    private sealed class HangingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return 0;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
        public override void SetLength(long v) { }
        public override void Write(byte[] b, int o, int c) { }
    }

    [Fact]
    public async Task ReadVoSpaceFile_StalledBody_TimesOut_DoesNotHang()
    {
        // The download "succeeds" (returns a stream) but the body never arrives. The bounded read must
        // surface a typed timeout, not block. (Caller token cancels fast so the test is quick; in prod the
        // tool's own 30s bound covers it.)
        var tool = new ReadVoSpaceFileTool((_, _) => Task.FromResult<Stream>(new HangingStream()));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/stalled"}"""), Ctx, cts.Token);

        Assert.IsType<UpstreamTimeout>(Fail(result));
    }

    // ---- get_vospace_node -----------------------------------------------------------------

    [Fact]
    public async Task GetVoSpaceNode_ListsTheParentAndReturnsTheMatchingLeaf()
    {
        (string Path, int? Limit) captured = default;
        var tool = new GetVoSpaceNodeTool((req, _) =>
        {
            captured = req;
            return Task.FromResult(new List<VoSpaceNode>
            {
                new() { Name = "other.txt", Path = "/u/other.txt", Type = VoSpaceNodeType.DataNode },
                new() { Name = "image.fits", Path = "/u/image.fits", Type = VoSpaceNodeType.DataNode, SizeBytes = 4096, IsPublic = true },
            });
        });

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/u/image.fits"}"""), Ctx, default));

        Assert.Equal("u", captured.Path);        // the parent listing, not the leaf
        Assert.Equal(10_000, captured.Limit);    // far above macOS's 500 — avoids false not-found in big folders
        Assert.Equal("image.fits", ((JsonString)data["name"]!).Value);
        Assert.Equal("/u/image.fits", ((JsonString)data["path"]!).Value);
        Assert.Equal("DataNode", ((JsonString)data["type"]!).Value);
        Assert.Equal(4096, ((JsonInt)data["sizeBytes"]!).Value);
        Assert.True(((JsonBool)data["isPublic"]!).Value);
    }

    [Fact]
    public async Task GetVoSpaceNode_TopLevelNode_ListsTheRoot()
    {
        (string Path, int? Limit) captured = default;
        var tool = new GetVoSpaceNodeTool((req, _) =>
        {
            captured = req;
            return Task.FromResult(new List<VoSpaceNode> { new() { Name = "szautkin", Path = "/szautkin", Type = VoSpaceNodeType.Container } });
        });

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"path":"/szautkin"}"""), Ctx, default));

        Assert.Equal("/", captured.Path);
        Assert.Equal("szautkin", ((JsonString)data["name"]!).Value);
    }

    [Theory]
    [InlineData("""{"path":""}""")]
    [InlineData("""{"path":"/"}""")]
    [InlineData("""{"path":"  //  "}""")]
    public async Task GetVoSpaceNode_RootPath_InvalidArgument(string args)
    {
        var tool = new GetVoSpaceNodeTool((_, _) => Task.FromResult(new List<VoSpaceNode>()));
        var result = await tool.InvokeAsync(JsonValue.Parse(args), Ctx, default);
        Assert.IsType<InvalidArgument>(Fail(result));
    }

    [Fact]
    public async Task GetVoSpaceNode_AbsentLeaf_UnknownTarget()
    {
        var tool = new GetVoSpaceNodeTool((_, _) => Task.FromResult(new List<VoSpaceNode>
        {
            new() { Name = "present.txt", Path = "/u/present.txt", Type = VoSpaceNodeType.DataNode },
        }));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/u/absent.txt"}"""), Ctx, default);
        var reason = Assert.IsType<UnknownTarget>(Fail(result));
        Assert.Contains("vospace_node /u/absent.txt", reason.Description);
    }

    [Fact]
    public async Task GetVoSpaceNode_AuthFailure_MapsToAuthRequired()
    {
        var tool = new GetVoSpaceNodeTool((_, _) =>
            throw new HttpRequestException("denied", null, HttpStatusCode.Forbidden));
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"path":"/private/x"}"""), Ctx, default);
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

    // ---- get_fits_wcs must never hang -----------------------------------------------------
    // Regression for the smoke-test "WCS-solve bug" misdiagnosis: WcsInfo.FromHeader and the
    // accessors it uses (FitsHeader.Get*, Sexagesimal.Parse*) are pure, loop-free code. Guard
    // that the tool returns promptly on diverse / degenerate / exotic-projection headers — the
    // same shape that was reported (wrongly) as an infinite hang.

    private static FitsHeader HeaderFrom(params (string Key, string Value)[] cards)
    {
        var h = new FitsHeader();
        foreach (var (k, v) in cards) h.Add(new FitsCard(k, v, ""));
        return h;
    }

    public static IEnumerable<(string Name, FitsHeader Header)> WcsHeaderVariants()
    {
        yield return ("IRIS SFL projection", HeaderFrom(
            ("NAXIS", "2"), ("NAXIS1", "500"), ("NAXIS2", "500"),
            ("CRPIX1", "250"), ("CRPIX2", "250"), ("CRVAL1", "266.4"), ("CRVAL2", "-28.9"),
            ("CD1_1", "-0.025"), ("CD1_2", "0"), ("CD2_1", "0"), ("CD2_2", "0.025"),
            ("CTYPE1", "RA---SFL"), ("CTYPE2", "DEC--SFL")));
        yield return ("CDELT + CROTA2", HeaderFrom(
            ("NAXIS", "2"), ("NAXIS1", "500"), ("NAXIS2", "500"),
            ("CRPIX1", "250"), ("CRPIX2", "250"), ("CRVAL1", "10"), ("CRVAL2", "20"),
            ("CDELT1", "-0.0125"), ("CDELT2", "0.0125"), ("CROTA2", "33.3"),
            ("CTYPE1", "RA---TAN"), ("CTYPE2", "DEC--TAN")));
        yield return ("Degenerate CD -> legacy RA/DEC", HeaderFrom(
            ("NAXIS", "2"), ("NAXIS1", "256"), ("NAXIS2", "256"),
            ("CD1_1", "0"), ("CD1_2", "0"), ("CD2_1", "0"), ("CD2_2", "0"),
            ("RA", "17:45:40.04"), ("DEC", "-29:00:28.1"), ("SECPIX", "1.5")));
        yield return ("No WCS keywords", HeaderFrom(("NAXIS", "2"), ("NAXIS1", "10"), ("NAXIS2", "10")));
        yield return ("Large CD values", HeaderFrom(
            ("NAXIS", "2"), ("NAXIS1", "10"), ("NAXIS2", "10"),
            ("CRPIX1", "5"), ("CRPIX2", "5"), ("CRVAL1", "0"), ("CRVAL2", "0"),
            ("CD1_1", "1e100"), ("CD1_2", "1e100"), ("CD2_1", "1e100"), ("CD2_2", "1e100"),
            ("CTYPE1", "RA---TAN"), ("CTYPE2", "DEC--TAN")));
    }

    [Fact]
    public async Task GetFitsWcs_ReturnsPromptly_NeverHangs()
    {
        foreach (var (name, header) in WcsHeaderVariants())
        {
            var tool = new GetFitsWcsTool(_ => Task.FromResult(new List<FitsHeader> { header }));
            var invoke = tool.InvokeAsync(JsonValue.Parse("""{"localPath":"x.fits"}"""), Ctx, default);
            var done = await Task.WhenAny(invoke, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(done == invoke, $"get_fits_wcs hung on header variant '{name}'");
            Assert.IsType<DataResult>(await invoke);
        }
    }
}