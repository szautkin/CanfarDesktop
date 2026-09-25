using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Rasterise an export plate at a real scale, in pieces when it is too big to do in one.
///
/// <para><c>RenderTargetBitmap</c> caps a single rasterisation at <see cref="RasterLimit.MaxEdge"/>
/// and, asked for more, renders smaller without saying so. That is why the 4x figure came back barely
/// larger than the 2x one. The cap is on one rasterisation rather than on the picture, so a figure
/// past it is rendered a piece at a time and the pieces assembled.</para>
///
/// <para>Each piece is produced by scaling the plate up and sliding it under a window the size of that
/// piece — the plate is drawn at the FULL output resolution every time, and the window decides which
/// part of it lands in the bitmap. Text and marks are therefore rendered at 4x rather than a 1x
/// rendering enlarged, which is the whole reason for offering the scale.</para>
/// </summary>
internal static class PlateRasterizer
{
    /// <summary>
    /// Whether to assemble an oversized figure from pieces. OFF, because it does not work yet.
    ///
    /// <para>The idea is sound and the arithmetic is right — TiledRaster is tested down to "every
    /// pixel is written exactly once". What does not work is the rasterising: a plate scaled up and
    /// slid under a window renders its top correctly and loses the rest. Measured on the cube figure,
    /// a 4x export comes out the full 5800x5140 with content reaching only 63% of the height; the
    /// colorbar and metadata are simply not there.</para>
    ///
    /// <para>Two attempts failed to fix it — first suspecting the per-piece window resize, then
    /// guarding against a piece coming back empty. The guard does not catch this because the missing
    /// pieces are not empty: they carry the plate's background and none of its content, so they look
    /// rendered. That is precisely the failure that must not ship, because a cropped figure reads as
    /// a deliberate crop.</para>
    ///
    /// <para>So exports take the single rasterisation, which is correct at up to about 2.8x on these
    /// plates and says which scale it really achieved. Turning this on again needs a debugger on the
    /// visual tree, not another guess.</para>
    /// </summary>
    private const bool TilingWorks = false;

    /// <summary>The assembled figure: BGRA8 pixels and the size they make up.</summary>
    /// <param name="Scale">
    /// The scale actually rendered. Equal to the request unless the whole figure would be too large to
    /// assemble — worth reporting, since it is the number the figure really is.
    /// </param>
    internal readonly record struct Result(byte[] Pixels, int Width, int Height, double Scale);

