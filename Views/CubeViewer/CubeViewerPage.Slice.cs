using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Slice mode (2D quantitative view) for the Cube Viewer: renders one spectral channel at native
/// resolution with a channel scrubber + playback, and a click-to-probe spectrum (flux vs channel)
/// at a spaxel. Shares window/stretch/colormap with the volume. The Windows analogue of the macOS
/// CubeSliceView + CubeSpectrumView.
/// </summary>
public sealed partial class CubeViewerPage
{
    private WriteableBitmap? _sliceBitmap;
    private byte[]? _sliceBuf;
    private DispatcherTimer? _playTimer;
    private bool _suppressChannel;
    private int _probeX = -1, _probeY = -1;
    private float[]? _probeSpectrum;

    private static readonly Color SpectrumLineColor = Color.FromArgb(0xFF, 0x73, 0xD9, 0xFF);
    private static readonly Color SpectrumMarkColor = Color.FromArgb(0xFF, 0xFF, 0xA5, 0x3C);

    // ── Mode switching ─────────────────────────────────────────────────────────

    private void OnVolumeModeClick(object sender, RoutedEventArgs e) => SetViewMode(CubeViewMode.Volume);
    private void OnSliceModeClick(object sender, RoutedEventArgs e) => SetViewMode(CubeViewMode.Slice);

