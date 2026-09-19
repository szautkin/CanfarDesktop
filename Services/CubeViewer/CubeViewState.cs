namespace CanfarDesktop.Services.CubeViewer;

/// <summary>A snapshot of the Cube Viewer's loaded cube + display state, returned to the MCP layer.</summary>
public sealed record CubeViewState(
    bool Loaded,
    string Name,
    string Object,
    int Nx,
    int Ny,
    int Nz,
    string Mode,            // "Volume" | "Slice"
    int Channel,
    string SpectralValue,   // formatted spectral value at the current channel
    string Colormap,
    string Stretch,
    string RenderMode,      // "Emission" | "Max-Intensity"
    double WindowLo,
    double WindowHi,
    string Unit,
    double DataMin,
    double DataMax,
    // Camera pose + volume tuning + visibility toggles + playback (full UI state).
    double Azimuth = 0,
    double Elevation = 0,
    double Distance = 0,
    double Density = 0,
    double SpectralScale = 0,
    int Steps = 0,
    string Background = "dark",
    bool ShowSlicePlane = false,
    bool ShowCaptions = false,
    bool AutoOrbit = false,
    bool Playing = false,
    // ── Full read parity: everything else the UI shows (info panel, slice view, spectrum panel,
    //    opacity curve) so get_cube_view is a complete mirror of what the user sees. ──
    string Instrument = "",
    double Median = 0,
    double NanFraction = 0,          // fraction of blanked voxels (0..1)
    double CutLo = 0,                // physical values of the display-normalization cut (info-panel RANGE)
    double CutHi = 0,
    int RenderNx = 0,                // in-memory (GPU-downsampled) dims; Nx/Ny above are NATIVE, Nz is RENDER
    int RenderNy = 0,
    int NativeNz = 0,                // header NAXIS3 (Nz above is the renderable channel count)
    bool Downsampled = false,
    string Path = "",                // source file of the loaded cube
    double SliceZoom = 1,            // slice-view zoom (1 = fit)
    int? SliceCenterX = null,        // NATIVE pixel currently at the slice viewport center (null when off-image)
    int? SliceCenterY = null,
    bool SpectrumPanelOpen = false,  // the click-to-probe spectrum panel
    int? SpectrumX = null,           // probed spaxel shown in the panel title (display pixels, as the UI labels it)
    int? SpectrumY = null,
    IReadOnlyList<CubeTransferPoint>? TransferPoints = null); // opacity curve control points

/// <summary>One opacity transfer-function control point: data value → alpha, both normalized [0,1].</summary>
public sealed record CubeTransferPoint(double X, double Y);

/// <summary>Per-channel mean profile (the scrubber waveform), returned by get_cube_channel_profile.
/// Means are in physical units; null entries are all-blank channels.</summary>
public sealed record CubeChannelProfileResult(
    int Channels,
    double?[] Mean,          // NaN-aware mean per rendered channel (null = fully blanked channel)
    string Unit,             // BUNIT ("" when the header has none)
    double[] SpectralAxis,   // spectral world value per channel (or channel index if no spectral WCS)
    string SpectralUnit);

/// <summary>The spectrum (flux vs channel) at a spaxel, returned to the MCP layer.</summary>
public sealed record CubeSpectrumResult(
    int X,
    int Y,
    double[] SpectralAxis,  // spectral world value per channel (or channel index if no WCS)
    double?[] Flux,         // physical flux per channel; null = blanked voxel (NaN/Inf in the data)
    string FluxUnit,
    string SpectralUnit,
    // Spectral conventions — surfaced (not converted) so kinematics are done correctly downstream.
    string? SpectralFrame = null,     // SPECSYS (LSRK/barycentric/topocentric) — REQUIRED to read a velocity axis
    double? RestFrequencyGHz = null,  // for frequency↔velocity conversion at the line rest frequency
    double? BeamMajorArcsec = null,   // synthesized beam (for K↔Jy/beam + flux integration)
    double? BeamMinorArcsec = null,
    double? BeamPaDeg = null,
    int BlankedChannels = 0);         // how many Flux entries are null (all == Flux.Length ⇒ fully masked spaxel)

/// <summary>How a spectrum probe resolved (distinguishes the failure modes that used to collapse to null).</summary>
public enum CubeProbeStatus { Ok, NoCube, OutOfRange }

/// <summary>Outcome of a spectrum probe: the status, the spectrum when Ok, and the cube's
/// native spatial dimensions (populated on OutOfRange so the caller can report the valid range).</summary>
public sealed record CubeSpectrumProbe(CubeProbeStatus Status, CubeSpectrumResult? Result, int Nx = 0, int Ny = 0);
