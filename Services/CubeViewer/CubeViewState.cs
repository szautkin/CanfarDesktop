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
    double DataMax);

/// <summary>The spectrum (flux vs channel) at a spaxel, returned to the MCP layer.</summary>
public sealed record CubeSpectrumResult(
    int X,
    int Y,
    double[] SpectralAxis,  // spectral world value per channel (or channel index if no WCS)
    double[] Flux,          // physical flux per channel
    string FluxUnit,
    string SpectralUnit);
