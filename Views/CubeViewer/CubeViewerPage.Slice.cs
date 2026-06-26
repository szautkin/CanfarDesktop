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

    // Pixel dims of the slice bitmap. When a native-plane source is available the on-screen slice is
    // drawn at NATIVE FITS resolution (full detail); otherwise at the GPU-down-sampled volume's dims.
    private int _sliceDispNx, _sliceDispNy;
    private bool _useNativeSlice;
    private const int SliceDisplayCap = 2048; // cap the on-screen slice bitmap's longest axis

    // Coalesce-to-latest scrubbing: a burst of slider ValueChanged events collapses to a single render
    // of the most recent channel, so dragging a large native cube can't pile up synchronous renders.
    private bool _slicePending, _sliceRendering;

    // The colormap LUT is stable across frames — cache it so playback/scrubbing don't rebuild it per frame.
    private byte[]? _sliceLut;
    private CubeColormap _sliceLutKey;

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
        // The channel scrubber + playback live in BOTH modes when the cube has channels: in slice
        // mode it shows the 2D plane, in volume mode it drives the slice-plane marker.
        SliceBar.Visibility = (_volume?.Nz ?? 0) > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (slice)
        {
            EnsureSliceBitmap();
            RenderSlice();
            UpdateChannelLabel();
        }
        else
        {
            // Probe panel is slice-only (it needs the 2D image to click).
            SpectrumPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ── Per-volume setup + rendering ───────────────────────────────────────────

    private void InitSliceForVolume()
    {
        if (_volume is null) return;
        int nz = _volume.Nz;

        // Draw the on-screen slice at native resolution when the source is available and not enormous;
        // otherwise fall back to the down-sampled volume plane.
        if (_nativeSource is { } src && Math.Max(src.Nx, src.Ny) <= SliceDisplayCap)
        {
            _useNativeSlice = true;
            _sliceDispNx = src.Nx;
            _sliceDispNy = src.Ny;
        }
        else
        {
            _useNativeSlice = false;
            _sliceDispNx = _volume.Nx;
            _sliceDispNy = _volume.Ny;
        }

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
        SliceBar.Visibility = nz > 1 ? Visibility.Visible : Visibility.Collapsed;

        EnsureSliceBitmap();
        UpdateChannelLabel();
        if (ViewModel.ViewMode == CubeViewMode.Slice) RenderSlice();
    }

    private void EnsureSliceBitmap()
    {
        if (_volume is null) return;
        int nx = _sliceDispNx > 0 ? _sliceDispNx : _volume.Nx;
        int ny = _sliceDispNy > 0 ? _sliceDispNy : _volume.Ny;
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
        if (_sliceLut is null || _sliceLutKey != _currentColormap)
        {
            _sliceLut = CubeColormaps.Build(_currentColormap);
            _sliceLutKey = _currentColormap;
        }
        var lut = _sliceLut;

        // Native-resolution plane when available, else the down-sampled volume plane.
        bool rendered = false;
        if (_useNativeSlice && _nativeSource is { } src)
        {
            int nativeCh = MapNativeChannel(ViewModel.Channel, _volume.Nz, src.Nz);
            var n = src.ReadChannel(nativeCh, _meta?.NormLo ?? 0, _meta?.NormHi ?? 1);
            if (n is { } v && v.Nx == _sliceDispNx && v.Ny == _sliceDispNy)
            {
                CubeSliceRenderer.RenderPlaneNorm(
                    v.Norm, v.Nx, v.Ny, ViewModel.WindowLo, ViewModel.WindowHi, ViewModel.Stretch, lut, _sliceBuf);
                rendered = true;
            }
            else
            {
                DisableNativeSlice(); // an I/O error mid-session → drop to down-sampled (re-renders)
                return;
            }
        }
        if (!rendered)
        {
            CubeSliceRenderer.RenderPlane(
                _volume, ViewModel.Channel, ViewModel.WindowLo, ViewModel.WindowHi,
                ViewModel.Stretch, lut, _sliceBuf);
        }

        using (var s = _sliceBitmap.PixelBuffer.AsStream()) s.Write(_sliceBuf, 0, _sliceBuf.Length);
        _sliceBitmap.Invalidate();

        if (SpectrumPanel.Visibility == Visibility.Visible) DrawSpectrum();
    }

    /// <summary>Map a down-sampled channel index to the matching native channel (endpoints exact).</summary>
    private static int MapNativeChannel(int ch, int downNz, int origNz)
        => (downNz > 1 && origNz > 1)
            ? Math.Clamp((int)Math.Round((double)ch / (downNz - 1) * (origNz - 1)), 0, origNz - 1)
            : Math.Clamp(ch, 0, Math.Max(0, origNz - 1));

    /// <summary>Native slice read failed → release the source and switch to the down-sampled plane.</summary>
    private void DisableNativeSlice()
    {
        _useNativeSlice = false;
        _nativeSource?.Dispose();
        _nativeSource = null;
        _sliceDispNx = _volume?.Nx ?? _sliceDispNx;
        _sliceDispNy = _volume?.Ny ?? _sliceDispNy;
        EnsureSliceBitmap();
        RenderSlice();
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
        UpdateChannelLabel();
        // Slice mode re-renders the 2D plane; volume mode's slice-plane marker updates in the render loop.
        if (ViewModel.ViewMode == CubeViewMode.Slice) RequestSliceRender();
    }

    /// <summary>
    /// Coalesce a burst of scrub events into a single render of the LATEST channel. RenderSlice always
    /// reads the current ViewModel.Channel, so intermediate channels crossed during a fast drag are
    /// absorbed for free; running at Low priority keeps the thumb tracking the pointer.
    /// </summary>
    private void RequestSliceRender()
    {
        if (_sliceRendering) { _slicePending = true; return; } // mid-render → remember to redraw the latest
        if (_slicePending) return;                             // a render is already queued
        _slicePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _slicePending = false;
            _sliceRendering = true;
            try { RenderSlice(); }
            finally { _sliceRendering = false; }
            if (_slicePending) RequestSliceRender(); // a newer channel arrived mid-render → draw once more
        });
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
        // Stop if this tab is no longer the active/shown one (TabView unload or cross-module nav),
        // so playback never runs off-screen. Works in both volume and slice modes.
        if (_closed || !_active)
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
        UpdateChannelLabel();
        // Slice mode renders each frame; volume mode animates the slice-plane via the render loop.
        if (ViewModel.ViewMode == CubeViewMode.Slice) RenderSlice();
    }

    // ── Spectrum probe ─────────────────────────────────────────────────────────

    private void OnSlicePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_volume is null) return;
        var px = MapToPixel(e.GetCurrentPoint(SliceImage).Position);
        if (px is null) return;
        _probeX = px.Value.x;
        _probeY = px.Value.y;
        // The spectrum is sampled from the (down-sampled) volume, so map the displayed pixel to its voxel.
        int sx = MapDispToVolume(_probeX, _sliceDispNx, _volume.Nx);
        int sy = MapDispToVolume(_probeY, _sliceDispNy, _volume.Ny);
        _probeSpectrum = CubeSliceRenderer.Spectrum(
            _volume, sx, sy, _meta?.NormLo ?? 0, _meta?.NormHi ?? 1);
        SpectrumTitle.Text = $"Spectrum @ ({_probeX}, {_probeY})";
        SpectrumPanel.Visibility = Visibility.Visible;
        DrawSpectrum();
    }

    /// <summary>Map a pointer position on the (Uniform-fit) slice image to a 0-based display pixel (x,y).</summary>
    private (int x, int y)? MapToPixel(Point p)
    {
        if (_volume is null) return null;
        double aw = SliceImage.ActualWidth, ah = SliceImage.ActualHeight;
        int nx = _sliceDispNx > 0 ? _sliceDispNx : _volume.Nx;
        int ny = _sliceDispNy > 0 ? _sliceDispNy : _volume.Ny;
        if (aw <= 0 || ah <= 0) return null;
        double scale = Math.Min(aw / nx, ah / ny);
        double ox = (aw - nx * scale) / 2, oy = (ah - ny * scale) / 2;
        int x = (int)Math.Floor((p.X - ox) / scale);
        int y = (int)Math.Floor((p.Y - oy) / scale);
        if (x < 0 || y < 0 || x >= nx || y >= ny) return null;
        return (x, y);
    }

    /// <summary>Map a displayed slice pixel to the matching voxel index in the down-sampled volume.</summary>
    private static int MapDispToVolume(int p, int dispN, int volN)
        => dispN > 0 ? Math.Clamp((int)((long)p * volN / dispN), 0, Math.Max(0, volN - 1)) : Math.Clamp(p, 0, Math.Max(0, volN - 1));

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
