using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Figure export for the Cube Viewer: renders the volume offscreen on the GPU at a 2×/4×
/// scale of the current view, composites the box + WCS caption overlay (re-rasterized crisply
/// at export resolution via <see cref="RenderTargetBitmap"/>), and writes a PNG. The Windows
/// analogue of the macOS "Export figure…" (a full title/colorbar plate is a later increment).
/// </summary>
public sealed partial class CubeViewerPage
{
    private bool _exporting;

    private async void OnExport2x(object sender, RoutedEventArgs e) => await ExportPngAsync(2);
    private async void OnExport4x(object sender, RoutedEventArgs e) => await ExportPngAsync(4);

    private async Task ExportPngAsync(int scale)
    {
        if (_exporting || !_renderer.IsReady) return;
        _exporting = true;
        var prevStatus = StatusText.Text;
        try
        {
            int w = Math.Max(1, (int)(RenderPanel.ActualWidth * scale));
            int h = Math.Max(1, (int)(RenderPanel.ActualHeight * scale));
            StatusText.Text = $"Exporting {w}×{h}…";

            // Freeze the render loop for the capture so the volume and the overlay are
            // projected for ONE camera (auto-orbit must not drift between them), and make
            // the overlay reflect that frozen camera.
            _freezeRenderLoop = true;
            PushRenderState();
            UpdateOverlay();

            // 1) Volume render (GPU) at export resolution, high-quality step count.
            float steps = Math.Max(ViewModel.VolumeSteps, 384f);
            byte[]? volume = _renderer.RenderToBgra(w, h, steps);

            // 2) Overlay (box + captions) rasterized at export resolution.
            byte[]? overlay = volume is null ? null : await RenderOverlayBgraAsync(w, h);

            // Capture done — let the live view resume while the save dialog is open.
            _freezeRenderLoop = false;

            if (volume is null)
            {
                StatusText.Text = "Export failed" + (scale >= 4 ? " (try 2×)" : "")
                    + ": " + (_renderer.LastError ?? "render");
                return;
            }

            // 3) Composite the overlay over the volume (opaque output).
            if (overlay is not null && overlay.Length == volume.Length)
                CompositeOver(volume, overlay);

            // 4) Save via picker + PNG encoder.
            var hwnd = WindowHelper.ActiveWindows.Count > 0
                ? WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]) : nint.Zero;
            if (hwnd == nint.Zero) { StatusText.Text = "Export failed: no window handle"; return; }

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedFileName = ExportBaseName();
            picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });

            var file = await picker.PickSaveFileAsync();
            if (file is null) { StatusText.Text = prevStatus; return; }

            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    (uint)w, (uint)h, 96, 96, volume);
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

    /// <summary>Rasterize the overlay canvas (box + captions) at export resolution as premultiplied BGRA.</summary>
    private async Task<byte[]?> RenderOverlayBgraAsync(int w, int h)
    {
        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(OverlayCanvas, w, h);
            // RenderTargetBitmap caps very large requests; if the result isn't the exact size we
            // asked for, the pixel grids won't line up — skip the overlay rather than misalign it.
            if (rtb.PixelWidth != w || rtb.PixelHeight != h) return null;
            var buf = await rtb.GetPixelsAsync();
            return buf.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Alpha-composite a premultiplied BGRA overlay over an opaque BGRA base, in place.</summary>
    private static void CompositeOver(byte[] baseBgra, byte[] overlayBgra)
    {
        for (int i = 0; i < baseBgra.Length; i += 4)
        {
            byte oa = overlayBgra[i + 3];
            if (oa == 0) { baseBgra[i + 3] = 255; continue; }
            int ia = 255 - oa;
            // overlay is premultiplied: out = overlay + base·(1−a), with rounded /255.
            baseBgra[i + 0] = (byte)Math.Min(255, overlayBgra[i + 0] + (baseBgra[i + 0] * ia + 127) / 255);
            baseBgra[i + 1] = (byte)Math.Min(255, overlayBgra[i + 1] + (baseBgra[i + 1] * ia + 127) / 255);
            baseBgra[i + 2] = (byte)Math.Min(255, overlayBgra[i + 2] + (baseBgra[i + 2] * ia + 127) / 255);
            baseBgra[i + 3] = 255;
        }
    }

    private string ExportBaseName()
    {
        var name = !string.IsNullOrEmpty(_meta?.Object) ? _meta!.Object : ViewModel.VolumeName;
        if (string.IsNullOrWhiteSpace(name)) name = "cube";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name + "_cube";
    }
}
