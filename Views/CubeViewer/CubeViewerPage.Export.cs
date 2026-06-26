using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
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
        CubeExportPlate? plate = null;
        try
        {
            int w = Math.Max(1, (int)(RenderPanel.ActualWidth * opt.Scale));
            int h = Math.Max(1, (int)(RenderPanel.ActualHeight * opt.Scale));
            StatusText.Text = $"Exporting {w}×{h}…";

            // Freeze the loop so the volume + overlay share one camera (no auto-orbit drift).
            _freezeRenderLoop = true;
            PushRenderState();
            UpdateOverlay();
            float steps = Math.Max(ViewModel.VolumeSteps, 384f);
            byte[]? volume = _renderer.RenderToBgra(w, h, steps);
            var overlay = volume is null ? null : await RenderOverlayAsync(w, h);
            _freezeRenderLoop = false;

            if (volume is null)
            {
                StatusText.Text = "Export failed" + (opt.Scale >= 4 ? " (try 2×)" : "")
                    + ": " + (_renderer.LastError ?? "render");
                return;
            }
            // Composite the box + captions over the volume, scaling the overlay if
            // RenderTargetBitmap didn't produce exactly w×h (it caps large requests).
            if (overlay is not null)
                CompositeOverScaled(volume, w, h, overlay.Value.Pixels, overlay.Value.W, overlay.Value.H);

            // Frame bitmap (the composited render).
            var frame = new WriteableBitmap(w, h);
            using (var s = frame.PixelBuffer.AsStream()) await s.WriteAsync(volume, 0, volume.Length);
            frame.Invalidate();

            // Compose + rasterize the figure plate off-screen.
            plate = new CubeExportPlate();
            plate.Populate(frame, w, h, BuildPlateData(), opt.Dark);
            ExportHost.Children.Add(plate);
            Canvas.SetLeft(plate, -100000); // off-screen so it never shows in the live UI
            plate.UpdateLayout();

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(plate, (int)Math.Ceiling(plate.ActualWidth), (int)Math.Ceiling(plate.ActualHeight));
            int pw = rtb.PixelWidth, ph = rtb.PixelHeight;
            byte[] plateBuf = (await rtb.GetPixelsAsync()).ToArray();
            ExportHost.Children.Remove(plate);
            plate = null;

            if (pw <= 0 || ph <= 0 || plateBuf.Length < (long)pw * ph * 4)
            {
                StatusText.Text = "Export failed: plate rasterization";
                return;
            }

            // Save.
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
            if (plate is not null) ExportHost.Children.Remove(plate);
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

    /// <summary>Rasterize the overlay canvas (box + captions) as premultiplied BGRA, returning its actual pixel size.</summary>
    private async Task<(byte[] Pixels, int W, int H)?> RenderOverlayAsync(int w, int h)
    {
        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(OverlayCanvas, w, h);
            int ow = rtb.PixelWidth, oh = rtb.PixelHeight;
            if (ow <= 0 || oh <= 0) return null;
            var buf = (await rtb.GetPixelsAsync()).ToArray();
            return (buf, ow, oh);
        }
        catch
        {
            return null;
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