    /// <summary>
    /// Rasterise <paramref name="plate"/> at <paramref name="scale"/>.
    ///
    /// <paramref name="host"/> is an off-screen container the pieces are laid out in; it is left as it
    /// was found. Null when the plate has no size to render, or a piece could not be rasterised.
    /// </summary>
    /// <param name="expectOpaque">
    /// Whether every piece should come back with something in it. True for a figure with a background,
    /// which is all of them except a transparent PNG. It is how a piece that silently failed to render
    /// is told from one that is legitimately empty, and it is worth knowing: a figure missing its
    /// bottom row of pieces has lost its colorbar and its metadata, and looks merely cropped.
    /// </param>
    public static async Task<Result?> RenderAsync(
        FrameworkElement plate, Panel host, double scale, bool expectOpaque = true)
    {
        // The plate's own size, before any scaling: everything below is measured from it.
        plate.UpdateLayout();
        double naturalWidth = plate.ActualWidth, naturalHeight = plate.ActualHeight;

        var plan = TiledRaster.For(naturalWidth, naturalHeight, scale);
        if (plan.IsEmpty) return null;

        // One rasterisation is the ordinary case, and it needs none of the machinery below.
        if (!plan.IsTiled || !TilingWorks)
        {
            // What a single rasterisation can actually produce, which for a figure past the limit is
            // less than was asked for. Reported as such rather than silently delivered.
            var fit = RasterLimit.Fit(naturalWidth, naturalHeight, scale);
            if (fit.Width < 1 || fit.Height < 1) return null;

            var single = new RenderTargetBitmap();
            await single.RenderAsync(plate, fit.Width, fit.Height);
            var pixels = (await single.GetPixelsAsync()).ToArray();

            return single.PixelWidth > 0 && single.PixelHeight > 0
                ? new Result(pixels, single.PixelWidth, single.PixelHeight, fit.Scale)
                : null;
        }

        var assembled = new byte[(long)plan.Width * plan.Height * 4];

        // A window the size of one piece, with the plate inside it scaled up and slid into place. The
        // plate keeps its natural size and alignment so the window crops it rather than squashing it.
        var window = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var scaling = new ScaleTransform { ScaleX = plan.Scale, ScaleY = plan.Scale };
        var sliding = new TranslateTransform();

        var parent = plate.Parent as Panel;
        var indexInParent = parent?.Children.IndexOf(plate) ?? -1;
        parent?.Children.Remove(plate);

        plate.HorizontalAlignment = HorizontalAlignment.Left;
        plate.VerticalAlignment = VerticalAlignment.Top;
        plate.Width = naturalWidth;
        plate.Height = naturalHeight;
        plate.RenderTransform = new TransformGroup { Children = { scaling, sliding } };

        // ONE size for every piece, set once. Resizing the window between pieces looked tidier and
        // did not work: the pieces that changed its height came back blank, which took the figure's
        // whole bottom row with them — the colorbar and the metadata. Whatever the cause, a window
        // that never changes shape has nothing to get wrong between one piece and the next. The
        // pieces at the right and bottom edges simply have blank space in them, and the copy back
        // takes only the part that belongs to the figure.
        var windowWidth = plan.Tiles.Max(t => t.Width);
        var windowHeight = plan.Tiles.Max(t => t.Height);

        window.Width = windowWidth;
        window.Height = windowHeight;
        window.Clip = new RectangleGeometry { Rect = new Rect(0, 0, windowWidth, windowHeight) };

        window.Children.Add(plate);
        host.Children.Add(window);
        Canvas.SetLeft(window, -100000);
        host.UpdateLayout();

        var lostAPiece = false;

        try
        {
            foreach (var tile in plan.Tiles)
            {
                sliding.X = -tile.X;
                sliding.Y = -tile.Y;

                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(window, windowWidth, windowHeight);
                if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1) return null;

                var piece = (await bitmap.GetPixelsAsync()).ToArray();

                // A piece of a figure that has a background cannot legitimately be empty. One that is
                // empty did not render, and carrying on would assemble a figure with a hole in it that
                // reads as a crop rather than as a fault.
                if (expectOpaque && IsBlank(piece)) { lostAPiece = true; break; }

                Blit(piece, bitmap.PixelWidth, bitmap.PixelHeight,
                     assembled, plan.Width, plan.Height,
                     tile.X, tile.Y, tile.Width, tile.Height);
            }
        }
        finally
        {
            // Put the plate back the way it was found, so a caller that still holds it is not handed
            // something scaled and clipped.
            window.Children.Remove(plate);
            host.Children.Remove(window);

            plate.RenderTransform = null;
            plate.Width = double.NaN;
            plate.Height = double.NaN;

            if (parent is not null && indexInParent >= 0) parent.Children.Insert(indexInParent, plate);
        }

        if (!lostAPiece) return new Result(assembled, plan.Width, plan.Height, plan.Scale);

        // Assembling it did not work. A smaller figure that is whole beats a large one missing its
        // bottom, so fall back to the one rasterisation that is known to work — and report the scale
        // it really is, which is the honest half of this that was always true.
        var fallback = RasterLimit.Fit(naturalWidth, naturalHeight, scale);
        if (fallback.Width < 1 || fallback.Height < 1) return null;

        var whole = new RenderTargetBitmap();
        await whole.RenderAsync(plate, fallback.Width, fallback.Height);
        if (whole.PixelWidth < 1 || whole.PixelHeight < 1) return null;

        return new Result((await whole.GetPixelsAsync()).ToArray(),
                          whole.PixelWidth, whole.PixelHeight, fallback.Scale);
    }

    /// <summary>Whether a rasterised piece came back with nothing in it at all.</summary>
    private static bool IsBlank(byte[] pixels)
    {
        // Every fourth byte is alpha. Checking that alone is four times less work than checking all of
        // them, and a piece that rendered anything at all over a background has alpha somewhere.
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) return false;

        return true;
    }

    /// <summary>
    /// Copy one piece into the assembled image, row by row.
    ///
    /// Clipped against both, because a rasterisation can come back a pixel smaller than it was asked
    /// for, and a copy that trusted the requested size would read past the end of the piece.
    /// </summary>
    private static void Blit(
        byte[] source, int sourceWidth, int sourceHeight,
        byte[] destination, int destinationWidth, int destinationHeight,
        int atX, int atY, int takeWidth, int takeHeight)
    {
        // Three things bound this: what the piece asked for, what came back, and what is left of the
        // figure. An edge piece is rendered full size with blank space in it, so taking all of it
        // would write that blank over the neighbour already copied in.
        var rows = Math.Min(takeHeight, Math.Min(sourceHeight, destinationHeight - atY));
        var columns = Math.Min(takeWidth, Math.Min(sourceWidth, destinationWidth - atX));
        if (rows < 1 || columns < 1) return;

        var bytes = columns * 4;

        for (var row = 0; row < rows; row++)
        {
            var from = (long)row * sourceWidth * 4;
            var to = ((long)(atY + row) * destinationWidth + atX) * 4;
            Array.Copy(source, from, destination, to, bytes);
        }
    }
}
