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
    bool Playing = false);

/// <summary>The spectrum (flux vs channel) at a spaxel, returned to the MCP layer.</summary>
public sealed record CubeSpectrumResult(
    int X,
    int Y,
    double[] SpectralAxis,  // spectral world value per channel (or channel index if no WCS)
    double[] Flux,          // physical flux per channel
    string FluxUnit,
    string SpectralUnit,
    // Spectral conventions — surfaced (not converted) so kinematics are done correctly downstream.
    string? SpectralFrame = null,     // SPECSYS (LSRK/barycentric/topocentric) — REQUIRED to read a velocity axis
    double? RestFrequencyGHz = null,  // for frequency↔velocity conversion at the line rest frequency
    double? BeamMajorArcsec = null,   // synthesized beam (for K↔Jy/beam + flux integration)
    double? BeamMinorArcsec = null,
    double? BeamPaDeg = null);

/// <summary>
/// The cube's per-channel intensity profile — the waveform under the channel scrubber.
///
/// It is the thing a person scrubs against to find where the line is, and it was on screen and
/// nowhere else: an agent asked to "go to the brightest channel" had to probe a spaxel it had no
/// reason to trust was on the source, or walk every channel one at a time.
///
/// <see cref="Intensity"/> is the whole-plane statistic per channel, indexed by channel. The spectral
/// axis is beside it so a channel can be named in the units the data is in rather than as an integer.
/// </summary>
public sealed record CubeChannelProfileResult(
    int Channels,
    double[] SpectralAxis,     // spectral world value per channel (or the channel index if no WCS)
    double[] Intensity,        // whole-plane intensity per channel
    string SpectralUnit,
    int PeakChannel,           // the channel where Intensity is greatest
    int CurrentChannel,        // what the viewer is showing right now
    string? SpectralFrame = null,
    double? RestFrequencyGHz = null);
