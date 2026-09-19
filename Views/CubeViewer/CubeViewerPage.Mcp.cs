using Microsoft.UI.Xaml;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// MCP surface for the Cube Viewer: read the current cube + display state, drive the view
/// (mode/channel/colormap/stretch/window/render-mode), and probe a spaxel's spectrum. These run on
/// the UI thread (the MCP appliers marshal to it via the DispatcherQueue). Export-to-path lives in
/// the export partial.
/// </summary>
public sealed partial class CubeViewerPage
{
    /// <summary>A consistent snapshot of the loaded cube + display state.</summary>
    public CubeViewState GetCubeState()
    {
        // Report the RENDERABLE channel count (a large cube is downsampled to fit the GPU cap), not the
        // header NAXIS3 — so the reported nz matches the channel slider's max and the spectral axis maps
        // 1:1 to the data planes the viewer scrubs (QA/SCI-4: header said 1936 but only ~242 render).
        int nz = _volume?.Nz ?? _meta?.Nz ?? 0;
        // The channel index is in RENDER space; map through the stride so a z-downsampled cube
        // reports the true world value (SpecText evaluates the native-resolution spectral WCS).
        string spec = (_meta is not null && _meta.Wcs.HasSpectral)
            ? (_meta.Wcs.SpecText(_meta.NativeChannel(ViewModel.Channel)) + " " + _meta.Wcs.SpecUnitDisplay()).Trim()
            : "";
        var center = SliceCenterNative();
        return new CubeViewState(
            _volume is not null,
            _cubeName,
            _meta?.Object ?? "",
            _meta?.Nx ?? _volNx,
            _meta?.Ny ?? _volNy,
            nz,
            ViewModel.ViewMode.ToString(),
            ViewModel.Channel,
            spec,
            CubeColormaps.DisplayName(ViewModel.Colormap),
            ViewModel.Stretch.ToString(),
            ViewModel.Mip ? "Max-Intensity" : "Emission",
            ViewModel.WindowLo,
            ViewModel.WindowHi,
            _meta?.Bunit ?? "",
            _meta?.DataMin ?? 0,
            _meta?.DataMax ?? 0,
            ViewModel.CameraAzimuth,
            ViewModel.CameraElevation,
            ViewModel.CameraDistance,
            ViewModel.Density,
            ViewModel.SpectralScale,
            (int)ViewModel.VolumeSteps,
            BackgroundName(),
            ViewModel.ShowSlicePlane,
            _captionsOn,
            ViewModel.AutoOrbit,
            ViewModel.IsPlaying,
            // Full read parity: the info panel, slice view, spectrum panel, and opacity curve.
            Instrument: _meta?.Instrument ?? "",
            Median: _meta?.Median ?? 0,
            NanFraction: _meta?.NanFraction ?? 0,
            CutLo: _meta?.NormLo ?? 0,
            CutHi: _meta?.NormHi ?? 0,
            RenderNx: _volume?.Nx ?? 0,
            RenderNy: _volume?.Ny ?? 0,
            NativeNz: _meta?.Nz ?? nz,
            Downsampled: _meta?.IsDownsampled ?? false,
            Path: _cubePath ?? "",
            SliceZoom: _sliceZoom,
            SliceCenterX: center?.X,
            SliceCenterY: center?.Y,
            SpectrumPanelOpen: SpectrumPanel.Visibility == Visibility.Visible,
            SpectrumX: _probeX >= 0 ? _probeX : null,
            SpectrumY: _probeY >= 0 ? _probeY : null,
            TransferPoints: _transfer.Points.Select(p => new CubeTransferPoint(p.X, p.Y)).ToList());
    }

    private string BackgroundName() => BackgroundCombo.SelectedIndex switch { 1 => "black", 2 => "light", _ => "dark" };

