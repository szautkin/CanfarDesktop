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
        int nz = _meta?.Nz ?? _volume?.Nz ?? 0;
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
            _meta?.DataMax ?? 0);
    }

    /// <summary>Apply view settings from MCP; each null/empty argument is left unchanged.</summary>
    public void ApplyCubeView(string? mode, int? channel, string? colormap, string? stretch,
                              string? renderMode, double? windowLo, double? windowHi)
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
