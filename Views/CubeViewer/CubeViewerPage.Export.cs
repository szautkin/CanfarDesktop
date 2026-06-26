using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Figure export for the Cube Viewer: renders the volume offscreen on the GPU at a 2×/4× scale of
/// the current view, composites the box + WCS caption overlay, then composes a publication "plate"
/// (header / framed render / legend + colorbar) and writes it as PNG or PDF. The Windows analogue
/// of the macOS "Export figure…" / v-cube composePlate.
/// </summary>
public sealed partial class CubeViewerPage
{
    private bool _exporting;

    private enum ExportFormat { Png, Pdf }
    private readonly record struct ExportOptions(ExportFormat Format, int Scale, bool Dark);

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var opt = await ShowExportOptionsAsync();
        if (opt is not null) await ExportFigureAsync(opt.Value);
    }

    private async Task<ExportOptions?> ShowExportOptionsAsync()
    {
        var format = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        format.Items.Add("PNG"); format.Items.Add("PDF"); format.SelectedIndex = 0;

        var scale = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        scale.Items.Add("2×"); scale.Items.Add("4×"); scale.SelectedIndex = 0;

        var theme = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.Items.Add("Dark (cockpit)"); theme.Items.Add("Light (journal)"); theme.SelectedIndex = 0;

        var panel = new StackPanel { Spacing = 12, MinWidth = 260 };
        panel.Children.Add(Labeled("Format", format));
        panel.Children.Add(Labeled("Resolution", scale));
        panel.Children.Add(Labeled("Theme", theme));

        var dialog = new ContentDialog
        {
            Title = "Export figure",
            Content = panel,
            PrimaryButtonText = "Export",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return new ExportOptions(
            format.SelectedIndex == 1 ? ExportFormat.Pdf : ExportFormat.Png,
            scale.SelectedIndex == 1 ? 4 : 2,
            theme.SelectedIndex == 0);
    }

    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(new TextBlock { Text = label, Opacity = 0.8 });
        sp.Children.Add(control);
        return sp;
    }

    private async Task ExportFigureAsync(ExportOptions opt)
    {
        if (_exporting || !_renderer.IsReady) return;
        _exporting = true;
        var prevStatus = StatusText.Text;
        try
        {
            var raster = await BuildPlateRasterAsync(opt.Scale, opt.Dark);
            if (raster is null) return; // BuildPlateRasterAsync set the status
            var (plateBuf, pw, ph) = raster.Value;

            var hwnd = WindowHelper.ActiveWindows.Count > 0
                ? WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]) : nint.Zero;
            if (hwnd == nint.Zero) { StatusText.Text = "Export failed: no window handle"; return; }

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedFileName = ExportBaseName();
            if (opt.Format == ExportFormat.Pdf)
                picker.FileTypeChoices.Add("PDF document", new List<string> { ".pdf" });
            else
                picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });

            var file = await picker.PickSaveFileAsync();
            if (file is null) { StatusText.Text = prevStatus; return; }

            if (opt.Format == ExportFormat.Pdf)
            {
                var rgb = PdfImageWriter.BgraToRgb(plateBuf, pw, ph);
                using var fs = await file.OpenStreamForWriteAsync();
                fs.SetLength(0);
                PdfImageWriter.Write(fs, rgb, pw, ph);
            }
            else
            {
                using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)pw, (uint)ph, 96, 96, plateBuf);
                await encoder.FlushAsync();
            }
            StatusText.Text = "Saved " + file.Name;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed: " + ex.Message;
        }
        finally
        {
            _freezeRenderLoop = false;
            _exporting = false;
        }
    }

    /// <summary>
    /// Render the figure plate (volume + box + captions + legend) to BGRA pixels at the given scale,
    /// freezing the render loop for a still capture. Shared by the interactive export + the MCP export.
    /// Returns null (and sets a status message) on failure.
    /// </summary>
    private async Task<(byte[] Bytes, int W, int H)?> BuildPlateRasterAsync(int scale, bool dark)
    {
        CubeExportPlate? plate = null;
        try
        {
            if (RenderPanel.ActualWidth < 1 || RenderPanel.ActualHeight < 1)
            {
                StatusText.Text = "Export failed: the cube viewer must be visible";
                return null;
            }

            // Render the export at a FIXED 4:3 aspect (macOS 1400×1050 at 2×) with a camera pull-back,
            // so the cube is framed with margin — NOT stretched to fill the live window's wide aspect.
            int w = 700 * scale;   // 2× → 1400×1050, 4× → 2800×2100
            int h = 525 * scale;
            float az = ViewModel.CameraAzimuth, el = ViewModel.CameraElevation;
            float dist = ViewModel.CameraDistance * 1.3f; // macOS exportDistanceScale
            StatusText.Text = $"Exporting {w}×{h}…";

            // Freeze the loop so the volume + overlay share one (pulled-back) camera.
            _freezeRenderLoop = true;
            PushRenderState();
            _renderer.CameraDistance = dist; // overridden only for this still capture
            float steps = Math.Max(ViewModel.VolumeSteps, 384f);
            byte[]? volume = _renderer.RenderToBgra(w, h, steps);
            var overlay = volume is null ? null : await RenderExportOverlayAsync(w, h, az, el, dist);
            _freezeRenderLoop = false;

            if (volume is null)
            {
                StatusText.Text = "Export failed" + (scale >= 4 ? " (try 2×)" : "")
                    + ": " + (_renderer.LastError ?? "render");
                return null;
            }
            // Composite the box + captions over the volume (both rendered at the same 4:3 aspect).
            if (overlay is not null)
                CompositeOverScaled(volume, w, h, overlay.Value.Pixels, overlay.Value.W, overlay.Value.H);

            var frame = new WriteableBitmap(w, h);
            using (var s = frame.PixelBuffer.AsStream()) await s.WriteAsync(volume, 0, volume.Length);
            frame.Invalidate();

            plate = new CubeExportPlate();
            plate.Populate(frame, w, h, BuildPlateData(), dark);
            ExportHost.Children.Add(plate);
            Canvas.SetLeft(plate, -100000); // off-screen so it never shows in the live UI
            plate.UpdateLayout();

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(plate, (int)Math.Ceiling(plate.ActualWidth), (int)Math.Ceiling(plate.ActualHeight));
            int pw = rtb.PixelWidth, ph = rtb.PixelHeight;
            byte[] plateBuf = (await rtb.GetPixelsAsync()).ToArray();

            if (pw <= 0 || ph <= 0 || plateBuf.Length < (long)pw * ph * 4)
            {
                StatusText.Text = "Export failed: plate rasterization";
                return null;
            }
            return (plateBuf, pw, ph);
        }
        finally
        {
            _freezeRenderLoop = false;
            if (plate is not null) ExportHost.Children.Remove(plate);
        }
    }

    /// <summary>
    /// MCP entry: export the figure to an explicit file path (no picker). <paramref name="format"/> is
    /// "pdf" or "png". Returns null on success, or an error message.
    /// </summary>
    public async Task<string?> ExportCubeToPathAsync(string path, string format, int scale, bool dark)
    {
        if (_exporting) return "an export is already in progress";
        if (!_renderer.IsReady) return "the cube viewer is not ready";

        // Validate the agent-supplied path: must be a full (rooted) path, normalized to defeat
        // "..\\" traversal/relative-CWD surprises, with an extension matching the format.
        bool pdf = (format ?? "").Trim().Equals("pdf", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
            return "path must be a full (rooted) file path";
        string full;
        try { full = System.IO.Path.GetFullPath(path); }
        catch { return "invalid path"; }
        var ext = System.IO.Path.GetExtension(full).ToLowerInvariant();
        if (pdf && ext != ".pdf") return "path must end in .pdf for a PDF export";
        if (!pdf && ext != ".png") return "path must end in .png for a PNG export";

        _exporting = true;
        try
        {
            var raster = await BuildPlateRasterAsync(Math.Clamp(scale, 1, 4), dark);
            if (raster is null) return StatusText.Text;
            var (buf, pw, ph) = raster.Value;

            using (var fs = new System.IO.FileStream(full, System.IO.FileMode.Create))
            {
                if (pdf)
                {
                    PdfImageWriter.Write(fs, PdfImageWriter.BgraToRgb(buf, pw, ph), pw, ph);
                }
                else
                {
                    using var ras = fs.AsRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        (uint)pw, (uint)ph, 96, 96, buf);
                    await encoder.FlushAsync();
                }
            }
            StatusText.Text = "Saved " + System.IO.Path.GetFileName(path);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            _freezeRenderLoop = false;
            _exporting = false;
        }
    }

    /// <summary>Build the plate's text + colorbar content from the cube metadata and current display state.</summary>
    private CubeExportPlate.PlateData BuildPlateData()
    {
        string title = !string.IsNullOrEmpty(_meta?.Object) ? _meta!.Object
            : (string.IsNullOrEmpty(_cubeName) ? "Cube" : _cubeName);

        var d = new CubeExportPlate.PlateData
        {
            Title = title,
            Subtitle = _meta?.Instrument ?? "",
            FileName = _cubeName,
            DateText = DateTime.Now.ToString("yyyy-MM-dd"),
            AxisRanges = new List<(string, string)>(),
            CbStretch = $"{ViewModel.Stretch.ToString().ToUpperInvariant()} · "
                        + CubeColormaps.DisplayName(ViewModel.Colormap).ToUpperInvariant(),
            ColorbarLut = CubeColormaps.Build(ViewModel.Colormap),
        };

        if (_meta is not null)
        {
            var w = _meta.Wcs;
            d.Facts = $"{_meta.DimensionsText} · NaN {_meta.NanText} · Resident";
            d.AxisRanges.Add((w.LonName, $"{w.LonText(0)} … {w.LonText(Math.Max(0, w.Nx - 1))}"));
            d.AxisRanges.Add((w.LatName, $"{w.LatText(0)} … {w.LatText(Math.Max(0, w.Ny - 1))}"));
            if (w.Nz > 1)
            {
                string unit = w.SpecUnitDisplay();
                string range = $"{w.SpecText(0)} … {w.SpecText(Math.Max(0, w.Nz - 1))}"
                    + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
                d.AxisRanges.Add((w.SpecAxisName(), range));
            }
            string bunit = string.IsNullOrEmpty(_meta.Bunit) ? "" : " " + _meta.Bunit;
            d.CbMin = FormatColorbarValue(_meta.ValueAtNormalized(ViewModel.WindowLo));
            d.CbMax = FormatColorbarValue(_meta.ValueAtNormalized(ViewModel.WindowHi)) + bunit;
        }
        else
        {
            d.Facts = $"{_volNx}×{_volNy} · synthetic";
            d.CbMin = "0";
            d.CbMax = "1";
        }
        return d;
    }

    /// <summary>
    /// Build the box + WCS captions for the export camera at the export's (4:3) aspect into a transient
    /// off-screen canvas, and rasterize it to premultiplied BGRA. Done fresh (not from the live overlay)
    /// so the wireframe/captions match the fixed-aspect, pulled-back export render exactly.
    /// </summary>
    private async Task<(byte[] Pixels, int W, int H)?> RenderExportOverlayAsync(int w, int h, float az, float el, float dist)
    {
        var canvas = new Canvas { Width = w, Height = h };
        var frame = new CubeAxesOverlay.Frame();
        CubeAxesOverlay.Build(frame, az, el, dist, ViewModel.SpectralScale, _volNx, _volNy, _meta, w, h);

        double edgeThickness = Math.Max(1.0, w / 1600.0);
        var edgeBrush = ArgbBrush(0x66, 0x9F, 0xC4, 0xE8);
        foreach (var (a, b) in frame.Edges)
        {
            if (!a.Visible || !b.Visible) continue;
            canvas.Children.Add(new Line
            {
                Stroke = edgeBrush, StrokeThickness = edgeThickness,
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
            });
        }

        if (_captionsOn)
        {
            double fontSize = Math.Max(11.0, w * 0.011);
            var mono = new FontFamily("Consolas");
            foreach (var cap in frame.Captions)
            {
                if (!cap.At.Visible || string.IsNullOrEmpty(cap.Text)) continue;
                var weight = cap.IsAxisName
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal;
                var color = cap.IsAxisName ? ArgbColor(0xFF, 0x73, 0xD9, 0xFF) : ArgbColor(0xF5, 0xFF, 0xFF, 0xFF);
                var main = new TextBlock { Text = cap.Text, FontFamily = mono, FontSize = fontSize, FontWeight = weight, Foreground = new SolidColorBrush(color) };
                var shadow = new TextBlock { Text = cap.Text, FontFamily = mono, FontSize = fontSize, FontWeight = weight, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) };
                main.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double tw = main.DesiredSize.Width, th = main.DesiredSize.Height;
                double x = Math.Clamp(cap.At.X - tw / 2, 6, Math.Max(6, w - tw - 6));
                double y = Math.Clamp(cap.At.Y - th / 2, 6, Math.Max(6, h - th - 6));
                Canvas.SetLeft(shadow, x + 1); Canvas.SetTop(shadow, y + 1);
                Canvas.SetLeft(main, x); Canvas.SetTop(main, y);
                canvas.Children.Add(shadow);
                canvas.Children.Add(main);
            }
        }

        ExportHost.Children.Add(canvas);
        Canvas.SetLeft(canvas, -100000);
        canvas.UpdateLayout();
        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(canvas, w, h);
            int ow = rtb.PixelWidth, oh = rtb.PixelHeight;
            if (ow <= 0 || oh <= 0) return null;
            var buf = (await rtb.GetPixelsAsync()).ToArray();
            return (buf, ow, oh);
        }
        catch
        {
            return null;
        }
        finally
        {
            ExportHost.Children.Remove(canvas);
        }
    }

    /// <summary>
    /// Alpha-composite a premultiplied BGRA overlay (ow×oh) over an opaque BGRA base (w×h), in place,
    /// nearest-neighbor scaling the overlay when its size differs (so the box/captions are never dropped).
    /// </summary>
    private static void CompositeOverScaled(byte[] baseBgra, int w, int h, byte[] ov, int ow, int oh)
    {
        for (int y = 0; y < h; y++)
        {
            int oy = oh == h ? y : (int)((long)y * oh / h);
            for (int x = 0; x < w; x++)
            {
                int ox = ow == w ? x : (int)((long)x * ow / w);
                int si = (oy * ow + ox) * 4;
                int di = (y * w + x) * 4;
                byte oa = ov[si + 3];
                if (oa == 0) { baseBgra[di + 3] = 255; continue; }
                int ia = 255 - oa;
                baseBgra[di + 0] = (byte)Math.Min(255, ov[si + 0] + (baseBgra[di + 0] * ia + 127) / 255);
                baseBgra[di + 1] = (byte)Math.Min(255, ov[si + 1] + (baseBgra[di + 1] * ia + 127) / 255);
                baseBgra[di + 2] = (byte)Math.Min(255, ov[si + 2] + (baseBgra[di + 2] * ia + 127) / 255);
                baseBgra[di + 3] = 255;
            }
        }
    }

    private string ExportBaseName()
    {
        var name = !string.IsNullOrEmpty(_meta?.Object) ? _meta!.Object
            : (string.IsNullOrEmpty(_cubeName) ? "cube" : _cubeName);
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name + "_cube";
    }
}
