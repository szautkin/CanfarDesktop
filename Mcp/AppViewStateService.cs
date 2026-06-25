using CanfarDesktop.Mcp.Tools.Write;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Bridges the live UI to the MCP view-state tools. For reads (<c>get_current_view</c>) the UI PUSHES
/// immutable state on the UI thread (mode, Search sky focus, open FITS paths) and the tool READS a
/// consistent volatile snapshot from the MCP connection thread. For the live ViewState WRITE tools
/// (<c>navigate_to</c>, <c>set_search_focus</c>) the UI registers action delegates that marshal to the
/// UI thread; the tools invoke them. Push (not pull) keeps cross-thread access safe.
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

    /// <summary>Raised when an agent invokes a tool; carries the tool name and the module it concerns
    /// (null for meta tools). The UI shows a transient "agent is working" indicator.</summary>
    public sealed record AgentActivitySignal(string ToolName, string? Module);

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

    /// <summary>Raised (off the UI thread) each time an agent begins a tool call. The UI subscribes and
    /// marshals to its thread to show + auto-hide the "agent is working" indicator.</summary>
    public event Action<AgentActivitySignal>? AgentActivity;

    /// <summary>Signal that an agent invoked <paramref name="toolName"/> (working in <paramref name="module"/>).</summary>
    public void NotifyAgentActivity(string toolName, string? module)
        => AgentActivity?.Invoke(new AgentActivitySignal(toolName, module));

    // ── Live ViewState write actions (registered by the UI; invoked by the write tools) ──────────

    private volatile Func<string, Task<NavigationOutcome>>? _navigate;
    private volatile Func<double, double, Task>? _setSearchFocus;
    private volatile Func<string, Task<OpenFitsOutcome>>? _openFits;

    /// <summary>The UI registers the live ViewState actions (each marshals to the UI thread).</summary>
    public void SetActions(
        Func<string, Task<NavigationOutcome>> navigate,
        Func<double, double, Task> setSearchFocus,
        Func<string, Task<OpenFitsOutcome>> openFits)
    {
        _navigate = navigate;
        _setSearchFocus = setSearchFocus;
        _openFits = openFits;
    }

    public Task<NavigationOutcome> NavigateAsync(string mode)
        => _navigate?.Invoke(mode) ?? Task.FromResult(new NavigationOutcome(false, mode, mode));

    public Task SetSearchFocusActionAsync(double ra, double dec)
        => _setSearchFocus?.Invoke(ra, dec) ?? Task.CompletedTask;

    public Task<OpenFitsOutcome> OpenFitsAsync(string id)
        => _openFits?.Invoke(id) ?? Task.FromResult(new OpenFitsOutcome(false, id, null, "FITS viewer unavailable"));
}