    /// <summary>
    /// Apply view settings from MCP; each null/empty argument is left unchanged. Mirrors EVERY control the
    /// UI exposes so an agent can fully drive the viewer. Where a UI control exists we set it (its change
    /// handler updates the model + renderer); the gesture-only camera is written straight to the model
    /// (the render loop reads it each frame).
    /// </summary>
    public void ApplyCubeView(
        string? mode = null, int? channel = null, string? colormap = null, string? stretch = null,
        string? renderMode = null, double? windowLo = null, double? windowHi = null,
        double? azimuth = null, double? elevation = null, double? distance = null,
        double? density = null, double? spectralScale = null, int? steps = null,
        string? background = null, bool? showSlicePlane = null, bool? showCaptions = null,
        bool? autoOrbit = null, bool? playing = null, bool? resetCamera = null,
        string? windowPreset = null, double? sliceZoom = null,
        int? sliceCenterX = null, int? sliceCenterY = null, bool? resetSliceView = null)
    {
        if (!string.IsNullOrEmpty(mode))
            SetViewMode(mode.Equals("slice", StringComparison.OrdinalIgnoreCase) ? CubeViewMode.Slice : CubeViewMode.Volume);

        if (!string.IsNullOrEmpty(colormap) && Enum.TryParse<CubeColormap>(colormap, true, out var cm))
            ColormapCombo.SelectedIndex = (int)cm; // fires OnColormapChanged

        if (!string.IsNullOrEmpty(stretch) && Enum.TryParse<ImageStretcher.StretchMode>(stretch, true, out var sm))
            StretchCombo.SelectedIndex = (int)sm; // fires OnStretchChanged

        if (!string.IsNullOrEmpty(renderMode))
            RenderModeCombo.SelectedIndex = renderMode.Contains("max", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        if (windowLo is not null || windowHi is not null)
            SetWindow((float)(windowLo ?? ViewModel.WindowLo), (float)(windowHi ?? ViewModel.WindowHi));

        // Window presets — the Min/Max and 99% buttons.
        if (!string.IsNullOrEmpty(windowPreset))
            SetWindow(0f, windowPreset.Equals("p99", StringComparison.OrdinalIgnoreCase) ? 0.99f : 1f);

        // Slice navigation — the wheel-zoom / drag-pan / double-tap gestures.
        if (resetSliceView == true || sliceZoom is not null || sliceCenterX is not null || sliceCenterY is not null)
            SetSliceViewFromMcp(sliceZoom, sliceCenterX, sliceCenterY, resetSliceView == true);

        // Camera — gesture-driven in the UI, so write the model directly (render loop applies it). Use the
        // same clamps as the orbit/zoom gestures so an agent can't push the camera into an invalid pose.
        if (resetCamera == true) ViewModel.ResetCamera();
        if (azimuth is not null) ViewModel.CameraAzimuth = (float)azimuth.Value;
        if (elevation is not null) ViewModel.CameraElevation = Math.Clamp((float)elevation.Value, -1.4f, 1.4f);
        if (distance is not null) ViewModel.CameraDistance = Math.Clamp((float)distance.Value, 0.5f, 8f);

        // Volume tuning — drive the sliders so their handlers update the model + renderer (WinUI clamps to range).
        if (density is not null) DensitySlider.Value = density.Value;
        if (spectralScale is not null) SpectralSlider.Value = spectralScale.Value;
        if (steps is not null) StepsSlider.Value = steps.Value;

        // Visibility toggles + background (each control's handler does the work).
        if (!string.IsNullOrEmpty(background))
            BackgroundCombo.SelectedIndex = background.Trim().ToLowerInvariant() switch { "black" => 1, "light" => 2, _ => 0 };
        if (showSlicePlane is not null) SlicePlaneToggle.IsOn = showSlicePlane.Value;
        if (showCaptions is not null) CaptionsToggle.IsOn = showCaptions.Value;
        if (autoOrbit is not null) AutoOrbitToggle.IsOn = autoOrbit.Value;

        // Playback — start/stop the channel animation (works in both modes).
        if (playing is not null)
        {
            if (playing.Value) StartPlayback(); else StopPlayback();
        }

        if (channel is not null && _volume is not null)
        {
            int c = Math.Clamp(channel.Value, 0, Math.Max(0, _volume.Nz - 1));
            _suppressChannel = true;
            ChannelSlider.Value = c;
            _suppressChannel = false;
            ViewModel.Channel = c;
            UpdateChannelLabel();
            if (ViewModel.ViewMode == CubeViewMode.Slice) RenderSlice(); // no-op render in volume mode
        }
    }

    /// <summary>Probe the spectrum at a 0-based NATIVE spaxel — typed outcome, never a bare null.</summary>
    public CubeSpectrumProbe ProbeCubeSpectrum(int x, int y)
        => CubeSpectrumProber.Probe(_volume, x, y);

    /// <summary>
    /// The agent-side spaxel CLICK (show_cube_spectrum): switch to slice mode, probe the NATIVE pixel,
    /// and open the on-screen spectrum panel exactly as a user click would.
    /// </summary>
    public CubeSpectrumProbe ShowCubeSpectrum(int x, int y)
    {
        var probe = CubeSpectrumProber.Probe(_volume, x, y);
        if (probe.Status != CubeProbeStatus.Ok || _volume is null) return probe;

        SetViewMode(CubeViewMode.Slice);
        // Native pixel → display pixel (the slice may draw at native res or the down-sampled volume's).
        int nativeNx = _meta?.Nx ?? _volume.Nx, nativeNy = _meta?.Ny ?? _volume.Ny;
        int nxDisp = _sliceDispNx > 0 ? _sliceDispNx : _volume.Nx;
        int nyDisp = _sliceDispNy > 0 ? _sliceDispNy : _volume.Ny;
        _probeX = Math.Clamp((int)((long)x * nxDisp / nativeNx), 0, Math.Max(0, nxDisp - 1));
        _probeY = Math.Clamp((int)((long)y * nyDisp / nativeNy), 0, Math.Max(0, nyDisp - 1));
        int sx = MapDispToVolume(_probeX, nxDisp, _volume.Nx);
        int sy = MapDispToVolume(_probeY, nyDisp, _volume.Ny);
        _probeSpectrum = CubeSliceRenderer.Spectrum(_volume, sx, sy, _meta?.NormLo ?? 0, _meta?.NormHi ?? 1);
        SpectrumTitle.Text = Helpers.Loc.F("Cube_SpectrumTitle", _probeX, _probeY);
        SpectrumPanel.Visibility = Visibility.Visible;
        DrawSpectrum();
        return probe;
    }

    /// <summary>Dismiss the spectrum panel (the panel's ✕). True even when it was already closed.</summary>
    public bool CloseCubeSpectrum()
    {
        SpectrumPanel.Visibility = Visibility.Collapsed;
        _probeSpectrum = null;
        _probeX = _probeY = -1;
        return true;
    }

    /// <summary>
    /// Set or reset the opacity transfer function (set_cube_transfer) and return the updated state.
    /// Points arrive validated (>= 2); the model clamps values and pins the min/max-x endpoints.
    /// </summary>
    public CubeViewState ApplyCubeTransfer(IReadOnlyList<CubeTransferPoint>? points, bool reset)
    {
        if (reset)
            _transfer.Reset();
        else if (points is not null)
            _transfer.Replace(points.Select(p => new System.Numerics.Vector2((float)p.X, (float)p.Y)).ToList());
        ApplyTransferFunction();
        DrawTransferEditor();
        return GetCubeState();
    }

    /// <summary>The channel scrubber's waveform data in physical units, or null when there is no
    /// scrubbable cube (not loaded / single channel).</summary>
    public CubeChannelProfileResult? GetChannelProfile()
    {
        var prof = _channelProfile;
        if (_volume is null || prof is null) return null;

        int nz = prof.Length;
        var mean = new double?[nz];
        var axis = new double[nz];
        bool hasSpec = _meta?.Wcs.HasSpectral == true;
        double lo = _meta?.NormLo ?? 0, hi = _meta?.NormHi ?? 1;
        double range = hi - lo;
        for (int z = 0; z < nz; z++)
        {
            float v = prof[z];
            mean[z] = float.IsFinite(v) ? lo + v * range : null;   // NaN (all-blank channel) → null
            int nativeZ = _meta?.NativeChannel(z) ?? z;
            double a = hasSpec ? _meta!.Wcs.SpectralValue(nativeZ) : nativeZ;
            axis[z] = double.IsFinite(a) ? a : nativeZ;
        }
        return new CubeChannelProfileResult(nz, mean, _meta?.Bunit ?? "", axis,
            hasSpec ? _meta!.Wcs.SpecUnitDisplay() : "channel");
    }
}
