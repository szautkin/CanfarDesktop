using CanfarDesktop.Helpers;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Which picture a cube capture is of — the two the cube viewer shows.</summary>
public enum CubeCaptureView
{
    /// <summary>The 3D volume as it is currently orbited.</summary>
    Volume,

    /// <summary>The 2D slice at the current channel.</summary>
    Slice,
}

/// <summary>
/// A request for a capture of what a viewer is showing. <see cref="View"/> is null for "whichever is
/// on screen", which is the honest default for a tool whose whole job is to show what is on screen.
/// </summary>
public sealed record ViewerCaptureRequest(int? MaxPixels, int? MaxBytes, CubeCaptureView? View = null);

/// <summary>
/// A capture, or the reason there isn't one.
///
/// <see cref="Transform"/> is what separates this from a screenshot: it is how a position in
/// <see cref="Data"/> becomes a display pixel of the image, and from there — through the WCS the
/// viewer already exposes — a sky coordinate. Null for the cube's volume view, where the picture is a
/// projection through a rotated box and no single scale-and-offset describes it.
/// </summary>
public sealed record ViewerCapture(
    bool Captured,
    byte[]? Data,
    string MimeType,
    int Width,
    int Height,
    CaptureTransform? Transform,
    string? Describes = null,
    string? Note = null,
    string? Message = null)
{
    public static ViewerCapture Unavailable(string message)
        => new(false, null, "image/png", 0, 0, null, null, null, message);
}
