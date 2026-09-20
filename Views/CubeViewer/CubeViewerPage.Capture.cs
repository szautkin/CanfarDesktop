using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.ViewModels.CubeViewer;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// What an agent sees when it looks at the cube viewer.
///
/// The same distinction the FITS viewer draws: <c>export_cube_figure</c> writes a plate for a paper,
/// this shows what is on the screen now. It reuses the viewer's own frame capture and its export plate
/// with the chrome switched off, so there is one way to draw the picture and one way to draw the marks
/// on it.
/// </summary>
public sealed partial class CubeViewerPage
{
    /// <summary>
    /// Capture what this viewer is showing.
    ///
    /// <paramref name="request"/>'s view is null for "whichever mode the viewer is in", which is the
    /// honest default; naming one captures that one whatever is on screen.
    /// </summary>
    internal async Task<ViewerCapture> CaptureAsync(ViewerCaptureRequest request)
    {
        if (_volume is null) return ViewerCapture.Unavailable("no cube is loaded");
        if (_exporting) return ViewerCapture.Unavailable("an export is already in progress");

        var wanted = request.View
            ?? (ViewModel.ViewMode == CubeViewMode.Slice ? CubeCaptureView.Slice : CubeCaptureView.Volume);

        _exporting = true;
        CubeExportPlate? plate = null;
        try
        {
            var frame = wanted == CubeCaptureView.Slice
                ? CaptureSliceFrame()
                : await CaptureVolumeFrameAsync();

            if (frame is null)
                return ViewerCapture.Unavailable(wanted == CubeCaptureView.Slice
                    ? "the slice could not be rendered"
                    : "the volume could not be captured (the cube viewer must be visible)");

            var size = AgentCapture.Fit(
                frame.Value.W, frame.Value.H,
                request.MaxPixels ?? AgentCapture.DefaultMaxPixels,
                request.MaxBytes ?? AgentCapture.DefaultMaxBytes);

            // The plate without its chrome: the picture, and the marks over it.
            var style = new CubeExportPlate.PlateStyle
            {
                Dark = true, Font = "sans", TextColor = "auto", TextScale = 1.0,
                Annotate = false, ShowMarks = true, Transparent = false,
            };

            plate = new CubeExportPlate();
            plate.Populate(frame.Value.Frame, frame.Value.W, frame.Value.H,
                BuildPlateData(asSlice: wanted == CubeCaptureView.Slice), style);
            ExportHost.Children.Add(plate);
            Canvas.SetLeft(plate, -100000);
            plate.UpdateLayout();

            // Rasterised at the capture's own scale, so the bound is on the PICTURE rather than on the
            // plate's layout size.
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(plate,
                Math.Max(1, (int)Math.Ceiling(plate.ActualWidth * size.Scale)),
                Math.Max(1, (int)Math.Ceiling(plate.ActualHeight * size.Scale)));

            int rw = rtb.PixelWidth, rh = rtb.PixelHeight;
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            if (rw <= 0 || rh <= 0 || pixels.Length < (long)rw * rh * 4)
                return ViewerCapture.Unavailable("the capture could not be rasterized");

            var png = await PngBytes.FromBgraAsync(pixels, rw, rh);

            return new ViewerCapture(
                Captured: true, png, "image/png", rw, rh,
                Transform: wanted == CubeCaptureView.Slice ? SliceCaptureTransform(rw) : null,
                Describes: Describe(wanted),
                Note: NoteFor(wanted, size.Note));
        }
        catch (Exception ex)
        {
            return ViewerCapture.Unavailable(ex.Message);
        }
        finally
        {
            _freezeRenderLoop = false;
            _exporting = false;
            if (plate is not null) ExportHost.Children.Remove(plate);
        }
    }

    /// <summary>
    /// The volume as it is on screen — no export pull-back.
    ///
    /// A figure pulls the camera back 1.3x so the box never clips while someone flips through
    /// orientations. A capture must not: the question it answers is what the person is looking at, and
    /// a picture framed differently from their screen is the wrong answer to it.
    /// </summary>
    private async Task<(WriteableBitmap Frame, int W, int H)?> CaptureVolumeFrameAsync()
    {
        if (!_renderer.IsReady || RenderPanel.ActualWidth < 1 || RenderPanel.ActualHeight < 1) return null;

        const int w = 1400, h = 1050;
        try
        {
            _freezeRenderLoop = true;
            PushRenderState();
            var steps = Math.Max(ViewModel.VolumeSteps, 384f);
            var volume = _renderer.RenderToBgra(w, h, steps, transparent: true);
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

    /// <summary>
    /// A slice capture's transform, expressed in the coordinates <c>probe_cube_spectrum</c> takes —
    /// 0-based spaxels of the loaded volume.
    ///
    /// Two scalings compose into one. The plane may be rendered at its NATIVE resolution, which can be
    /// finer than the down-sampled volume the probe indexes; both are plain scalings from the same
    /// origin, so capture → native → volume collapses to a single ratio and the agent never has to
    /// know there were two.
    /// </summary>
    private CaptureTransform? SliceCaptureTransform(int captureWidth)
    {
        if (_volume is null || _volume.Nx < 1 || captureWidth < 1) return null;
        return new CaptureTransform(0, 0, captureWidth / (double)_volume.Nx);
    }

    private string Describe(CubeCaptureView view)
    {
        if (_volume is null) return string.Empty;

        return view == CubeCaptureView.Slice
            ? $"the slice at channel {ViewModel.Channel} of {_volume.Nz}, {_volume.Nx}×{_volume.Ny} spaxels"
            : $"the {_volume.Nx}×{_volume.Ny}×{_volume.Nz} volume as currently orbited";
    }

    /// <summary>
    /// Why the volume carries no transform, said plainly. A projection through a rotated box is not a
    /// scale and an offset, and handing back one that is subtly wrong is worse than saying so.
    /// </summary>
    private static string? NoteFor(CubeCaptureView view, string? sizeNote)
    {
        var volumeNote = view == CubeCaptureView.Volume
            ? "no transform: the volume is a projection through a rotated box, so a position in it does " +
              "not map to one voxel. Capture the slice for coordinates you can act on."
            : null;

        return (sizeNote, volumeNote) switch
        {
            (null, null) => null,
            (null, var v) => v,
            (var s, null) => s,
            var (s, v) => $"{s}; {v}",
        };
    }
}
