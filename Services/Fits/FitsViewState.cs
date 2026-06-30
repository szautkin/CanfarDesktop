namespace CanfarDesktop.Services.Fits;

/// <summary>A snapshot of the 2D FITS viewer's active tab + display state, returned to the MCP layer.</summary>
public sealed record FitsViewState(
    bool Loaded,
    string FileName,
    int Width,
    int Height,
    string Stretch,        // Linear | Log | Sqrt | Squared | Asinh
    string Colormap,       // Grayscale | Inverted | Heat | Cool | Viridis
    double MinCut,         // black-level cut (physical pixel value)
    double MaxCut,         // white-level cut (physical pixel value)
    double ZoomPercent,    // current zoom (100 = 1:1)
    bool NorthUp,
    bool HasWcs,
    bool CrosshairPlaced,
    double CrosshairRa,    // degrees (0 when no crosshair is placed)
    double CrosshairDec);

/// <summary>The pixel value + sky coordinate at a 0-based display pixel, returned by probe_fits_pixel.</summary>
public sealed record FitsPixelResult(
    int X,
    int Y,
    double Value,
    bool HasWcs,
    double Ra,             // degrees (0 if no WCS)
    double Dec,
    string? Unit = null);  // BUNIT (physical unit of Value), omitted when the FITS has no BUNIT

/// <summary>Result of fits_goto_coordinate: whether the viewport was moved to the RA/Dec.</summary>
public sealed record FitsGotoOutcome(bool Moved, double Ra, double Dec, string? Message);

/// <summary>A saved FITS sky-coordinate bookmark, for the bookmark MCP tools.</summary>
public sealed record FitsBookmark(string Id, string Label, double Ra, double Dec, string? SourceFile, DateTime SavedAt);
