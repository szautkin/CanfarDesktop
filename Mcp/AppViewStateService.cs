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

    // ── Search page actions (registered by the UI; invoked by the search MCP tools) ──────────────

    private volatile Func<Task<SearchFormSnapshot?>>? _getSearchForm;
    private volatile Func<SearchFormPatch, Task<SearchFormSnapshot?>>? _setSearchForm;
    private volatile Func<Task<SearchFacetsSnapshot?>>? _getSearchConstraints;
    private volatile Func<SearchFacetSelections, Task<SearchConstraintsOutcome?>>? _setSearchConstraints;
    private volatile Func<Task<SearchFormSnapshot?>>? _resetSearchForm;
    private volatile Func<Task<SearchRunOutcome>>? _runSearch;
    private volatile Func<string, Task<AdqlStageOutcome?>>? _setAdqlQuery;
    private volatile Func<string?, Task<SearchRunOutcome>>? _executeAdqlQuery;
    private volatile Func<bool, int, Task<SearchResultsSnapshot?>>? _getSearchResults;
    private volatile Func<SearchResultsCommand, Task<SearchResultsSnapshot?>>? _setSearchResultsView;
    private volatile Func<string, string?, Task<SearchExportOutcome>>? _exportSearchResults;
    private volatile Func<int, Task<LoadRecentSearchOutcome?>>? _loadRecentSearch;
    private volatile Func<string, Task<SearchRunOutcome>>? _runSavedQuery;

    /// <summary>The UI registers the Search page actions (each marshals to the UI thread).</summary>
    public void SetSearchActions(
        Func<Task<SearchFormSnapshot?>> getForm,
        Func<SearchFormPatch, Task<SearchFormSnapshot?>> setForm,
        Func<Task<SearchFacetsSnapshot?>> getConstraints,
        Func<SearchFacetSelections, Task<SearchConstraintsOutcome?>> setConstraints,
        Func<Task<SearchFormSnapshot?>> resetForm,
        Func<Task<SearchRunOutcome>> runSearch,
        Func<string, Task<AdqlStageOutcome?>> setAdql,
        Func<string?, Task<SearchRunOutcome>> executeAdql,
        Func<bool, int, Task<SearchResultsSnapshot?>> getResults,
        Func<SearchResultsCommand, Task<SearchResultsSnapshot?>> setResultsView,
        Func<string, string?, Task<SearchExportOutcome>> exportResults,
        Func<int, Task<LoadRecentSearchOutcome?>> loadRecent,
        Func<string, Task<SearchRunOutcome>> runSavedQuery)
    {
        _getSearchForm = getForm;
        _setSearchForm = setForm;
        _getSearchConstraints = getConstraints;
        _setSearchConstraints = setConstraints;
        _resetSearchForm = resetForm;
        _runSearch = runSearch;
        _setAdqlQuery = setAdql;
        _executeAdqlQuery = executeAdql;
        _getSearchResults = getResults;
        _setSearchResultsView = setResultsView;
        _exportSearchResults = exportResults;
        _loadRecentSearch = loadRecent;
        _runSavedQuery = runSavedQuery;
    }

    private static readonly SearchRunOutcome SearchUnavailableRun =
        new(false, null, 0, null, "Search page unavailable");

    public Task<SearchFormSnapshot?> GetSearchFormAsync()
        => _getSearchForm?.Invoke() ?? Task.FromResult<SearchFormSnapshot?>(null);

    public Task<SearchFormSnapshot?> SetSearchFormAsync(SearchFormPatch patch)
        => _setSearchForm?.Invoke(patch) ?? Task.FromResult<SearchFormSnapshot?>(null);

    public Task<SearchFacetsSnapshot?> GetSearchConstraintsAsync()
        => _getSearchConstraints?.Invoke() ?? Task.FromResult<SearchFacetsSnapshot?>(null);

    public Task<SearchConstraintsOutcome?> SetSearchConstraintsAsync(SearchFacetSelections selections)
        => _setSearchConstraints?.Invoke(selections) ?? Task.FromResult<SearchConstraintsOutcome?>(null);

    public Task<SearchFormSnapshot?> ResetSearchFormAsync()
        => _resetSearchForm?.Invoke() ?? Task.FromResult<SearchFormSnapshot?>(null);

    public Task<SearchRunOutcome> RunSearchAsync()
        => _runSearch?.Invoke() ?? Task.FromResult(SearchUnavailableRun);

    public Task<AdqlStageOutcome?> SetAdqlQueryAsync(string adql)
        => _setAdqlQuery?.Invoke(adql) ?? Task.FromResult<AdqlStageOutcome?>(null);

    public Task<SearchRunOutcome> ExecuteAdqlQueryAsync(string? adql)
        => _executeAdqlQuery?.Invoke(adql) ?? Task.FromResult(SearchUnavailableRun);

    public Task<SearchResultsSnapshot?> GetSearchResultsAsync(bool includeRows, int maxRows)
        => _getSearchResults?.Invoke(includeRows, maxRows) ?? Task.FromResult<SearchResultsSnapshot?>(null);

    public Task<SearchResultsSnapshot?> SetSearchResultsViewAsync(SearchResultsCommand command)
        => _setSearchResultsView?.Invoke(command) ?? Task.FromResult<SearchResultsSnapshot?>(null);

    public Task<SearchExportOutcome> ExportSearchResultsAsync(string format, string? path)
        => _exportSearchResults?.Invoke(format, path)
           ?? Task.FromResult(new SearchExportOutcome(false, null, 0, "Search page unavailable"));

    public Task<LoadRecentSearchOutcome?> LoadRecentSearchAsync(int index)
        => _loadRecentSearch?.Invoke(index) ?? Task.FromResult<LoadRecentSearchOutcome?>(null);

    public Task<SearchRunOutcome> RunSavedQueryAsync(string name)
        => _runSavedQuery?.Invoke(name) ?? Task.FromResult(SearchUnavailableRun);

    // ── Cube Viewer actions (registered by the UI; invoked by the cube MCP tools) ────────────────

    private volatile Func<string, Task<CubeOpenOutcome>>? _openCube;
    private volatile Func<Task<CubeViewState?>>? _getCube;
    private volatile Func<CubeViewArgs, Task<CubeViewState?>>? _setCube;
    private volatile Func<CubeExportRequest, Task<CubeExportOutcome>>? _exportCube;
    private volatile Func<int, int, Task<CubeSpectrumProbe?>>? _probeCube;
    private volatile Func<int, int, Task<CubeSpectrumProbe?>>? _showSpectrum;
    private volatile Func<Task<bool>>? _closeSpectrum;
    private volatile Func<IReadOnlyList<CubeTransferPoint>?, bool, Task<CubeViewState?>>? _setTransfer;
    private volatile Func<Task<CubeChannelProfileResult?>>? _channelProfile;
    private volatile Func<int, Task<CubeTabSwitchOutcome>>? _switchCubeTab;
    private volatile Func<Task<IReadOnlyList<RecentCubeInfo>>>? _listRecentCubes;

    /// <summary>The UI registers the cube viewer actions (each marshals to the UI thread).</summary>
    public void SetCubeActions(
        Func<string, Task<CubeOpenOutcome>> openCube,
        Func<Task<CubeViewState?>> getCube,
        Func<CubeViewArgs, Task<CubeViewState?>> setCube,
        Func<CubeExportRequest, Task<CubeExportOutcome>> exportCube,
        Func<int, int, Task<CubeSpectrumProbe?>> probeCube,
        Func<int, int, Task<CubeSpectrumProbe?>> showSpectrum,
        Func<Task<bool>> closeSpectrum,
        Func<IReadOnlyList<CubeTransferPoint>?, bool, Task<CubeViewState?>> setTransfer,
        Func<Task<CubeChannelProfileResult?>> channelProfile,
        Func<int, Task<CubeTabSwitchOutcome>> switchCubeTab,
        Func<Task<IReadOnlyList<RecentCubeInfo>>> listRecentCubes)
    {
        _openCube = openCube;
        _getCube = getCube;
        _setCube = setCube;
        _exportCube = exportCube;
        _probeCube = probeCube;
        _showSpectrum = showSpectrum;
        _closeSpectrum = closeSpectrum;
        _setTransfer = setTransfer;
        _channelProfile = channelProfile;
        _switchCubeTab = switchCubeTab;
        _listRecentCubes = listRecentCubes;
    }

    public Task<CubeOpenOutcome> OpenCubeAsync(string target)
        => _openCube?.Invoke(target) ?? Task.FromResult(new CubeOpenOutcome(false, target, 0, 0, 0, "cube viewer unavailable"));

    public Task<CubeViewState?> GetCubeAsync()
        => _getCube?.Invoke() ?? Task.FromResult<CubeViewState?>(null);

    public Task<CubeViewState?> SetCubeAsync(CubeViewArgs args)
        => _setCube?.Invoke(args) ?? Task.FromResult<CubeViewState?>(null);

    public Task<CubeExportOutcome> ExportCubeAsync(CubeExportRequest request)
        => _exportCube?.Invoke(request) ?? Task.FromResult(new CubeExportOutcome(false, request.Path, "cube viewer unavailable"));

    public Task<CubeSpectrumProbe?> ProbeCubeAsync(int x, int y)
        => _probeCube?.Invoke(x, y)
           ?? Task.FromResult<CubeSpectrumProbe?>(new(CubeProbeStatus.NoCube, null)); // viewer never registered

    public Task<CubeSpectrumProbe?> ShowCubeSpectrumAsync(int x, int y)
        => _showSpectrum?.Invoke(x, y)
           ?? Task.FromResult<CubeSpectrumProbe?>(new(CubeProbeStatus.NoCube, null));

    public Task<bool> CloseCubeSpectrumAsync()
        => _closeSpectrum?.Invoke() ?? Task.FromResult(false);

    public Task<CubeViewState?> SetCubeTransferAsync(IReadOnlyList<CubeTransferPoint>? points, bool reset)
        => _setTransfer?.Invoke(points, reset) ?? Task.FromResult<CubeViewState?>(null);

    public Task<CubeChannelProfileResult?> GetCubeChannelProfileAsync()
        => _channelProfile?.Invoke() ?? Task.FromResult<CubeChannelProfileResult?>(null);

    public Task<CubeTabSwitchOutcome> SwitchCubeTabAsync(int index)
        => _switchCubeTab?.Invoke(index)
           ?? Task.FromResult(new CubeTabSwitchOutcome(false, index, 0, null, "cube viewer unavailable"));

    public Task<IReadOnlyList<RecentCubeInfo>> ListRecentCubesAsync()
        => _listRecentCubes?.Invoke() ?? Task.FromResult<IReadOnlyList<RecentCubeInfo>>(Array.Empty<RecentCubeInfo>());

    // ── 2D FITS Viewer actions (registered by the UI; invoked by the FITS MCP tools) ─────────────

    private volatile Func<Task<FitsViewState?>>? _getFits;
    private volatile Func<FitsViewArgs, Task<FitsViewState?>>? _setFits;
    private volatile Func<int, int, Task<FitsPixelResult?>>? _probeFits;
    private volatile Func<double, double, Task<FitsGotoOutcome>>? _gotoFits;
    private volatile Func<string, int?, int?, Task<FitsBlinkOutcome?>>? _blinkFits;
    private volatile Func<int, Task<FitsTabSwitchOutcome>>? _switchFitsTab;

    /// <summary>The UI registers the FITS viewer actions (each marshals to the UI thread).</summary>
    public void SetFitsActions(
        Func<Task<FitsViewState?>> getFits,
        Func<FitsViewArgs, Task<FitsViewState?>> setFits,
        Func<int, int, Task<FitsPixelResult?>> probeFits,
        Func<double, double, Task<FitsGotoOutcome>> gotoFits,
        Func<string, int?, int?, Task<FitsBlinkOutcome?>> blinkFits,
        Func<int, Task<FitsTabSwitchOutcome>> switchFitsTab)
    {
        _getFits = getFits;
        _setFits = setFits;
        _probeFits = probeFits;
        _gotoFits = gotoFits;
        _blinkFits = blinkFits;
        _switchFitsTab = switchFitsTab;
    }

    public Task<FitsViewState?> GetFitsAsync()
        => _getFits?.Invoke() ?? Task.FromResult<FitsViewState?>(null);

    public Task<FitsViewState?> SetFitsAsync(FitsViewArgs args)
        => _setFits?.Invoke(args) ?? Task.FromResult<FitsViewState?>(null);

    public Task<FitsPixelResult?> ProbeFitsAsync(int x, int y)
        => _probeFits?.Invoke(x, y) ?? Task.FromResult<FitsPixelResult?>(null);

    public Task<FitsGotoOutcome> GotoFitsAsync(double ra, double dec)
        => _gotoFits?.Invoke(ra, dec) ?? Task.FromResult(new FitsGotoOutcome(false, ra, dec, "FITS viewer unavailable"));

    public Task<FitsBlinkOutcome?> BlinkFitsAsync(string action, int? withTabIndex, int? intervalMs)
        => _blinkFits?.Invoke(action, withTabIndex, intervalMs) ?? Task.FromResult<FitsBlinkOutcome?>(null);

    public Task<FitsTabSwitchOutcome> SwitchFitsTabAsync(int index)
        => _switchFitsTab?.Invoke(index)
           ?? Task.FromResult(new FitsTabSwitchOutcome(false, index, 0, null, "FITS viewer unavailable"));

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
    private volatile Func<string?, Task<NotebookState?>>? _notebookGet;
    private volatile Func<int, string?, Task<NotebookCellOutputs?>>? _notebookCellOutput;
    private volatile Func<string?, Task<NotebookKernelInfo>>? _notebookKernel;
    private volatile Func<Task<IReadOnlyList<NotebookRef>>>? _notebookList;
    private volatile Func<Task<IReadOnlyList<OpenNotebookInfo>>>? _notebookOpenList;

    public void SetNotebookActions(
        Func<NotebookCommand, Task<NotebookState?>> mutate,
        Func<string?, Task<NotebookState?>> get,
        Func<int, string?, Task<NotebookCellOutputs?>> cellOutput,
        Func<string?, Task<NotebookKernelInfo>> kernel,
        Func<Task<IReadOnlyList<NotebookRef>>> list,
        Func<Task<IReadOnlyList<OpenNotebookInfo>>> openList)
    {
        _notebookMutate = mutate;
        _notebookGet = get;
        _notebookCellOutput = cellOutput;
        _notebookKernel = kernel;
        _notebookList = list;
        _notebookOpenList = openList;
    }

    public Task<NotebookState?> NotebookMutateAsync(NotebookCommand cmd)
        => _notebookMutate?.Invoke(cmd) ?? Task.FromResult<NotebookState?>(null);

    public Task<NotebookState?> GetNotebookAsync(string? notebook = null)
        => _notebookGet?.Invoke(notebook) ?? Task.FromResult<NotebookState?>(null);

    public Task<NotebookCellOutputs?> GetCellOutputAsync(int index, string? notebook = null)
        => _notebookCellOutput?.Invoke(index, notebook) ?? Task.FromResult<NotebookCellOutputs?>(null);

    public Task<NotebookKernelInfo> GetKernelStateAsync(string? notebook = null)
        => _notebookKernel?.Invoke(notebook) ?? Task.FromResult(new NotebookKernelInfo("Dead", "no notebook open", ""));

    public Task<IReadOnlyList<NotebookRef>> ListNotebooksAsync()
        => _notebookList?.Invoke() ?? Task.FromResult<IReadOnlyList<NotebookRef>>(Array.Empty<NotebookRef>());

    public Task<IReadOnlyList<OpenNotebookInfo>> ListOpenNotebooksAsync()
        => _notebookOpenList?.Invoke() ?? Task.FromResult<IReadOnlyList<OpenNotebookInfo>>(Array.Empty<OpenNotebookInfo>());

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
