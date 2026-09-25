namespace CanfarDesktop.Helpers;

/// <summary>
/// Cutting a figure too large to rasterise in one go into pieces that can be.
///
/// <para><c>RenderTargetBitmap</c> will not produce an image past <see cref="RasterLimit.MaxEdge"/> on
/// its longest side, and it does not refuse a bigger request — it renders smaller in silence. That is
/// why a 4x export came back barely larger than a 2x one. The limit is on a single rasterisation, not
/// on the picture: render the figure in pieces that each fit, and the pieces assemble into the size
/// that was asked for.</para>
///
/// <para>This works out the pieces. It is arithmetic — where each tile sits and how big it is — and
/// the part most worth testing, because a tiling that is off by a pixel produces seams, and a tiling
/// that overlaps produces a figure that is subtly wrong in a way nobody will notice until it is in a
/// paper.</para>
/// </summary>
public static class TiledRaster
{
    /// <summary>
    /// A ceiling on the whole figure, not on one tile.
    ///
    /// Tiling removes the per-rasterisation limit but not the cost: the assembled image is held in
    /// memory uncompressed, at four bytes a pixel, twice over while it is written. Sixty-four
    /// megapixels is a quarter of a gigabyte a side of that — past any figure anyone puts in a paper,
    /// and short of the size where the app would simply fall over.
    /// </summary>
    public const long MaxTotalPixels = 64L * 1024 * 1024;

    /// <summary>One piece of the figure, in the assembled image's own pixels.</summary>
    public readonly record struct Tile(int X, int Y, int Width, int Height);

    /// <summary>
    /// How to render a plate of <paramref name="plateWidth"/> x <paramref name="plateHeight"/> at
    /// <paramref name="scale"/>.
    /// </summary>
    /// <param name="Width">The assembled figure's width.</param>
    /// <param name="Height">Its height.</param>
    /// <param name="Scale">
    /// The scale actually used. Below the request only when the whole figure would exceed
    /// <see cref="MaxTotalPixels"/> — the per-rasterisation limit no longer reduces it.
    /// </param>
    /// <param name="Tiles">The pieces, in reading order, together covering the figure exactly once.</param>
    public readonly record struct Plan(int Width, int Height, double Scale, IReadOnlyList<Tile> Tiles)
    {
        /// <summary>Whether this needs more than one rasterisation.</summary>
        public bool IsTiled => Tiles.Count > 1;

        /// <summary>Nothing to draw.</summary>
        public bool IsEmpty => Width < 1 || Height < 1 || Tiles.Count == 0;
    }

    /// <summary>
    /// Work out the pieces. An unusable plate or scale yields an empty plan rather than a guess.
    /// </summary>
    public static Plan For(double plateWidth, double plateHeight, double scale, int maxEdge = RasterLimit.MaxEdge)
    {
        if (!double.IsFinite(plateWidth) || !double.IsFinite(plateHeight) || !double.IsFinite(scale) ||
            plateWidth <= 0 || plateHeight <= 0 || scale <= 0 || maxEdge < 1)
            return new Plan(0, 0, 0, []);

        // Hold the whole figure to a size the machine can actually assemble. This is the only thing
        // that still reduces the scale; the per-rasterisation limit is handled by tiling.
        var wanted = scale;
        var totalAtScale = plateWidth * scale * plateHeight * scale;
        if (totalAtScale > MaxTotalPixels)
            scale *= Math.Sqrt(MaxTotalPixels / totalAtScale);

        var width = Math.Max(1, (int)Math.Round(plateWidth * scale));
        var height = Math.Max(1, (int)Math.Round(plateHeight * scale));

        var tiles = new List<Tile>();
        for (var y = 0; y < height; y += maxEdge)
        for (var x = 0; x < width; x += maxEdge)
            tiles.Add(new Tile(x, y, Math.Min(maxEdge, width - x), Math.Min(maxEdge, height - y)));

        return new Plan(width, height, Math.Min(scale, wanted), tiles);
    }
}