    private void SetViewMode(CubeViewMode mode)
    {
        ViewModel.ViewMode = mode;
        bool slice = mode == CubeViewMode.Slice;

        VolumeModeButton.IsChecked = !slice;
        SliceModeButton.IsChecked = slice;

        var showVol = slice ? Visibility.Collapsed : Visibility.Visible;
        var showSlice = slice ? Visibility.Visible : Visibility.Collapsed;

        // Collapsing RenderPanel pauses the GPU loop (its ActualWidth → 0).
        RenderPanel.Visibility = showVol;
        OverlayCanvas.Visibility = showVol;
        VolumeSection.Visibility = showVol;
        SliceImage.Visibility = showSlice;
        SliceBar.Visibility = showSlice;

        if (slice)
        {
            EnsureSliceBitmap();
            RenderSlice();
            UpdateChannelLabel();
        }
        else
        {
            StopPlayback();
            SpectrumPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ── Per-volume setup + rendering ───────────────────────────────────────────

    private void InitSliceForVolume()
    {
        if (_volume is null) return;
        int nz = _volume.Nz;
        _suppressChannel = true;
        ChannelSlider.Maximum = Math.Max(0, nz - 1);
        int mid = nz / 2;
        ChannelSlider.Value = mid;
        ViewModel.Channel = mid;
        _suppressChannel = false;

        // Probe state must not outlive the cube it was sampled from.
        _probeSpectrum = null;
        _probeX = _probeY = -1;
        SpectrumPanel.Visibility = Visibility.Collapsed;

        EnsureSliceBitmap();
        UpdateChannelLabel();
        if (ViewModel.ViewMode == CubeViewMode.Slice) RenderSlice();
    }

    private void EnsureSliceBitmap()
    {
        if (_volume is null) return;
        int nx = _volume.Nx, ny = _volume.Ny;
        if (_sliceBitmap is null || _sliceBitmap.PixelWidth != nx || _sliceBitmap.PixelHeight != ny)
        {
            _sliceBitmap = new WriteableBitmap(nx, ny);
            _sliceBuf = new byte[(long)nx * ny * 4];
            SliceImage.Source = _sliceBitmap;
        }
    }

    private void RenderSlice()
    {
        if (_volume is null || _sliceBitmap is null || _sliceBuf is null) return;
        var lut = CubeColormaps.Build(_currentColormap);
        CubeSliceRenderer.RenderPlane(
            _volume, ViewModel.Channel, ViewModel.WindowLo, ViewModel.WindowHi,
            ViewModel.Stretch, lut, _sliceBuf);
        using (var s = _sliceBitmap.PixelBuffer.AsStream()) s.Write(_sliceBuf, 0, _sliceBuf.Length);
        _sliceBitmap.Invalidate();

        if (SpectrumPanel.Visibility == Visibility.Visible) DrawSpectrum();
    }

    /// <summary>Re-render the slice when a shared display control (window/stretch/colormap) changes.</summary>
    private void RefreshSliceIfActive()
    {
        if (_initialized && ViewModel.ViewMode == CubeViewMode.Slice) RenderSlice();
    }

    private void UpdateChannelLabel()
    {
        if (_volume is null) return;
        int c = ViewModel.Channel, nz = _volume.Nz;
        string spec = "";
        if (_meta is not null && _meta.Wcs.HasSpectral)
        {
            string unit = _meta.Wcs.SpecUnitDisplay();
            spec = " · " + _meta.Wcs.SpecText(c) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
        }
        ChannelLabel.Text = $"CH {c}/{Math.Max(0, nz - 1)}{spec}";
    }

    // ── Channel scrubber + playback ────────────────────────────────────────────

    private void OnChannelChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressChannel) return;
        ViewModel.Channel = (int)e.NewValue;
        RenderSlice();
        UpdateChannelLabel();
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsPlaying) StopPlayback(); else StartPlayback();
    }

    private void StartPlayback()
    {
        if (_volume is null || _volume.Nz < 2) return;
        if (_playTimer is null)
        {
            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / ViewModel.PlaybackFps) };
            _playTimer.Tick += (_, _) => AdvanceChannelPlayback();
        }
        _playTimer.Start();
        ViewModel.IsPlaying = true;
        PlayIcon.Glyph = ""; // pause
    }

    private void StopPlayback()
    {
        _playTimer?.Stop();
        ViewModel.IsPlaying = false;
        if (PlayIcon is not null) PlayIcon.Glyph = ""; // play
    }

    private void AdvanceChannelPlayback()
    {
        if (_volume is null) return;
        // Stop if the page was hidden (navigation toggles Visibility without raising Unloaded,
        // so the timer would otherwise keep rendering slices off-screen) or torn down.
        if (_closed || ViewModel.ViewMode != CubeViewMode.Slice || SliceImage.ActualWidth < 1)
        {
            StopPlayback();
            return;
        }
        int nz = Math.Max(1, _volume.Nz);
        int next = (ViewModel.Channel + 1) % nz;
        _suppressChannel = true;
        ChannelSlider.Value = next;
        _suppressChannel = false;
        ViewModel.Channel = next;
        RenderSlice();
        UpdateChannelLabel();
    }

    // ── Spectrum probe ─────────────────────────────────────────────────────────

    private void OnSlicePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_volume is null) return;
        var px = MapToPixel(e.GetCurrentPoint(SliceImage).Position);
        if (px is null) return;
        _probeX = px.Value.x;
        _probeY = px.Value.y;
        _probeSpectrum = CubeSliceRenderer.Spectrum(
            _volume, _probeX, _probeY, _meta?.NormLo ?? 0, _meta?.NormHi ?? 1);
        SpectrumTitle.Text = $"Spectrum @ ({_probeX}, {_probeY})";
        SpectrumPanel.Visibility = Visibility.Visible;
        DrawSpectrum();
    }

    /// <summary>Map a pointer position on the (Uniform-fit) slice image to a 0-based voxel (x,y).</summary>
    private (int x, int y)? MapToPixel(Point p)
    {
        if (_volume is null) return null;
        double aw = SliceImage.ActualWidth, ah = SliceImage.ActualHeight;
        int nx = _volume.Nx, ny = _volume.Ny;
        if (aw <= 0 || ah <= 0) return null;
        double scale = Math.Min(aw / nx, ah / ny);
        double ox = (aw - nx * scale) / 2, oy = (ah - ny * scale) / 2;
        int x = (int)Math.Floor((p.X - ox) / scale);
        int y = (int)Math.Floor((p.Y - oy) / scale);
        if (x < 0 || y < 0 || x >= nx || y >= ny) return null;
        return (x, y);
    }

    private void OnSpectrumClose(object sender, RoutedEventArgs e)
    {
        SpectrumPanel.Visibility = Visibility.Collapsed;
        _probeSpectrum = null;
    }

    private void OnSpectrumCanvasPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_volume is null || _probeSpectrum is null) return;
        double w = SpectrumCanvas.Width;
        int nz = _volume.Nz;
        var p = e.GetCurrentPoint(SpectrumCanvas).Position;
        int c = nz > 1 ? (int)Math.Round(p.X / Math.Max(1, w) * (nz - 1)) : 0;
        c = Math.Clamp(c, 0, Math.Max(0, nz - 1));
        _suppressChannel = true;
        ChannelSlider.Value = c;
        _suppressChannel = false;
        ViewModel.Channel = c;
        RenderSlice();
        UpdateChannelLabel();
    }

    private void DrawSpectrum()
    {
        SpectrumCanvas.Children.Clear();
        var sp = _probeSpectrum;
        if (sp is null) return;
        double w = SpectrumCanvas.Width, h = SpectrumCanvas.Height;

        float mn = float.MaxValue, mx = float.MinValue;
        foreach (var v in sp)
            if (float.IsFinite(v)) { if (v < mn) mn = v; if (v > mx) mx = v; }
        if (sp.Length < 2 || mn > mx) // single channel or all-masked (NaN) spaxel
        {
            SpectrumCanvas.Children.Add(new TextBlock
            {
                Text = "NO SIGNAL",
                Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)),
                FontSize = 12,
            });
            return;
        }

        double range = mx > mn ? mx - mn : 1;
        int nz = sp.Length;
        var poly = new Polyline { Stroke = new SolidColorBrush(SpectrumLineColor), StrokeThickness = 1.5 };
        var pts = new PointCollection();
        for (int z = 0; z < nz; z++)
        {
            double x = nz > 1 ? z / (double)(nz - 1) * w : 0;
            double y = float.IsFinite(sp[z]) ? h - 3 - (sp[z] - mn) / range * (h - 6) : h;
            pts.Add(new Point(x, y));
        }
        poly.Points = pts;
        SpectrumCanvas.Children.Add(poly);

        // Current-channel marker (dashed).
        double mxX = nz > 1 ? ViewModel.Channel / (double)(nz - 1) * w : 0;
        SpectrumCanvas.Children.Add(new Line
        {
            X1 = mxX, Y1 = 0, X2 = mxX, Y2 = h,
            Stroke = new SolidColorBrush(SpectrumMarkColor),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        });
    }

    // ── Keyboard shortcuts (slice mode): Space play/pause, ←/→ ±1, ⇧←/→ ±10 ──

    private void OnPlayPauseAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.ViewMode != CubeViewMode.Slice) return;
        args.Handled = true;
        OnPlayPause(this, new RoutedEventArgs());
    }

    private void OnChannelStepAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.ViewMode != CubeViewMode.Slice || _volume is null) return;
        args.Handled = true;
        int step = (sender.Modifiers & VirtualKeyModifiers.Shift) != 0 ? 10 : 1;
        if (sender.Key == VirtualKey.Left) step = -step;
        StepChannel(step);
    }

    private void StepChannel(int delta)
    {
        if (_volume is null) return;
        int nz = _volume.Nz;
        int c = Math.Clamp(ViewModel.Channel + delta, 0, Math.Max(0, nz - 1));
        _suppressChannel = true;
        ChannelSlider.Value = c;
        _suppressChannel = false;
        ViewModel.Channel = c;
        RenderSlice();
        UpdateChannelLabel();
    }
}
