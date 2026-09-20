namespace CanfarDesktop.Helpers;

/// <summary>
/// How much bigger an exported figure's picture is than the one on screen — the factor a mark's ink
/// is drawn at.
///
/// <para>A mark's stroke, label and leader are in DEVICE pixels, deliberately: a stroke that thickened
/// as you zoomed would turn the view into a blot. But "device pixels" means the screen's, and a figure
/// is not the screen. A cube volume figure is a fixed 1400px snapshot and a slice figure is framed at
/// 900–1600px, while the viewport they came from is whatever size the window happens to be — so marks
/// drawn at screen weight on that picture come out finer, relative to the figure, than they looked
/// when they were drawn.</para>
///
/// <para>Separate from the viewers because it is arithmetic with two decisions in it — what a
/// degenerate input means, and how far the factor may go — and neither of those should be discovered
/// by looking at a figure.</para>
/// </summary>
public static class PlateInk
{
    /// <summary>
    /// The narrowest and widest the factor may get.
    ///
    /// A viewport of a few pixels — a window mid-resize, a page not yet laid out — would otherwise
    /// give an enormous ratio and strokes that swallow the figure. The lower bound is below 1 because
    /// a picture genuinely smaller than the screen should thin its ink to match rather than carry
    /// screen-weight strokes over a shrunken image.
    /// </summary>
    public const double Min = 0.5;
    public const double Max = 4.0;

    /// <summary>
    /// The factor for a picture <paramref name="frameWidth"/> across that was <paramref name="onScreenWidth"/>
    /// across in the viewer. 1.0 — the screen's own ink — whenever either is not a usable measurement.
    /// </summary>
    public static double ScaleFor(double frameWidth, double onScreenWidth)
    {
        if (!double.IsFinite(frameWidth) || !double.IsFinite(onScreenWidth)) return 1.0;
        if (frameWidth <= 0 || onScreenWidth <= 0) return 1.0;

        var ratio = frameWidth / onScreenWidth;
        return double.IsFinite(ratio) ? Math.Clamp(ratio, Min, Max) : 1.0;
    }
}
