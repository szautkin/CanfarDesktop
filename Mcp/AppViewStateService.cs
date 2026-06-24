namespace CanfarDesktop.Mcp;

/// <summary>
/// Bridges the live UI's navigation context to the <c>get_current_view</c> MCP tool. The UI PUSHES
/// immutable state on the UI thread (mode, Search sky focus, open FITS paths); the tool READS a
/// consistent snapshot from the MCP connection thread. Push (rather than pull) keeps the cross-thread
/// access safe — the reader never iterates a live ObservableCollection or tears a multi-word value;
/// every field is a volatile reference to an immutable value.
/// </summary>
public sealed class AppViewStateService
{
    /// <summary>Per-mode view context returned to <c>get_current_view</c>.</summary>
    public sealed record ModeView(
        string Mode,
        string ModeTitle,
        double? SearchFocusRA,
        double? SearchFocusDec,
        IReadOnlyList<string> OpenFitsPaths);

    private sealed record SkyFocus(double Ra, double Dec);

    private volatile string _mode = "landing";
    private volatile string _modeTitle = "Home";
    private volatile SkyFocus? _searchFocus;
    private volatile IReadOnlyList<string> _openFitsPaths = Array.Empty<string>();

    /// <summary>The current app mode + its human title (push on navigation).</summary>
    public void SetMode(string mode, string title)
    {
        _mode = mode;
        _modeTitle = title;
    }

    /// <summary>The Search form's resolved sky focus, or null when unset (push on RA/Dec change).</summary>
    public void SetSearchFocus(double? ra, double? dec)
        => _searchFocus = ra.HasValue && dec.HasValue ? new SkyFocus(ra.Value, dec.Value) : null;

    /// <summary>The local paths of all open FITS viewer tabs (push on tab open/close).</summary>
    public void SetOpenFitsPaths(IReadOnlyList<string> paths)
        => _openFitsPaths = paths.Count == 0 ? Array.Empty<string>() : paths;

    /// <summary>A consistent snapshot of the current view for the tool.</summary>
    public ModeView Capture()
    {
        var focus = _searchFocus;
        return new ModeView(_mode, _modeTitle, focus?.Ra, focus?.Dec, _openFitsPaths);
    }
}
