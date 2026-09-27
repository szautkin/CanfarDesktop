namespace CanfarDesktop.Services.Fits;

using CanfarDesktop.Models.Fits;

/// <summary>
/// The picture of an image, as distinct from the image: what the viewer draws, at a size a screen and
/// a GPU can take.
///
/// <para>The viewer used to draw every image at full size, into one bitmap. For a 20315 × 20475
/// MegaPipe tile that is a 1.6 GB colour buffer, copied into a 1.6 GB bitmap, remade on every move of
/// a stretch slider — on top of the 1.6 GB of pixels — and a bitmap wider than the 16384 pixels a
/// Direct3D 11 texture can be, so it could not be put on screen at all. A screen shows about two
/// million pixels; the other four hundred million were being coloured in for nothing.</para>
///
/// <para>So only the picture is reduced. The image's own pixels stay whole, and everything that
/// reads a VALUE or a COORDINATE — the readout, the WCS, marks, Go To, exports, an agent's probe —
/// goes on reading them. The page's coordinate chain was already built on the data's size and the
/// element's on-screen size, never the bitmap's, so a smaller picture fits into it unchanged.</para>
///
/// <para>Every image under the limits is its own picture: no copy, no change, the path it always
/// took. The limits are generous enough that the 11471 × 4593 mosaics that already opened are among
/// them.</para>
/// </summary>
public static class FitsDisplayRaster
{
    /// <summary>At most this many pixels drawn: 256 MB as BGRA, per buffer.</summary>
    public const long MaxPixels = 64L * 1024 * 1024;

    /// <summary>The widest texture Direct3D 11 hardware is required to support.</summary>
    public const int MaxSide = 16384;

    /// <summary>The fraction of full size the picture is drawn at: 1 when the image fits as it is.</summary>
    /// <param name="maxPixels">The limits, given only by tests: the real ones need a 256 MB image to cross.</param>
    public static double ScaleFor(int width, int height, long maxPixels = MaxPixels, int maxSide = MaxSide)
    {
        if (width <= 0 || height <= 0) return 1;

        var byArea = Math.Sqrt(maxPixels / ((double)width * height));
        var bySide = (double)maxSide / Math.Max(width, height);
        return Math.Min(1, Math.Min(byArea, bySide));
    }

    /// <summary>
    /// The picture to draw for <paramref name="image"/>: the image itself when it fits, otherwise a
    /// block average of it within <see cref="MaxPixels"/> and <see cref="MaxSide"/>.
    ///
    /// <para>An average rather than every Nth pixel. Picking pixels drops what falls between them,
    /// and in a star field a star is often one or two pixels across: decimation would leave holes in
    /// the sky where sources are. An average keeps each one, spread over its block, which is what
    /// binning an astronomical image means. Non-finite pixels (blank, NaN) are left out of the
    /// average; a block with nothing finite in it stays NaN.</para>
    ///
    /// <para>No WCS on the result, deliberately. Its pixels are not the image's, and a coordinate
    /// read from them would be wrong by the scale; the viewer takes coordinates from the image.</para>
    /// </summary>
    public static FitsImageData For(FitsImageData image, long maxPixels = MaxPixels, int maxSide = MaxSide)
    {
        var scale = ScaleFor(image.Width, image.Height, maxPixels, maxSide);
        if (scale >= 1) return image;

        // Floors, so the product stays within MaxPixels and each side within MaxSide — after a hair of
        // tolerance, or 6 × ⅓ comes out 1.9999999999999998 and a side is lost to rounding. The limits
        // are whole numbers, so the tolerance cannot carry a side or the area past them.
        var w = Math.Max(1, (int)(image.Width * scale + 1e-6));
        var h = Math.Max(1, (int)(image.Height * scale + 1e-6));
        var fullWidth = image.Width;
        var fullHeight = image.Height;
        var source = image.Pixels;
        var picture = new float[w * h];
        var rowMin = new float[h];
        var rowMax = new float[h];

        // Each output column covers [start[c], start[c + 1]) of the image — two or three columns at
        // this app's scales. Worked out once rather than per row.
        var start = new int[w + 1];
        for (var c = 0; c <= w; c++) start[c] = (int)((long)c * fullWidth / w);

        // Rows are independent — each output row reads its own band of input rows — so they go in
        // parallel, with one pair of accumulators per worker rather than per row.
        Parallel.For(0, h,
            () => (Sums: new double[w], Counts: new int[w]),
            (oy, _, acc) =>
            {
                Array.Clear(acc.Sums);
                Array.Clear(acc.Counts);

                // Stored bottom-up, and kept that way: FitsRenderer flips it as it draws, as it does
                // the full-size image.
                var y0 = (int)((long)oy * fullHeight / h);
                var y1 = (int)((long)(oy + 1) * fullHeight / h);
                for (var y = y0; y < y1; y++)
                {
                    var row = (long)y * fullWidth;
                    for (var c = 0; c < w; c++)
                    {
                        for (var x = start[c]; x < start[c + 1]; x++)
                        {
                            var v = source[row + x];
                            if (!float.IsFinite(v)) continue;
                            acc.Sums[c] += v;
                            acc.Counts[c]++;
                        }
                    }
                }

                var lo = float.MaxValue;
                var hi = float.MinValue;
                var at = oy * w;
                for (var c = 0; c < w; c++)
                {
                    if (acc.Counts[c] == 0)
                    {
                        picture[at + c] = float.NaN;
                        continue;
                    }

                    var mean = (float)(acc.Sums[c] / acc.Counts[c]);
                    picture[at + c] = mean;
                    if (mean < lo) lo = mean;
                    if (mean > hi) hi = mean;
                }

                rowMin[oy] = lo;
                rowMax[oy] = hi;
                return acc;
            },
            _ => { });

        var min = rowMin.Min();
        var max = rowMax.Max();
        if (min == float.MaxValue) (min, max) = (0, 1); // nothing finite — as FitsParser has it

        return new FitsImageData
        {
            Pixels = picture,
            Width = w,
            Height = h,
            Min = min,
            Max = max,
            Unit = image.Unit,
        };
    }
}
