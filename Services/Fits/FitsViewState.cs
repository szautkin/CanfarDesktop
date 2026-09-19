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
    double CrosshairDec,
    // ── Full read parity: everything else the UI shows (image-info panel, HDUs, toggles, blink,
    //    panels, crosshair pixel) so get_fits_view is a complete mirror of what the user sees. ──
    string Path = "",                          // full local path (feed get_fits_header / get_fits_wcs)
    int HduIndex = 0,                          // displayed HDU/extension
    IReadOnlyList<FitsHduInfo>? Hdus = null,   // all HDUs (multi-extension files; null/1-entry otherwise)
    string Unit = "",                          // BUNIT ("" when absent)
    double DataMin = 0,                        // pixel value range of the displayed image
    double DataMax = 0,
    double PixelScaleArcsec = 0,               // 0 when no valid WCS
    double NorthAngleDeg = 0,
    bool ParityFlip = false,
    bool WcsApproximate = false,               // reconstructed/approximate solution (sync warning basis)
    bool SyncZoom = false,                     // the two host toggles
    bool LinkedCrosshair = false,
    bool BlinkActive = false,                  // a blink comparison is running
    bool HeaderPanelOpen = false,
    bool BookmarksPanelOpen = false,
    int? CrosshairX = null,                    // crosshair display pixel (null when not placed)
    int? CrosshairY = null,
    string Status = "");                       // the status-bar message

/// <summary>One HDU/extension of the loaded FITS (the viewer's extension selector rows).</summary>
public sealed record FitsHduInfo(int Index, string Name, string Shape, bool IsImage);

/// <summary>The pixel value + sky coordinate at a 0-based display pixel, returned by probe_fits_pixel.</summary>
public sealed record FitsPixelResult(
    int X,
    int Y,
    double? Value,         // null = blanked pixel (NaN/Inf in the data — raw NaN would crash the JSON serializer)
    bool HasWcs,
    double Ra,             // degrees (0 if no WCS)
    double Dec,
    string? Unit = null);  // BUNIT (physical unit of Value), omitted when the FITS has no BUNIT

/// <summary>Result of fits_goto_coordinate: whether the viewport was moved to the RA/Dec.</summary>
public sealed record FitsGotoOutcome(bool Moved, double Ra, double Dec, string? Message);

/// <summary>A saved FITS sky-coordinate bookmark, for the bookmark MCP tools.</summary>
public sealed record FitsBookmark(string Id, string Label, double Ra, double Dec, string? SourceFile, DateTime SavedAt);
