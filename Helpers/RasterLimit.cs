namespace CanfarDesktop.Helpers;

/// <summary>
/// How large a figure can actually be rasterised, and what scale that really amounts to.
///
/// <para><c>RenderTargetBitmap</c> will not produce an image whose longest edge exceeds
/// <see cref="MaxEdge"/>. It does not fail when asked for more — it quietly renders smaller, keeping
/// the aspect. So a plate 1450 across, asked for 4x, comes back at 4096 rather than 5800: bigger than
/// the 2x export, but nothing like twice it. The app offered "2x or 4x" and delivered 2x and 2.8x,
/// which is exactly the "I see no difference between them" it was reported as.</para>
///
/// <para>The limit cannot be removed without rendering in tiles and stitching them. It CAN be told
/// the truth about, which is what this is for: the caller asks what it will really get, and can say
/// so rather than printing the number that was asked for.</para>
/// </summary>
public static class RasterLimit
{
    /// <summary>
    /// The longest edge <c>RenderTargetBitmap</c> will produce.
    ///
    /// Measured rather than assumed: a plate 1450 x 1285 asked for 4x came back 4096 x 3630 — the
    /// width pinned exactly here and the height reduced in proportion.
    /// </summary>
    public const int MaxEdge = 4096;

    /// <summary>What a rasterisation of this plate at this scale will really produce.</summary>
    /// <param name="Width">Pixels across, after any clamping.</param>
    /// <param name="Height">Pixels down, after any clamping.</param>
    /// <param name="Scale">
    /// The scale actually achieved. Equal to the request when nothing was clamped, and less than it
    /// otherwise — this is the number worth showing someone, not the one they asked for.
    /// </param>
    /// <param name="Clamped">Whether the request had to be reduced to fit.</param>
    public readonly record struct Raster(int Width, int Height, double Scale, bool Clamped);

    /// <summary>
    /// The largest whole-pixel size at or below <paramref name="requestedScale"/> that will rasterise.
    ///
    /// A plate with no measurable size yields nothing: there is no picture to scale.
    /// </summary>
    public static Raster Fit(double plateWidth, double plateHeight, double requestedScale)
    {
        if (!double.IsFinite(plateWidth) || !double.IsFinite(plateHeight) ||
            plateWidth <= 0 || plateHeight <= 0 || !double.IsFinite(requestedScale) || requestedScale <= 0)
            return new Raster(0, 0, 0, false);

        var longest = Math.Max(plateWidth, plateHeight);
        var achievable = Math.Min(requestedScale, MaxEdge / longest);

        // A plate already larger than the limit still has to produce something, so the scale can fall
        // below 1. Better a smaller figure than none.
        //
        // Rounded, not truncated, and then held at the limit: the scale that exactly fills the budget
        // is a division, so multiplying it back out lands a hair under a whole pixel and truncating
        // would throw away the last row for nothing.
        var width = Math.Clamp((int)Math.Round(plateWidth * achievable), 1, MaxEdge);
        var height = Math.Clamp((int)Math.Round(plateHeight * achievable), 1, MaxEdge);

        return new Raster(width, height, achievable, achievable < requestedScale - 1e-9);
    }

    /// <summary>
    /// The highest scale that will not be clamped for a plate this size — what an interface can offer
    /// without promising something it cannot produce.
    /// </summary>
    public static double LargestUsefulScale(double plateWidth, double plateHeight)
    {
        if (!double.IsFinite(plateWidth) || !double.IsFinite(plateHeight) ||
            plateWidth <= 0 || plateHeight <= 0)
            return 0;

        return MaxEdge / Math.Max(plateWidth, plateHeight);
    }
}
