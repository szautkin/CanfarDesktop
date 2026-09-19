using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Models.Fits;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// What an agent sees when it looks at this viewer.
///
/// A capture is not a figure. <c>export_fits_figure</c> writes a plate someone will put in a paper —
/// title, caption, colorbar, provenance — because a reader has to be able to believe it. This answers
/// a different question: what is the person looking at RIGHT NOW, at their zoom and pan, in their
/// colormap and stretch, with their marks on it. So it reuses the same renderer and the same plate
/// with the chrome switched off, rather than growing a second way to draw the same picture.
///
/// The transform that rides back with it is the part that matters. Without it a capture is something
/// to describe in words; with it an agent can point at a feature and every other tool in the viewer
/// will accept the coordinate.
/// </summary>
public sealed partial class FitsViewerPage
{
    /// <summary>
    /// Capture what this viewer is showing, bounded by the caller's pixel and byte caps.
    ///
    /// Runs on the UI thread: the plate is a real control and has to measure before it can be
    /// rasterised. It is laid out far off-screen so it never appears over the viewer while it does.
    /// </summary>
    internal async Task<ViewerCapture> CaptureAsync(ViewerCaptureRequest request)
    {
        if (ViewModel.ImageData is not { } image)
            return ViewerCapture.Unavailable("no FITS image is open");

        if (CurrentViewRegion().ClampTo(image.Width, image.Height) is not { } area || !area.IsValid)
            return ViewerCapture.Unavailable("the viewer is not showing any of this image");

        var size = AgentCapture.Fit(
            (int)Math.Round(area.Width), (int)Math.Round(area.Height),
            request.MaxPixels ?? AgentCapture.DefaultMaxPixels,
            request.MaxBytes ?? AgentCapture.DefaultMaxBytes);

        if (size.Width < 1 || size.Height < 1)
            return ViewerCapture.Unavailable("the viewer is showing too little of the image to capture");

        FitsExportPlate? plate = null;
        try
        {
            var frame = await RenderFrameAtAsync(area, size.Width, size.Height);
            if (frame is null) return ViewerCapture.Unavailable("that region could not be rendered");

            // The plate without its chrome: the frame, and the marks over it. `inkScale` follows the
            // rendering in both directions, so a downscaled capture thins the strokes to match rather
            // than handing back a picture where the annotations are the one thing that did not shrink.
            var style = FitsExportPlate.PlateStyle.Default;
            style.Annotate = false;
            style.ShowMarks = true;
            style.Transparent = false;

            plate = new FitsExportPlate();
            plate.Populate(frame.Value.Frame, frame.Value.W, frame.Value.H, BuildPlateData(area), style, size.Scale);

            ExportHost.Children.Add(plate);
            Canvas.SetLeft(plate, -100000);
            plate.UpdateLayout();

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(plate, (int)Math.Ceiling(plate.ActualWidth), (int)Math.Ceiling(plate.ActualHeight));

            int rw = rtb.PixelWidth, rh = rtb.PixelHeight;
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            if (rw <= 0 || rh <= 0 || pixels.Length < (long)rw * rh * 4)
                return ViewerCapture.Unavailable("the capture could not be rasterized");

            var png = await PngBytes.FromBgraAsync(pixels, rw, rh);

            // From the ACTUAL rasterized width, not the size we asked for: the plate rounds its own
            // layout, and a transform derived from the request would be off by that rounding.
            var transform = AgentCapture.TransformFor(area.X, area.Y, rw / area.Width);

            return new ViewerCapture(
                Captured: true, png, "image/png", rw, rh, transform,
                Describes: Describe(area, image),
                Note: size.Note);
        }
        catch (Exception ex)
        {
            return ViewerCapture.Unavailable(ex.Message);
        }
        finally
        {
            if (plate is not null) ExportHost.Children.Remove(plate);
        }
    }

    /// <summary>
    /// One line saying what the capture is of, in the coordinates the caller can act on: the image
    /// pixels it covers, and the sky centre when there is a WCS to give one.
    /// </summary>
    private static string Describe(FitsRegion area, FitsImageData image)
    {
        var box = $"display pixels x {area.X:0}–{area.Right:0}, y {area.Y:0}–{area.Bottom:0} " +
                  $"of {image.Width}×{image.Height}";

        if (image.Wcs is not { IsValid: true } wcs) return box;

        var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, image.Height, area.CentreX, area.CentreY);
        return $"{box}; centre {WcsInfo.FormatRa(ra)} {WcsInfo.FormatDec(dec)}";
    }
}
