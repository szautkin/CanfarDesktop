using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// Figure export for the FITS viewer: the picture, the marks over it, the cut levels it was made with,
/// and where on the sky it is.
///
/// The picture is RENDERED from the pixel data at the size the figure needs — not captured from the
/// canvas. Everything else on the plate is laid out in XAML and rasterised, which is how the cube
/// viewer's export works too, so the two figures come out of the same press.
/// </summary>
public sealed partial class FitsViewerPage
{
    private bool _exporting;

    /// <summary>
    /// The figure's longest side, before the scale factor. Big enough that a 1× export is publishable
    /// and a 4× one is a poster, small enough that the plate lays out in a blink.
    /// </summary>
    private const int FrameBase = 1200;

    /// <summary>
    /// Render a region as the figure's picture. Null when the region is not on the image, or when
    /// nothing is loaded.
    /// </summary>
    private async Task<(WriteableBitmap Frame, int W, int H)?> RenderFrameAsync(FitsRegion region, int scale)
    {
        if (ViewModel.ImageData is not { } image) return null;
        if (region.ClampTo(image.Width, image.Height) is not { } area) return null;

        // The frame keeps the region's aspect: a figure of a wide region should be wide. The scale
        // multiplies both sides, so 4x is four times the picture rather than four times the file.
        var longest = Math.Max(area.Width, area.Height);
        var w = (int)Math.Round(FrameBase * scale * area.Width / longest);
        var h = (int)Math.Round(FrameBase * scale * area.Height / longest);
        if (w < 1 || h < 1) return null;

        var colormap = ColormapProvider.GetColormap(ViewModel.Colormap);
        var stretch = ViewModel.Stretch;
        var (lo, hi) = (ViewModel.MinCut, ViewModel.MaxCut);

        var bgra = await Task.Run(() =>
            FitsRenderer.RenderRegion(image, area, w, h, stretch, colormap, lo, hi));
        if (bgra is null) return null;

        var bitmap = new WriteableBitmap(w, h);
        using (var stream = bitmap.PixelBuffer.AsStream()) await stream.WriteAsync(bgra, 0, bgra.Length);
        bitmap.Invalidate();

        return (bitmap, w, h);
    }

    /// <summary>
    /// The region the viewer is currently showing, in image pixels.
    ///
    /// Derived by asking the canvas transform where its own corners land, rather than by reading the
    /// zoom and pan back: that keeps it right through rotation and a parity flip, which the transform
    /// knows about and this does not.
    /// </summary>
    internal FitsRegion CurrentViewRegion()
    {
        if (ViewModel.ImageData is not { } image) return default;

        var w = ImageCanvas.ActualWidth;
        var h = ImageCanvas.ActualHeight;
        if (w < 1 || h < 1) return FitsRegion.WholeImage(image.Width, image.Height);

        // All four corners, because a rotated view's bounding box is not given by two of them.
        var corners = new[]
        {
            e_PointToImage(new Windows.Foundation.Point(0, 0)),
            e_PointToImage(new Windows.Foundation.Point(w, 0)),
            e_PointToImage(new Windows.Foundation.Point(0, h)),
            e_PointToImage(new Windows.Foundation.Point(w, h)),
        };

        var minX = corners.Min(p => p.X);
        var maxX = corners.Max(p => p.X);
        var minY = corners.Min(p => p.Y);
        var maxY = corners.Max(p => p.Y);

        var region = new FitsRegion(minX, minY, maxX - minX, maxY - minY);
        return region.ClampTo(image.Width, image.Height) ?? FitsRegion.WholeImage(image.Width, image.Height);
    }

