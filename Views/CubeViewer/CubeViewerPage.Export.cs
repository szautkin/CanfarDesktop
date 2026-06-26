using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Figure export for the Cube Viewer. The interactive path opens a modal with a live preview +
/// style controls (<see cref="CubeExportDialog"/>); the MCP path renders straight to a file. Both
/// share: capture the volume once as a TRANSPARENT 4:3 snapshot (1.3× pull-back), build the themed
/// <see cref="CubeExportPlate"/> (which draws the box + captions live), and rasterize the plate.
/// </summary>
public sealed partial class CubeViewerPage
{
    private bool _exporting;

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_volume is null) { StatusText.Text = "Open a cube first."; return; }
        if (_exporting) return;
        _exporting = true;
        try
        {
            var cap = await CaptureExportFrameAsync();
            if (cap is null) { StatusText.Text = "Export capture failed (is the cube viewer visible?)"; return; }

            var dialog = new CubeExportDialog { XamlRoot = XamlRoot };
            dialog.Initialize(cap.Value.Frame, cap.Value.W, cap.Value.H, BuildPlateData(), ExportBaseName());
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed: " + ex.Message;
        }
        finally
        {
            _exporting = false;
        }
    }

    /// <summary>
    /// Capture the volume as a transparent 4:3 snapshot (1400×1050) with the export pull-back. The
    /// box + captions are NOT baked in — the plate draws them live so they re-theme with the figure.
    /// </summary>
    private async Task<(WriteableBitmap Frame, int W, int H)?> CaptureExportFrameAsync()
    {
        if (_volume is null) return null;

        // Slice mode exports the 2D channel image (no GPU, no box/caption overlay).
        if (ViewModel.ViewMode == CubeViewMode.Slice)
            return CaptureSliceFrame();

        // Volume mode: a transparent 4:3 GPU snapshot with the export pull-back.
        if (!_renderer.IsReady || RenderPanel.ActualWidth < 1 || RenderPanel.ActualHeight < 1) return null;
        const int w = 1400, h = 1050; // 4:3, macOS export base
        float dist = ViewModel.CameraDistance * 1.3f;
        try
        {
            _freezeRenderLoop = true;
            PushRenderState();
            _renderer.CameraDistance = dist;
            float steps = Math.Max(ViewModel.VolumeSteps, 384f);
            byte[]? volume = _renderer.RenderToBgra(w, h, steps, transparent: true);
            if (volume is null) return null;

            var wb = new WriteableBitmap(w, h);
            using (var s = wb.PixelBuffer.AsStream()) await s.WriteAsync(volume, 0, volume.Length);
            wb.Invalidate();
            return (wb, w, h);
        }
        finally
        {
            _freezeRenderLoop = false;
        }
    }

    /// <summary>Render the current channel as the export frame (2D slice; native res shown scaled).</summary>
    private (WriteableBitmap Frame, int W, int H)? CaptureSliceFrame()
    {
        if (_volume is null) return null;
        int nx = _volume.Nx, ny = _volume.Ny;
        var lut = CubeColormaps.Build(_currentColormap);
        var buf = new byte[(long)nx * ny * 4];
        CubeSliceRenderer.RenderPlane(_volume, ViewModel.Channel, ViewModel.WindowLo, ViewModel.WindowHi, ViewModel.Stretch, lut, buf);

        var wb = new WriteableBitmap(nx, ny);
        using (var s = wb.PixelBuffer.AsStream()) s.Write(buf, 0, buf.Length);
        wb.Invalidate();

        // Display the (native) slice scaled so its longest spatial axis is ~1100 px.
        double k = 1100.0 / Math.Max(nx, ny);
        int dw = Math.Max(1, (int)Math.Round(nx * k)), dh = Math.Max(1, (int)Math.Round(ny * k));
        return (wb, dw, dh);
    }

    /// <summary>MCP entry: export the current view to a PNG/PDF path (no modal), default style.</summary>
    public async Task<string?> ExportCubeToPathAsync(string path, string format, int scale, bool dark)
    {
        if (_exporting) return "an export is already in progress";
        if (_volume is null) return "no cube is loaded";

        bool pdf = (format ?? "").Trim().Equals("pdf", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return "path must be a full (rooted) file path";
        string full;
        try { full = Path.GetFullPath(path); } catch { return "invalid path"; }
        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (pdf && ext != ".pdf") return "path must end in .pdf for a PDF export";
        if (!pdf && ext != ".png") return "path must end in .png for a PNG export";

        _exporting = true;
        CubeExportPlate? plate = null;
        try
        {
            var cap = await CaptureExportFrameAsync();
            if (cap is null) return "capture failed (the cube viewer must be visible)";

            plate = new CubeExportPlate();
            plate.Populate(cap.Value.Frame, cap.Value.W, cap.Value.H, BuildPlateData(),
                new CubeExportPlate.PlateStyle { Dark = dark, Font = "sans", TextColor = "auto", TextScale = 1.0, Annotate = true, Transparent = false });
            ExportHost.Children.Add(plate);
            Canvas.SetLeft(plate, -100000);
            plate.UpdateLayout();

            int sc = Math.Clamp(scale, 1, 4);
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(plate, (int)Math.Ceiling(plate.ActualWidth * sc), (int)Math.Ceiling(plate.ActualHeight * sc));
            int rw = rtb.PixelWidth, rh = rtb.PixelHeight;
            byte[] buf = (await rtb.GetPixelsAsync()).ToArray();
            if (rw <= 0 || rh <= 0 || buf.Length < (long)rw * rh * 4) return "plate rasterization failed";

            using (var fs = new FileStream(full, FileMode.Create))
            {
                if (pdf)
                {
                    PdfImageWriter.Write(fs, PdfImageWriter.BgraToRgb(buf, rw, rh), rw, rh);
                }
                else
                {
                    using var ras = fs.AsRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        (uint)rw, (uint)rh, 96, 96, buf);
                    await encoder.FlushAsync();
                }
            }
            StatusText.Text = "Saved " + Path.GetFileName(full);
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
            if (plate is not null) ExportHost.Children.Remove(plate);
        }
    }

    /// <summary>Build the plate's content (text + colorbar + the camera/metadata for the live overlay).</summary>
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
            ModeText = "Resident",
            Az = ViewModel.CameraAzimuth,
            El = ViewModel.CameraElevation,
            Dist = ViewModel.CameraDistance * 1.3f,
            SpectralScale = ViewModel.SpectralScale,
            VolNx = _volNx,
            VolNy = _volNy,
            Meta = _meta,
            // Box + captions only make sense over the 3D volume, not the flat slice.
            CaptionsOn = ViewModel.ViewMode == CubeViewMode.Volume && _captionsOn,
        };

        if (_meta is not null)
        {
            var w = _meta.Wcs;
            d.Dims = _meta.DimensionsText;
            d.NanText = _meta.NanText;
            d.AxisRanges.Add((w.LonName, $"{w.LonText(0)} … {w.LonText(Math.Max(0, w.Nx - 1))}"));
            d.AxisRanges.Add((w.LatName, $"{w.LatText(0)} … {w.LatText(Math.Max(0, w.Ny - 1))}"));
            if (w.Nz > 1)
            {
                string unit = w.SpecUnitDisplay();
                d.AxisRanges.Add((w.SpecAxisName(),
                    $"{w.SpecText(0)} … {w.SpecText(Math.Max(0, w.Nz - 1))}" + (string.IsNullOrEmpty(unit) ? "" : " " + unit)));
            }
            string bunit = string.IsNullOrEmpty(_meta.Bunit) ? "" : " " + _meta.Bunit;
            d.CbMin = FormatColorbarValue(_meta.ValueAtNormalized(ViewModel.WindowLo));
            d.CbMax = FormatColorbarValue(_meta.ValueAtNormalized(ViewModel.WindowHi)) + bunit;

            int nz = _volume?.Nz ?? w.Nz;
            if (nz > 1)
            {
                string spec = w.HasSpectral ? $" · {w.SpecText(ViewModel.Channel)} {w.SpecUnitDisplay()}".TrimEnd() : "";
                d.ChannelText = $"CH {ViewModel.Channel}/{Math.Max(0, nz - 1)}{spec}";
            }
        }
        else
        {
            d.Dims = $"{_volNx}×{_volNy}";
            d.NanText = "";
            d.ModeText = "Synthetic";
            d.CbMin = "0";
            d.CbMax = "1";
        }
        return d;
    }

    private string ExportBaseName()
    {
        var name = !string.IsNullOrEmpty(_meta?.Object) ? _meta!.Object
            : (string.IsNullOrEmpty(_cubeName) ? "cube" : _cubeName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name + "_cube";
    }
}
