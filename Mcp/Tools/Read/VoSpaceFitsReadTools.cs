using System.Net;
using System.Text;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>Compact view of a VOSpace/ARC node for MCP output.</summary>
public sealed record VoSpaceNodeSummary(string Name, string Path, string Type, long? SizeBytes, string? ContentType, DateTime? LastModified)
{
    public static VoSpaceNodeSummary From(VoSpaceNode n) =>
        new(n.Name, n.Path, n.Type.ToString(), n.SizeBytes, n.ContentType, n.LastModified);
}

/// <summary><c>list_vospace_path</c> — children of a VOSpace/ARC container path.</summary>
public sealed class ListVoSpacePathTool : JsonReadTool<ListVoSpacePathTool.Args, ListVoSpacePathTool.Output>
{
    private readonly Func<(string Path, int? Limit), CancellationToken, Task<List<VoSpaceNode>>> _list;

    public ListVoSpacePathTool(Func<(string Path, int? Limit), CancellationToken, Task<List<VoSpaceNode>>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_vospace_path",
        "List the files and folders under a VOSpace/ARC storage path (name, type, size).",
        """{"type":"object","properties":{"path":{"type":"string","description":"VOSpace/ARC path to list, e.g. /home/user or /projects/foo"},"limit":{"type":"integer","minimum":1,"description":"Optional maximum number of nodes to return"}},"required":["path"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Path))
            throw new McpToolException(new InvalidArgument("path is required"));
        if (args.Limit is <= 0)
            throw new McpToolException(new InvalidArgument("limit must be a positive integer"));

        List<VoSpaceNode> nodes;
        try
        {
            nodes = await _list((args.Path, args.Limit), ct);
        }
        catch (HttpRequestException ex) when (IsAuthFailure(ex))
        {
            throw new McpToolException(new AuthRequired());
        }

        var items = nodes.Select(VoSpaceNodeSummary.From).ToList();
        return new Output(args.Path, items.Count, items);
    }

    internal static bool IsAuthFailure(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public sealed record Args
    {
        public string Path { get; init; } = string.Empty;
        public int? Limit { get; init; }
    }

    public sealed record Output(string Path, int Count, IReadOnlyList<VoSpaceNodeSummary> Nodes);
}

/// <summary><c>read_vospace_file</c> — a bounded slice of a VOSpace/ARC file as text or base64.</summary>
public sealed class ReadVoSpaceFileTool : JsonReadTool<ReadVoSpaceFileTool.Args, ReadVoSpaceFileTool.Output>
{
    private const int DefaultMaxBytes = 65536;
    private const int MaxBytesCap = 1048576;

    private readonly Func<string, CancellationToken, Task<Stream>> _download;

    public ReadVoSpaceFileTool(Func<string, CancellationToken, Task<Stream>> download) => _download = download;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "read_vospace_file",
        "Read a bounded number of bytes from a VOSpace/ARC file and return them as utf8 text or base64.",
        """{"type":"object","properties":{"path":{"type":"string","description":"VOSpace/ARC file path to read"},"maxBytes":{"type":"integer","minimum":1,"maximum":1048576,"description":"Maximum bytes to read (default 65536, hard cap 1048576)"},"encoding":{"type":"string","enum":["utf8","base64"],"description":"How to return the bytes (default utf8)"}},"required":["path"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Path))
            throw new McpToolException(new InvalidArgument("path is required"));

        var encoding = (args.Encoding ?? "utf8").ToLowerInvariant();
        if (encoding is not ("utf8" or "base64"))
            throw new McpToolException(new InvalidArgument("encoding must be 'utf8' or 'base64'"));

        if (args.MaxBytes is <= 0)
            throw new McpToolException(new InvalidArgument("maxBytes must be a positive integer"));

        var maxBytes = Math.Min(args.MaxBytes ?? DefaultMaxBytes, MaxBytesCap);

        Stream stream;
        try
        {
            stream = await _download(args.Path, ct);
        }
        catch (HttpRequestException ex) when (ListVoSpacePathTool.IsAuthFailure(ex))
        {
            throw new McpToolException(new AuthRequired());
        }

        byte[] bytes;
        bool truncated;
        await using (stream)
        {
            // Read up to maxBytes + 1 so we can detect truncation without buffering the whole file.
            var buffer = new byte[maxBytes];
            var total = 0;
            while (total < maxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct);
                if (read == 0) break;
                total += read;
            }

            // Probe one more byte: if anything remains, the file was larger than maxBytes.
            var extra = new byte[1];
            truncated = total == maxBytes && await stream.ReadAsync(extra.AsMemory(0, 1), ct) > 0;

            bytes = total == buffer.Length ? buffer : buffer.AsSpan(0, total).ToArray();
        }

        string content;
        if (encoding == "base64")
        {
            content = Convert.ToBase64String(bytes);
        }
        else
        {
            // Reject binary payloads that are not valid UTF-8 text.
            try
            {
                content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new McpToolException(new ContentTypeMismatch("file is not valid utf8 text; request encoding 'base64' instead"));
            }
        }