    /// <summary>Build the plate's content for a region.</summary>
    internal FitsExportPlate.PlateData BuildPlateData(FitsRegion region)
    {
        var image = ViewModel.ImageData;
        var header = ViewModel.CurrentHeader;
        var wcs = image?.Wcs is { IsValid: true } valid ? valid : null;
        var unit = string.IsNullOrWhiteSpace(image?.Unit) ? "" : " " + image!.Unit;

        var facts = new List<(string, string)>();

        if (wcs is not null && image is not null)
        {
            // The centre of the REGION, not of the image: the figure is of the region.
            var fitsX = region.CentreX + 1;
            var fitsY = image.Height - 1 - region.CentreY + 1;
            var (ra, dec) = wcs.PixelToWorld(fitsX, fitsY);
            facts.Add(("Centre", $"{WcsInfo.FormatRa(ra)}  {WcsInfo.FormatDec(dec)}"));

            var fovX = region.Width * wcs.PixelScaleArcsec / 60.0;
            var fovY = region.Height * wcs.PixelScaleArcsec / 60.0;
            facts.Add(("Field", FormatFov(fovX, fovY)));
            facts.Add(("Scale", $"{wcs.PixelScaleArcsec.ToString("0.##", CultureInfo.InvariantCulture)} ″/px"));
        }

        AddFact(facts, "Filter", header?.GetString("FILTER") ?? header?.GetString("FILTER1"));
        AddFact(facts, "Observed", header?.GetString("DATE-OBS"));

        var exposure = header?.GetDouble("EXPTIME") ?? 0;
        if (exposure > 0) AddFact(facts, "Exposure", $"{exposure.ToString("0.###", CultureInfo.InvariantCulture)} s");

        var whole = image is not null
                 && region.Width >= image.Width - 1 && region.Height >= image.Height - 1;

        return new FitsExportPlate.PlateData
        {
            Title = header?.GetString("OBJECT") is { Length: > 0 } obj ? obj : ExportBaseName(),
            Subtitle = string.Join(" · ", new[] { header?.GetString("TELESCOP"), header?.GetString("INSTRUME") }
                .Where(v => !string.IsNullOrWhiteSpace(v))),
            RegionText = whole
                ? $"FULL FRAME {region.Width:0} × {region.Height:0} px"
                : $"REGION {region.Width:0} × {region.Height:0} px",
            FileName = System.IO.Path.GetFileName(ViewModel.FilePath ?? "") ?? "",
            DateText = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CbMin = ViewModel.MinCut.ToString("0.###", CultureInfo.InvariantCulture),
            CbMax = ViewModel.MaxCut.ToString("0.###", CultureInfo.InvariantCulture) + unit,
            CbStretch = $"{ViewModel.Stretch.ToString().ToUpperInvariant()} · {ViewModel.Colormap.ToString().ToUpperInvariant()}",
            ColorbarLut = ColorbarLut(ViewModel.Colormap),
            Facts = facts,
            Marks = _annotations,
            Region = region,
            Wcs = wcs,
            ImageHeight = image?.Height ?? 0,
        };
    }

    private static void AddFact(List<(string, string)> facts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) facts.Add((key, value!));
    }

    /// <summary>The colormap as the 256×4 RGBA the plate's colorbar gradient reads.</summary>
    private static byte[] ColorbarLut(ColormapProvider.ColormapName name)
    {
        var colors = ColormapProvider.GetColormap(name);
        var lut = new byte[256 * 4];

        for (var i = 0; i < 256 && i < colors.Length; i++)
        {
            lut[i * 4 + 0] = colors[i].R;
            lut[i * 4 + 1] = colors[i].G;
            lut[i * 4 + 2] = colors[i].B;
            lut[i * 4 + 3] = 255;
        }
        return lut;
    }

    private string ExportBaseName()
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(ViewModel.FilePath ?? "");
        return string.IsNullOrWhiteSpace(name) ? "figure" : name;
    }


    /// <summary>
    /// A plate for the preview, built at 1x and handed to the dialog to show.
    ///
    /// Always 1x whatever the export scale is: the preview's job is to show the LAYOUT, and rendering a
    /// 4x plate to display it at a quarter size is the same picture four times as slowly.
    /// </summary>
    internal async Task<FitsExportPlate?> BuildPreviewPlateAsync(FitsRegion region, FitsExportPlate.PlateStyle style)
    {
        var frame = await RenderFrameAsync(region, scale: 1);
        if (frame is null) return null;

        var plate = new FitsExportPlate();
        plate.Populate(frame.Value.Frame, frame.Value.W, frame.Value.H, BuildPlateData(region), style, inkScale: 1);
        return plate;
    }

    /// <summary>
    /// Render a region to a PNG or PDF at <paramref name="path"/>. Null on success, else the reason.
    ///
    /// Shared by the dialog's Save and the MCP tool, so a figure exported by an agent is the same
    /// artefact as one exported by hand.
    /// </summary>
    internal async Task<string?> ExportRegionToPathAsync(
        FitsRegion region, string path, string format, int scale, FitsExportPlate.PlateStyle style)
    {
        if (_exporting) return "an export is already in progress";
        if (ViewModel.ImageData is null) return "no image is open";

        var pdf = (format ?? "").Trim().Equals("pdf", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
            return "path must be a full (rooted) file path";

        string full;
        try { full = System.IO.Path.GetFullPath(path); }
        catch { return "invalid path"; }

        var extension = System.IO.Path.GetExtension(full).ToLowerInvariant();
        if (pdf && extension != ".pdf") return "path must end in .pdf for a PDF export";
        if (!pdf && extension != ".png") return "path must end in .png for a PNG export";

        _exporting = true;
        FitsExportPlate? plate = null;
        try
        {
            var clamped = Math.Clamp(scale, 1, 4);
            var frame = await RenderFrameAsync(region, clamped);
            if (frame is null) return "that region is not on this image";

            plate = new FitsExportPlate();
            plate.Populate(frame.Value.Frame, frame.Value.W, frame.Value.H, BuildPlateData(region), style, clamped);

            // Laid out off-screen: the plate is a real control and has to measure before it can be
            // rasterised, but it must never appear over the viewer while it does.
            ExportHost.Children.Add(plate);
            Canvas.SetLeft(plate, -100000);
            plate.UpdateLayout();

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(plate, (int)Math.Ceiling(plate.ActualWidth), (int)Math.Ceiling(plate.ActualHeight));

            int rw = rtb.PixelWidth, rh = rtb.PixelHeight;
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            if (rw <= 0 || rh <= 0 || pixels.Length < (long)rw * rh * 4) return "plate rasterization failed";

            using var file = new System.IO.FileStream(full, System.IO.FileMode.Create);
            if (pdf)
            {
                PdfImageWriter.Write(file, PdfImageWriter.BgraToRgb(pixels, rw, rh), rw, rh);
            }
            else
            {
                using var stream = file.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)rw, (uint)rh, 96, 96, pixels);
                await encoder.FlushAsync();
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            _exporting = false;
            if (plate is not null) ExportHost.Children.Remove(plate);
        }
    }

    /// <summary>
    /// The MCP entry: resolve which region the request means, then render it.
    ///
    /// Resolving happens HERE rather than in the tool because only the page knows what is on screen,
    /// what the image is, and which marks are on it. The tool's job is to refuse a request that cannot
    /// mean anything; this one's is to answer the ones that can.
    /// </summary>
    internal async Task<FitsFigureOutcome> ExportFigureAsync(FitsFigureRequest request)
    {
        if (ViewModel.ImageData is not { } image)
            return FitsFigureOutcome.Unavailable("no FITS image is open");

        var wcs = image.Wcs is { IsValid: true } valid ? valid : null;

        FitsRegion? region;
        string described;

        switch (request.RegionKind)
        {
            case FigureRegionKind.Image:
                region = FitsRegion.WholeImage(image.Width, image.Height);
                described = "the whole frame";
                break;

            case FigureRegionKind.PixelBox:
                region = new FitsRegion(request.X!.Value, request.Y!.Value, request.Width!.Value, request.Height!.Value);
                described = $"pixels {request.X:0},{request.Y:0} + {request.Width:0}×{request.Height:0}";
                break;

            case FigureRegionKind.SkyCircle:
                if (wcs is null)
                    return FitsFigureOutcome.Unavailable("this image has no WCS, so a sky region cannot be placed on it");

                region = FitsRegion.FromSkyCircle(wcs, image.Height, request.RaDeg!.Value, request.DecDeg!.Value, request.RadiusDeg!.Value);
                described = $"{request.RadiusDeg}° around {request.RaDeg}, {request.DecDeg}";
                break;

            case FigureRegionKind.Mark:
            {
                var mark = _annotations.FirstOrDefault(a => a.Id == request.MarkId);
                if (mark is null)
                    return FitsFigureOutcome.Unavailable(
                        $"no mark '{request.MarkId}' on this image — list_fits_annotations gives the ids");

                region = RegionAround(mark, image, wcs);
                if (region is null)
                    return FitsFigureOutcome.Unavailable($"mark '{request.MarkId}' is not on this image");

                described = $"around mark {mark.Id}";
                break;
            }

            default:
                region = CurrentViewRegion();
                described = "the view on screen";
                break;
        }

        if (region is not { } wanted || wanted.ClampTo(image.Width, image.Height) is not { } area)
            return FitsFigureOutcome.Unavailable($"{described} is not on this image");

        var style = new FitsExportPlate.PlateStyle
        {
            Dark = request.Dark,
            Font = "sans",
            TextColor = "auto",
            TextScale = 1.0,
            Annotate = request.Annotate,
            ShowMarks = request.ShowMarks,
            Transparent = false,
        };

        var error = await ExportRegionToPathAsync(area, request.Path, request.Format, request.Scale, style);
        if (error is not null) return new FitsFigureOutcome(false, request.Path, request.Format, request.Scale, described, 0, error);

        var drawn = request.ShowMarks ? CountMarksIn(area) : 0;
        return new FitsFigureOutcome(true, request.Path, request.Format, request.Scale, described, drawn);
    }

    /// <summary>
    /// A region framing one mark, with room around it. A figure cropped exactly to a circle is a picture
    /// of the circle; the point of framing a mark is the thing it is drawn around and what is near it.
    /// </summary>
    private FitsRegion? RegionAround(Models.Annotation mark, FitsImageData image, WcsInfo? wcs)
    {
        var surface = new FitsExportSurface(FitsRegion.WholeImage(image.Width, image.Height),
            image.Width, image.Height, wcs, image.Height);

        if (surface.Project(mark.Anchor) is not { } centre) return null;

        // The extent is in the anchor's own units; on the whole-image surface one image pixel is one
        // plate pixel, so the same conversion gives it in image pixels.
        var half = mark.Extent is { } extent
            ? Math.Max(extent.HalfWidth, extent.HalfHeight) * surface.UnitsToPixels(mark.Anchor)
            : 20;

        return FitsRegion.Around(centre.X, centre.Y, half, half).Padded(1.0);
    }

    /// <summary>How many marks the figure actually shows — the ones inside the region.</summary>
    private int CountMarksIn(FitsRegion region)
    {
        if (ViewModel.ImageData is not { } image) return 0;

        var surface = new FitsExportSurface(region, region.Width, region.Height,
            image.Wcs is { IsValid: true } wcs ? wcs : null, image.Height);

        return _annotations.Count(a => surface.Project(a.Anchor) is not null);
    }
}
