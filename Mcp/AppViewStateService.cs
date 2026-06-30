using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.Services.Notebook;

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

    // ── Cube Viewer actions (registered by the UI; invoked by the cube MCP tools) ────────────────

    private volatile Func<string, Task<CubeOpenOutcome>>? _openCube;
    private volatile Func<Task<CubeViewState?>>? _getCube;
    private volatile Func<CubeViewArgs, Task<CubeViewState?>>? _setCube;
    private volatile Func<string, string, int, bool, Task<CubeExportOutcome>>? _exportCube;
    private volatile Func<int, int, Task<CubeSpectrumResult?>>? _probeCube;

    /// <summary>The UI registers the cube viewer actions (each marshals to the UI thread).</summary>
    public void SetCubeActions(
        Func<string, Task<CubeOpenOutcome>> openCube,
        Func<Task<CubeViewState?>> getCube,
        Func<CubeViewArgs, Task<CubeViewState?>> setCube,
        Func<string, string, int, bool, Task<CubeExportOutcome>> exportCube,
        Func<int, int, Task<CubeSpectrumResult?>> probeCube)
    {
        _openCube = openCube;
        _getCube = getCube;
        _setCube = setCube;
        _exportCube = exportCube;
        _probeCube = probeCube;
    }

    public Task<CubeOpenOutcome> OpenCubeAsync(string target)
        => _openCube?.Invoke(target) ?? Task.FromResult(new CubeOpenOutcome(false, target, 0, 0, 0, "cube viewer unavailable"));

    public Task<CubeViewState?> GetCubeAsync()
        => _getCube?.Invoke() ?? Task.FromResult<CubeViewState?>(null);

    public Task<CubeViewState?> SetCubeAsync(CubeViewArgs args)
        => _setCube?.Invoke(args) ?? Task.FromResult<CubeViewState?>(null);

    public Task<CubeExportOutcome> ExportCubeAsync(string path, string format, int scale, bool dark)
        => _exportCube?.Invoke(path, format, scale, dark) ?? Task.FromResult(new CubeExportOutcome(false, path, "cube viewer unavailable"));

    public Task<CubeSpectrumResult?> ProbeCubeAsync(int x, int y)
        => _probeCube?.Invoke(x, y) ?? Task.FromResult<CubeSpectrumResult?>(null);

    // ── 2D FITS Viewer actions (registered by the UI; invoked by the FITS MCP tools) ─────────────

    private volatile Func<Task<FitsViewState?>>? _getFits;
    private volatile Func<FitsViewArgs, Task<FitsViewState?>>? _setFits;
    private volatile Func<int, int, Task<FitsPixelResult?>>? _probeFits;
    private volatile Func<double, double, Task<FitsGotoOutcome>>? _gotoFits;

    /// <summary>The UI registers the FITS viewer actions (each marshals to the UI thread).</summary>
    public void SetFitsActions(
        Func<Task<FitsViewState?>> getFits,
        Func<FitsViewArgs, Task<FitsViewState?>> setFits,
        Func<int, int, Task<FitsPixelResult?>> probeFits,
        Func<double, double, Task<FitsGotoOutcome>> gotoFits)
    {
        _getFits = getFits;
        _setFits = setFits;
        _probeFits = probeFits;
        _gotoFits = gotoFits;
    }

    public Task<FitsViewState?> GetFitsAsync()
        => _getFits?.Invoke() ?? Task.FromResult<FitsViewState?>(null);

    public Task<FitsViewState?> SetFitsAsync(FitsViewArgs args)
        => _setFits?.Invoke(args) ?? Task.FromResult<FitsViewState?>(null);

    public Task<FitsPixelResult?> ProbeFitsAsync(int x, int y)
        => _probeFits?.Invoke(x, y) ?? Task.FromResult<FitsPixelResult?>(null);

    public Task<FitsGotoOutcome> GotoFitsAsync(double ra, double dec)
        => _gotoFits?.Invoke(ra, dec) ?? Task.FromResult(new FitsGotoOutcome(false, ra, dec, "FITS viewer unavailable"));

    // ── FITS coordinate bookmarks (persisted; routed through the host VM to keep the panel in sync) ──

    private volatile Func<Task<IReadOnlyList<FitsBookmark>>>? _listFitsBookmarks;
    private volatile Func<double, double, string?, string?, Task<FitsBookmark?>>? _saveFitsBookmark;
    private volatile Func<string, Task<bool>>? _deleteFitsBookmark;

    public void SetFitsBookmarkActions(
        Func<Task<IReadOnlyList<FitsBookmark>>> list,
        Func<double, double, string?, string?, Task<FitsBookmark?>> save,
        Func<string, Task<bool>> delete)
    {
        _listFitsBookmarks = list;
        _saveFitsBookmark = save;
        _deleteFitsBookmark = delete;
    }

    public Task<IReadOnlyList<FitsBookmark>> ListFitsBookmarksAsync()
        => _listFitsBookmarks?.Invoke() ?? Task.FromResult<IReadOnlyList<FitsBookmark>>(Array.Empty<FitsBookmark>());

    public Task<FitsBookmark?> SaveFitsBookmarkAsync(double ra, double dec, string? label, string? sourceFile)
        => _saveFitsBookmark?.Invoke(ra, dec, label, sourceFile) ?? Task.FromResult<FitsBookmark?>(null);

    public Task<bool> DeleteFitsBookmarkAsync(string id)
        => _deleteFitsBookmark?.Invoke(id) ?? Task.FromResult(false);

    // ── Notebook actions (one applier for all mutations + read accessors; all marshal to the UI thread) ──

    private volatile Func<NotebookCommand, Task<NotebookState?>>? _notebookMutate;
    private volatile Func<Task<NotebookState?>>? _notebookGet;
    private volatile Func<int, Task<NotebookCellOutputs?>>? _notebookCellOutput;
    private volatile Func<Task<NotebookKernelInfo>>? _notebookKernel;
    private volatile Func<Task<IReadOnlyList<NotebookRef>>>? _notebookList;

    public void SetNotebookActions(
        Func<NotebookCommand, Task<NotebookState?>> mutate,
        Func<Task<NotebookState?>> get,
        Func<int, Task<NotebookCellOutputs?>> cellOutput,
        Func<Task<NotebookKernelInfo>> kernel,
        Func<Task<IReadOnlyList<NotebookRef>>> list)
    {
        _notebookMutate = mutate;
        _notebookGet = get;
        _notebookCellOutput = cellOutput;
        _notebookKernel = kernel;
        _notebookList = list;
    }

    public Task<NotebookState?> NotebookMutateAsync(NotebookCommand cmd)
        => _notebookMutate?.Invoke(cmd) ?? Task.FromResult<NotebookState?>(null);

    public Task<NotebookState?> GetNotebookAsync()
        => _notebookGet?.Invoke() ?? Task.FromResult<NotebookState?>(null);

    public Task<NotebookCellOutputs?> GetCellOutputAsync(int index)
        => _notebookCellOutput?.Invoke(index) ?? Task.FromResult<NotebookCellOutputs?>(null);

    public Task<NotebookKernelInfo> GetKernelStateAsync()
        => _notebookKernel?.Invoke() ?? Task.FromResult(new NotebookKernelInfo("Dead", "no notebook open", ""));

    public Task<IReadOnlyList<NotebookRef>> ListNotebooksAsync()
        => _notebookList?.Invoke() ?? Task.FromResult<IReadOnlyList<NotebookRef>>(Array.Empty<NotebookRef>());

    // ── Analysis-notebook hand-off (resolve obs → seed + open a notebook) ────────────────────────

    private volatile Func<string, string, Task<NotebookState?>>? _createAnalysisNotebook;

    public void SetCreateAnalysisNotebookAction(Func<string, string, Task<NotebookState?>> create)
        => _createAnalysisNotebook = create;

    public Task<NotebookState?> CreateAnalysisNotebookAsync(string observationId, string template)
        => _createAnalysisNotebook?.Invoke(observationId, template) ?? Task.FromResult<NotebookState?>(null);

    // ── Tab management (close the active viewer tab; count open tabs) ─────────────────────────────

    private volatile Func<string, Task<TabCloseOutcome>>? _closeTab;
    private volatile Func<Task<OpenTabsState>>? _listTabs;

    public void SetTabActions(Func<string, Task<TabCloseOutcome>> close, Func<Task<OpenTabsState>> list)
    {
        _closeTab = close;
        _listTabs = list;
    }

    public Task<TabCloseOutcome> CloseTabAsync(string kind)
        => _closeTab?.Invoke(kind) ?? Task.FromResult(new TabCloseOutcome(false, kind, "viewer unavailable"));

    public Task<OpenTabsState> ListTabsAsync()
        => _listTabs?.Invoke() ?? Task.FromResult(new OpenTabsState(0, 0, 0));
}
