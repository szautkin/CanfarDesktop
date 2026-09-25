using System.Text.Json;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>
/// The shared half of <c>get_fits_image</c> and <c>get_cube_image</c>: the same arguments, the same
/// bounds, the same reply shape. Only the delegate and the wording differ, which is the whole reason
/// there is one class here rather than two that drift.
/// </summary>
public abstract class ViewerCaptureTool : IMcpTool
{
    private readonly Func<ViewerCaptureRequest, Task<ViewerCapture>> _capture;

    protected ViewerCaptureTool(Func<ViewerCaptureRequest, Task<ViewerCapture>> capture) => _capture = capture;

    /// <summary>Reading the screen changes nothing — no proposal, no gate beyond the agent-safe one.</summary>
    public McpVerbClass VerbClass => McpVerbClass.Read;
    public bool AgentSafe => true;

    public abstract ToolDescriptor Descriptor { get; }

    /// <summary>Whether this viewer offers the volume/slice choice. Only the cube does.</summary>
    protected virtual bool SupportsViewChoice => false;

    public async Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
    {
        Args args;
        try { args = DeserializeArgs(arguments); }
        catch (JsonException ex) { return ToolResult.Fail(new InvalidArgument(ex.Message)); }

        if (args.MaxPixels is <= 0)
            return ToolResult.Fail(new InvalidArgument("maxPixels must be a positive integer"));
        if (args.MaxBytes is <= 0)
            return ToolResult.Fail(new InvalidArgument("maxBytes must be a positive integer"));

        CubeCaptureView? view = null;
        if (SupportsViewChoice && !string.IsNullOrWhiteSpace(args.View))
        {
            view = args.View!.Trim().ToLowerInvariant() switch
            {
                "volume" => CubeCaptureView.Volume,
                "slice" => CubeCaptureView.Slice,
                _ => null,
            };
            if (view is null)
                return ToolResult.Fail(new InvalidArgument(
                    $"\"{args.View}\" is not a cube view; it takes \"volume\" or \"slice\" (omit it for whichever is on screen)"));
        }

        ViewerCapture capture;
        try
        {
            capture = await _capture(new ViewerCaptureRequest(args.MaxPixels, args.MaxBytes, view));
        }
        catch (McpToolException ex) { return ToolResult.Fail(ex.Reason); }
        catch (OperationCanceledException) { return ToolResult.Fail(new UpstreamTimeout()); }
        catch (Exception ex) { return ToolResult.Fail(new BackendError(ex.Message)); }

        if (!capture.Captured || capture.Data is null || capture.Data.Length == 0)
            return ToolResult.Fail(new BackendError(capture.Message ?? "nothing to capture"));

        return ToolResult.ImageResult(capture.Data, capture.MimeType, Caption(capture));
    }

    /// <summary>
    /// The JSON that rides beside the picture. Small on purpose: the image is the payload, and a
    /// caption that repeats it in words costs the caller context it would rather spend looking.
    /// </summary>
    private static string Caption(ViewerCapture capture)
        => JsonSerializer.Serialize(new
        {
            width = capture.Width,
            height = capture.Height,
            describes = capture.Describes,
            note = capture.Note,
            transform = capture.Transform is { } t
                ? new
                {
                    originX = t.OriginX,
                    originY = t.OriginY,
                    pixelsPerImagePixel = t.PixelsPerImagePixel,
                    howToUse = "imageX = originX + captureX / pixelsPerImagePixel (same for Y). Those are " +
                               "the 0-based coordinates the viewer's own tools take.",
                }
                : null,
        }, McpJson.Options);

    private static Args DeserializeArgs(JsonValue arguments)
    {
        if (arguments is JsonNull) return new Args();
        return JsonSerializer.Deserialize<Args>(arguments.ToJsonString(), McpJson.Options) ?? new Args();
    }

    public sealed record Args
    {
        public int? MaxPixels { get; init; }
        public int? MaxBytes { get; init; }
        public string? View { get; init; }
    }

    /// <summary>The bounds paragraph, identical in both descriptions because the bounds are identical.</summary>
    protected const string BoundsNote =
        "`maxPixels` caps the longest edge (default " + "1024" + ", never enlarged — a small cutout stays " +
        "small rather than being upscaled into detail the data does not have) and `maxBytes` caps the " +
        "encoded size so the reply fits a client's body limit. A 4000px capture costs roughly sixteen " +
        "times the context of a 1000px one and tells you nothing more.";
}

/// <summary>
/// <c>get_fits_image</c> — what the FITS viewer is SHOWING, not a fresh render of the file.
///
/// The distinction is the point. An agent asked to comment on what someone is looking at needs the
/// current zoom, pan, colormap, stretch and marks — the picture on the screen, including
/// the choices the person made to get there. A re-render of the file from scratch would answer a
/// question nobody asked.
///
/// The reply carries the transform that turns a position in the returned picture back into an image
/// pixel, which is what makes pointing at part of it possible.
/// </summary>
public sealed class GetFitsImageTool : ViewerCaptureTool
{
    public GetFitsImageTool(Func<ViewerCaptureRequest, Task<ViewerCapture>> capture) : base(capture) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_fits_image",
        "See what the FITS viewer is showing: the active tab's current zoom, pan, colormap, stretch " +
        "and marks, as an inline image. This is the PICTURE ON SCREEN, not a re-render of the " +
        "file — use it to look at what the person is looking at. The caption carries a `transform` that " +
        "converts a position in the returned image into a display pixel, which probe_fits_pixel, " +
        "fits_goto_coordinate and annotate_fits all take, so you can point at what you saw. " +
        "export_fits_figure is the other one: that writes a publication plate to a file. " + BoundsNote,
        """
        {"type":"object","properties":{
          "maxPixels":{"type":"integer","minimum":1,"maximum":4096,"description":"Cap on the longest edge. Default 1024."},
          "maxBytes":{"type":"integer","minimum":1,"description":"Cap on the encoded size in bytes."}
        },"additionalProperties":false}
        """);
}

/// <summary>
/// <c>get_cube_image</c> — what the cube viewer is showing, either the orbited 3D volume or the 2D
/// slice at the current channel.
///
/// The slice carries a transform, the same as the FITS viewer's. The volume does not: it is a
/// projection through a rotated box, and no single scale-and-offset describes where a voxel landed.
/// Saying so is better than handing back a transform that is subtly wrong.
/// </summary>
public sealed class GetCubeImageTool : ViewerCaptureTool
{
    public GetCubeImageTool(Func<ViewerCaptureRequest, Task<ViewerCapture>> capture) : base(capture) { }

    protected override bool SupportsViewChoice => true;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_cube_image",
        "See what the cube viewer is showing, as an inline image. `view` picks which picture: \"volume\" " +
        "is the 3D render as currently orbited, with its transfer function and marks; \"slice\" " +
        "is the 2D plane at the current channel. Omit it for whichever the viewer is showing. The slice's caption carries a `transform` for converting " +
        "a position in the returned image to a spatial pixel — the volume's does not, because a " +
        "projection through a rotated box is not a scale and an offset. export_cube_figure is the other " +
        "one: that writes a publication plate to a file. " + BoundsNote,
        """
        {"type":"object","properties":{
          "view":{"type":"string","enum":["volume","slice"],"description":"Which picture. Omit for whichever is on screen."},
          "maxPixels":{"type":"integer","minimum":1,"maximum":4096,"description":"Cap on the longest edge. Default 1024."},
          "maxBytes":{"type":"integer","minimum":1,"description":"Cap on the encoded size in bytes."}
        },"additionalProperties":false}
        """);
}
