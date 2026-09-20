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
    public static async Task<Result?> RenderAsync(FrameworkElement plate, Panel host, double scale)
    {
        // The plate's own size, before any scaling: everything below is measured from it.
        plate.UpdateLayout();
        double naturalWidth = plate.ActualWidth, naturalHeight = plate.ActualHeight;

        var plan = TiledRaster.For(naturalWidth, naturalHeight, scale);
        if (plan.IsEmpty) return null;

        // One rasterisation is the ordinary case, and it needs none of the machinery below.
        if (!plan.IsTiled)
        {
            var single = new RenderTargetBitmap();
            await single.RenderAsync(plate, plan.Width, plan.Height);
            var pixels = (await single.GetPixelsAsync()).ToArray();

            return single.PixelWidth > 0 && single.PixelHeight > 0
                ? new Result(pixels, single.PixelWidth, single.PixelHeight, plan.Scale)
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

        window.Children.Add(plate);
        host.Children.Add(window);
        Canvas.SetLeft(window, -100000);

        try
        {
            foreach (var tile in plan.Tiles)
            {
                window.Width = tile.Width;
                window.Height = tile.Height;
                window.Clip = new RectangleGeometry { Rect = new Rect(0, 0, tile.Width, tile.Height) };

                sliding.X = -tile.X;
                sliding.Y = -tile.Y;
                window.UpdateLayout();

                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(window, tile.Width, tile.Height);
                if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1) return null;

                Blit((await bitmap.GetPixelsAsync()).ToArray(),
                     bitmap.PixelWidth, bitmap.PixelHeight,
                     assembled, plan.Width, plan.Height, tile.X, tile.Y);
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

        return new Result(assembled, plan.Width, plan.Height, plan.Scale);
    }

    /// <summary>
    /// Copy one piece into the assembled image, row by row.
    ///
    /// Clipped against both, because a rasterisation can come back a pixel smaller than it was asked
    /// for, and a copy that trusted the requested size would read past the end of the piece.
    /// </summary>
    private static void Blit(
        byte[] source, int sourceWidth, int sourceHeight,
        byte[] destination, int destinationWidth, int destinationHeight, int atX, int atY)
    {
        var rows = Math.Min(sourceHeight, destinationHeight - atY);
        var columns = Math.Min(sourceWidth, destinationWidth - atX);
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
