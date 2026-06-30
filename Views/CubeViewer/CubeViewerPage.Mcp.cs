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
        string spec = (_meta is not null && _meta.Wcs.HasSpectral)
            ? (_meta.Wcs.SpecText(ViewModel.Channel) + " " + _meta.Wcs.SpecUnitDisplay()).Trim()
            : "";
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
            ViewModel.IsPlaying);
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
        bool? autoOrbit = null, bool? playing = null, bool? resetCamera = null)
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

    /// <summary>Extract the spectrum (flux vs channel) at a 0-based spaxel, or null if out of range.</summary>
    public CubeSpectrumResult? ProbeCubeSpectrum(int x, int y)
    {
        if (_volume is null) return null;
        var flux = CubeSliceRenderer.Spectrum(_volume, x, y, _meta?.NormLo ?? 0, _meta?.NormHi ?? 1);
        if (flux is null) return null;

        int nz = flux.Length;
        var axis = new double[nz];
        var fl = new double[nz];
        bool hasSpec = _meta?.Wcs.HasSpectral == true;
        for (int z = 0; z < nz; z++)
        {
            axis[z] = hasSpec ? _meta!.Wcs.SpectralValue(z) : z;
            fl[z] = flux[z];
        }
        return new CubeSpectrumResult(x, y, axis, fl, _meta?.Bunit ?? "",
            hasSpec ? _meta!.Wcs.SpecUnitDisplay() : "channel");
    }
}