        return new Output(args.Path, encoding, bytes.Length, truncated, content);
    }

    public sealed record Args
    {
        public string Path { get; init; } = string.Empty;
        public int? MaxBytes { get; init; }
        public string? Encoding { get; init; }
    }

    public sealed record Output(string Path, string Encoding, int BytesRead, bool Truncated, string Content);
}

/// <summary>A single parsed FITS header card for MCP output.</summary>
public sealed record FitsCardView(string Keyword, string Value, string Comment)
{
    public static FitsCardView From(FitsCard c) => new(c.Keyword, c.Value, c.Comment);
}

/// <summary><c>get_fits_header</c> — header cards of one HDU in a local FITS file.</summary>
public sealed class GetFitsHeaderTool : JsonReadTool<GetFitsHeaderTool.Args, GetFitsHeaderTool.Output>
{
    private readonly Func<string, Task<List<FitsHeader>>> _parseHeaders;

    public GetFitsHeaderTool(Func<string, Task<List<FitsHeader>>> parseHeaders) => _parseHeaders = parseHeaders;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_fits_header",
        "Read the FITS header cards (keyword/value/comment) of one HDU in a local FITS file.",
        """{"type":"object","properties":{"localPath":{"type":"string","description":"Local filesystem path to a FITS file"},"hdu":{"type":"integer","minimum":0,"description":"HDU index (default 0, the primary HDU)"}},"required":["localPath"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.LocalPath))
            throw new McpToolException(new InvalidArgument("localPath is required"));
        var hduIndex = args.Hdu ?? 0;
        if (hduIndex < 0)
            throw new McpToolException(new InvalidArgument("hdu must be a non-negative integer"));

        var header = await ResolveHeaderAsync(_parseHeaders, args.LocalPath, hduIndex);

        var cards = header.OrderedCards.Select(FitsCardView.From).ToList();
        return new Output(args.LocalPath, hduIndex, cards.Count, cards);
    }

    internal static async Task<FitsHeader> ResolveHeaderAsync(Func<string, Task<List<FitsHeader>>> parse, string localPath, int hduIndex)
    {
        List<FitsHeader> headers;
        try
        {
            headers = await parse(localPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
        {
            throw new McpToolException(new UnknownTarget($"could not parse FITS file: {localPath}"));
        }

        if (headers is null || headers.Count == 0)
            throw new McpToolException(new UnknownTarget($"no FITS HDUs found in: {localPath}"));
        if (hduIndex >= headers.Count)
            throw new McpToolException(new UnknownTarget($"HDU {hduIndex} not found ({headers.Count} HDU(s) in file)"));

        return headers[hduIndex];
    }

    public sealed record Args
    {
        public string LocalPath { get; init; } = string.Empty;
        public int? Hdu { get; init; }
    }

    public sealed record Output(string LocalPath, int Hdu, int Count, IReadOnlyList<FitsCardView> Cards);
}

/// <summary><c>get_fits_wcs</c> — the World Coordinate System solution of one HDU in a local FITS file.</summary>
public sealed class GetFitsWcsTool : JsonReadTool<GetFitsWcsTool.Args, GetFitsWcsTool.Output>
{
    private readonly Func<string, Task<List<FitsHeader>>> _parseHeaders;

    public GetFitsWcsTool(Func<string, Task<List<FitsHeader>>> parseHeaders) => _parseHeaders = parseHeaders;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_fits_wcs",
        "Read the World Coordinate System (WCS) solution of one HDU in a local FITS file (reference pixel/value, CD matrix, projection, pixel scale).",
        """{"type":"object","properties":{"localPath":{"type":"string","description":"Local filesystem path to a FITS file"},"hdu":{"type":"integer","minimum":0,"description":"HDU index (default 0, the primary HDU)"}},"required":["localPath"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.LocalPath))
            throw new McpToolException(new InvalidArgument("localPath is required"));
        var hduIndex = args.Hdu ?? 0;
        if (hduIndex < 0)
            throw new McpToolException(new InvalidArgument("hdu must be a non-negative integer"));

        var header = await GetFitsHeaderTool.ResolveHeaderAsync(_parseHeaders, args.LocalPath, hduIndex);
        var wcs = WcsInfo.FromHeader(header);

        return new Output(
            args.LocalPath, hduIndex, wcs.IsValid, wcs.IsApproximate,
            wcs.CType1, wcs.CType2, wcs.Proj.ToString(),
            wcs.CrPix1, wcs.CrPix2, wcs.CrVal1, wcs.CrVal2,
            wcs.Cd1_1, wcs.Cd1_2, wcs.Cd2_1, wcs.Cd2_2,
            wcs.IsValid ? wcs.PixelScaleArcsec : null,
            wcs.IsValid ? wcs.NorthAngle : null,
            wcs.IsValid ? wcs.HasParityFlip : null);
    }

    public sealed record Args
    {
        public string LocalPath { get; init; } = string.Empty;
        public int? Hdu { get; init; }
    }

    public sealed record Output(
        string LocalPath, int Hdu, bool IsValid, bool IsApproximate,
        string CType1, string CType2, string Projection,
        double CrPix1, double CrPix2, double CrVal1, double CrVal2,
        double Cd1_1, double Cd1_2, double Cd2_1, double Cd2_2,
        double? PixelScaleArcsec, double? NorthAngle, bool? HasParityFlip);
}