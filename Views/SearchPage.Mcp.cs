using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;

namespace CanfarDesktop.Views;

/// <summary>
/// The Search page's MCP surface: UI-thread methods behind the search tools (get/set form, Additional
/// Constraints facets, run/reset, ADQL editor, results-table steering, export, and the side-panel
/// pickers). MainWindow marshals MCP calls here via its OnUi helpers, so every method in this file runs
/// on the UI thread and may touch the controls and the ViewModel directly. Each write ends by surfacing
/// the relevant tab, mirroring what the equivalent click leaves on screen.
/// </summary>
public sealed partial class SearchPage
{
    // ── Form ─────────────────────────────────────────────────────────────────

    public SearchFormSnapshot McpGetForm() => BuildFormSnapshot();

    public SearchFormSnapshot McpSetForm(SearchFormPatch p)
    {
        var vm = ViewModel;

        if (p.ObservationId is not null) vm.ObservationId = p.ObservationId;
        if (p.PiName is not null) vm.ProposalPi = p.PiName;
        if (p.ProposalId is not null) vm.ProposalId = p.ProposalId;
        if (p.ProposalTitle is not null) vm.ProposalTitle = p.ProposalTitle;
        if (p.Keywords is not null) vm.ProposalKeywords = p.Keywords;
        if (p.DataRelease is not null) vm.DataRelease = p.DataRelease;
        if (p.PublicOnly is { } pub) vm.PublicOnly = pub;
        if (p.Intent is not null) vm.Intent = p.Intent.ToLowerInvariant();

        // Resolver before target, so a target set in the same call resolves with the requested service.
        if (p.Resolver is not null) vm.ResolverService = p.Resolver.ToUpperInvariant();
        if (p.Target is not null) vm.Target = p.Target;
        if (p.RadiusDeg is { } radius) vm.SearchRadius = radius;
        if (p.PixelScale is not null) vm.PixelScale = p.PixelScale;
        if (p.PixelScaleUnit is not null) vm.PixelScaleUnit = p.PixelScaleUnit;
        if (p.SpatialCutout is { } spatialCut) vm.SpatialCutout = spatialCut;

        // Preset before explicit date, so an explicit observationDate in the same call wins.
        if (p.DatePreset is not null) vm.DatePreset = p.DatePreset;
        if (p.ObservationDate is not null) vm.ObservationDate = p.ObservationDate;
        if (p.IntegrationTime is not null) vm.IntegrationTimeMin = p.IntegrationTime;
        if (p.IntegrationTimeUnit is not null) vm.IntegrationTimeUnit = p.IntegrationTimeUnit;
        if (p.TimeSpanRange is not null) vm.TimeSpan = p.TimeSpanRange;
        if (p.TimeSpanUnit is not null) vm.TimeSpanUnit = p.TimeSpanUnit;

        if (p.SpectralCoverage is not null) vm.SpectralCoverage = p.SpectralCoverage;
        if (p.SpectralCoverageUnit is not null) vm.SpectralCoverageUnit = p.SpectralCoverageUnit;
        if (p.SpectralSampling is not null) vm.SpectralSampling = p.SpectralSampling;
        if (p.SpectralSamplingUnit is not null) vm.SpectralSamplingUnit = p.SpectralSamplingUnit;
        if (p.ResolvingPower is not null) vm.ResolvingPower = p.ResolvingPower;
        if (p.BandpassWidth is not null) vm.BandpassWidth = p.BandpassWidth;
        if (p.BandpassWidthUnit is not null) vm.BandpassWidthUnit = p.BandpassWidthUnit;
        if (p.RestFrameEnergy is not null) vm.RestFrameEnergy = p.RestFrameEnergy;
        if (p.RestFrameEnergyUnit is not null) vm.RestFrameEnergyUnit = p.RestFrameEnergyUnit;
        if (p.SpectralCutout is { } spectralCut) vm.SpectralCutout = spectralCut;

        if (p.MaxRecords is { } max) vm.MaxRecords = max;

        ShowSearchForm();
        return BuildFormSnapshot();
    }

    public SearchFormSnapshot McpResetForm()
    {
        ViewModel.ClearForm();
        _dataTrainMgr.ClearAll();
        if (_dataTrainUIBuilt) SyncAllTrainLists();
        ShowSearchForm();
        return BuildFormSnapshot();
    }

    // ── Additional Constraints (data train facets) ───────────────────────────

    public async Task<SearchFacetsSnapshot> McpGetConstraintsAsync()
    {
        await EnsureDataTrainUiAsync();
        return BuildFacetsSnapshot();
    }

    public async Task<SearchConstraintsOutcome> McpSetConstraintsAsync(SearchFacetSelections sel)
    {
        await EnsureDataTrainUiAsync();
        var mgr = _dataTrainMgr;
        if (!mgr.IsLoaded)
            throw new McpToolException(new BackendError(
                "the Additional Constraints facet data could not be loaded (data-train fetch failed); retry shortly"));

        if (sel.ClearAll) mgr.ClearAll();

        // Replace each provided facet's selection, then let one cascade Refresh prune anything invalid
        // (unknown values and downstream picks the upstream narrowing removed) — same as the UI cascade.
        var requested = new List<(HashSet<string> Set, List<string> Values)>();
        void Apply(HashSet<string> set, IReadOnlyList<string>? values)
        {
            if (values is null) return;
            var cleaned = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
            set.Clear();
            foreach (var v in cleaned) set.Add(v);
            requested.Add((set, cleaned));
        }

        Apply(mgr.SelectedBands, sel.Bands);
        Apply(mgr.SelectedCollections, sel.Collections);
        Apply(mgr.SelectedInstruments, sel.Instruments);
        Apply(mgr.SelectedFilters, sel.Filters);
        Apply(mgr.SelectedCalLevels, sel.CalLevels);
        Apply(mgr.SelectedDataTypes, sel.DataTypes);
        Apply(mgr.SelectedObsTypes, sel.ObsTypes);
        mgr.Refresh();

        var dropped = requested
            .SelectMany(r => r.Values.Where(v => !r.Set.Contains(v)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (_dataTrainUIBuilt) SyncAllTrainLists();
        SyncDataTrainToViewModel();
        ConstraintsExpander.IsExpanded = true;
        ShowSearchForm();
        return new SearchConstraintsOutcome(true, dropped, BuildFacetsSnapshot());
    }

    /// <summary>Load the data train and build the facet UI, waiting for rows (unlike the lazy page load).</summary>
    private async Task EnsureDataTrainUiAsync()
    {
        if (_dataTrainMgr.IsLoaded) return;
        _dataTrainLoaded = true; // the background page load need not repeat this
        await ViewModel.EnsureDataTrainAsync();
        var rows = ViewModel.AllDataTrainRows.ToList();
        if (rows.Count > 0)
        {
            _dataTrainMgr.Load(rows);
            RebuildAllCheckColumns();
        }
    }

    // ── Run / ADQL ───────────────────────────────────────────────────────────

    public async Task<SearchRunOutcome> McpRunSearchAsync()
    {
        var vm = ViewModel;
        if (vm.IsSearching) return AlreadySearching();
        SyncDataTrainToViewModel();
        await vm.SearchCommand.ExecuteAsync(null);
        return FinishRun();
    }

    public AdqlStageOutcome McpSetAdql(string adql)
    {
        ViewModel.AdqlText = adql;
        MainPivot.SelectedIndex = 2; // ADQL Editor tab
        return new AdqlStageOutcome(true, adql);
    }

    public async Task<SearchRunOutcome> McpExecuteAdqlAsync(string? adql)
    {
        var vm = ViewModel;
        if (vm.IsSearching) return AlreadySearching();
        if (adql is not null) vm.AdqlText = adql;
        if (string.IsNullOrWhiteSpace(vm.AdqlText))
            return new SearchRunOutcome(false, null, 0, null,
                "the ADQL editor is empty — pass adql, or stage a query with set_adql_query");
        await vm.ExecuteAdqlCommand.ExecuteAsync(null);
        return FinishRun();
    }

    public async Task<SearchRunOutcome> McpRunSavedQueryAsync(string name)
    {
        var vm = ViewModel;
        vm.LoadSavedQueriesFromStore();
        var query = vm.SavedQueries.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.Ordinal));
        if (query is null)
            throw new McpToolException(new UnknownTarget($"no saved query named '{name}'"));
        if (vm.IsSearching) return AlreadySearching();
        vm.AdqlText = query.Adql;
        await vm.ExecuteAdqlCommand.ExecuteAsync(null);
        return FinishRun();
    }

    private SearchRunOutcome AlreadySearching()
        => new(false, ViewModel.AdqlText, 0, ViewModel.StatusMessage, "a search is already running — retry shortly");

    /// <summary>After a VM search/execute: render + surface the Results tab, and shape the outcome.</summary>
    private SearchRunOutcome FinishRun()
    {
        var vm = ViewModel;
        if (vm.HasError)
            return new SearchRunOutcome(false, vm.AdqlText, 0, vm.StatusMessage, vm.ErrorMessage);
        if (vm.Results is null)
            return new SearchRunOutcome(false, vm.AdqlText, 0, vm.StatusMessage, "no results returned");

        RowsPerPageCombo.SelectedItem = vm.RowsPerPage;
        RenderResultsPage(resetScroll: true);
        MainPivot.SelectedIndex = 1; // Results tab
        return new SearchRunOutcome(true, vm.AdqlText, vm.Results.TotalRows, vm.StatusMessage, null);
    }

    // ── Results table ────────────────────────────────────────────────────────

    public SearchResultsSnapshot McpGetResults(bool includeRows, int maxRows)
        => BuildResultsSnapshot(includeRows, maxRows);

    public SearchResultsSnapshot McpApplyResultsView(SearchResultsCommand cmd)
    {
        var vm = ViewModel;
        if (vm.Results is null || vm.Results.TotalRows == 0)
            throw new McpToolException(new UnknownTarget(
                "no search results yet — run run_search or execute_adql_query first"));

        // Validate every referenced column key up front (virtual action columns are not steerable).
        var keyed = vm.ResultColumns
            .Where(c => c.Key is not ("download" or "preview"))
            .ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        string Key(string requested)
            => keyed.TryGetValue(requested, out var col)
                ? col.Key
                : throw new McpToolException(new InvalidArgument(
                    $"unknown column key '{requested}' — see get_search_results for the column set"));

        if (cmd.ClearFilters)
            foreach (var k in vm.ActiveColumnFilters.Keys)
                vm.SetColumnFilter(k, string.Empty);

        if (cmd.SetFilters is { Count: > 0 })
            foreach (var (col, text) in cmd.SetFilters)
                vm.SetColumnFilter(Key(col), text);

        if (cmd.SortColumn is not null)
            vm.SetSort(Key(cmd.SortColumn), cmd.SortAscending ?? true);

        if (cmd.ShowColumns is { Count: > 0 })
            foreach (var col in cmd.ShowColumns)
                keyed[Key(col)].Visible = true;
        if (cmd.HideColumns is { Count: > 0 })
            foreach (var col in cmd.HideColumns)
                keyed[Key(col)].Visible = false;

        if (cmd.ColumnUnits is { Count: > 0 })
        {
            foreach (var (col, unit) in cmd.ColumnUnits)
            {
                var key = Key(col);
                if (!ColumnUnitCatalog.HasMenu(key))
                    throw new McpToolException(new InvalidArgument($"column '{key}' has no display-unit menu"));
                if (unit.Length == 0)
                {
                    vm.SetUnit(key, null); // back to the column default
                    continue;
                }
                var valid = ColumnUnitCatalog.AvailableUnits(key).Select(u => u.Id).ToList();
                if (!valid.Contains(unit, StringComparer.OrdinalIgnoreCase))
                    throw new McpToolException(new InvalidArgument(
                        $"unknown unit '{unit}' for column '{key}' — available: {string.Join(", ", valid)}"));
                vm.SetUnit(key, unit);
            }
        }

        if (cmd.RowsPerPage is { } rpp)
        {
            vm.RowsPerPage = rpp;
            vm.CurrentPage = 1;
            RowsPerPageCombo.SelectedItem = rpp;
        }

        vm.UpdatePagination();
        switch (cmd.PageAction?.ToLowerInvariant())
        {
            case "first": vm.GoToFirstPage(); break;
            case "prev": vm.GoToPreviousPage(); break;
            case "next": vm.GoToNextPage(); break;
            case "last": vm.GoToLastPage(); break;
        }
        if (cmd.Page is { } page)
        {
            vm.CurrentPage = page;
            vm.UpdatePagination(); // clamps into [1, TotalPages]
        }

        vm.UpdatePagination();
        RenderResultsPage(rebuildHeader: true, resetScroll: true);
        UpdateApplyFiltersButton();
        MainPivot.SelectedIndex = 1;

        if (cmd.ApplyFiltersToAdql)
        {
            var filtered = vm.BuildFilteredAdql();
            if (filtered is null)
                throw new McpToolException(new InvalidArgument(
                    "applyFiltersToAdql needs at least one active column filter (set one via setFilters)"));
            vm.AdqlText = filtered;
            MainPivot.SelectedIndex = 2; // ADQL Editor, same as the UI button
        }

        return BuildResultsSnapshot(includeRows: true, maxRows: vm.RowsPerPage);
    }

    public async Task<SearchExportOutcome> McpExportResultsAsync(string format, string? path)
    {
        var vm = ViewModel;
        var content = format == "csv" ? vm.ExportResultsCsv() : vm.ExportResultsTsv();
        if (content.Length == 0)
            return new SearchExportOutcome(false, null, 0, "no search results to export — run a search first");

        var dest = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Verbinal",
            $"search_results_{DateTime.Now:yyyyMMdd_HHmmss}.{format}");
        await Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(dest, content);
        });
        return new SearchExportOutcome(true, dest, vm.Results?.Rows.Count ?? 0, null);
    }

    // ── Side panel: recent searches ──────────────────────────────────────────

    public LoadRecentSearchOutcome McpLoadRecentSearch(int index)
    {
        var vm = ViewModel;
        vm.LoadRecentSearchesFromStore(); // fresh, store-ordered (matches list_recent_searches)
        if (index >= vm.RecentSearches.Count)
            return new LoadRecentSearchOutcome(false, null,
                $"no recent search at index {index} (history has {vm.RecentSearches.Count} entries)", null);

        var search = vm.RecentSearches[index];
        LoadRecentSearchCore(search);
        return new LoadRecentSearchOutcome(true, search.Summary, null, BuildFormSnapshot());
    }

    // ── Snapshots ────────────────────────────────────────────────────────────

    private SearchFormSnapshot BuildFormSnapshot()
    {
        var vm = ViewModel;
        return new SearchFormSnapshot
        {
            ObservationId = vm.ObservationId,
            PiName = vm.ProposalPi,
            ProposalId = vm.ProposalId,
            ProposalTitle = vm.ProposalTitle,
            Keywords = vm.ProposalKeywords,
            DataRelease = vm.DataRelease,
            PublicOnly = vm.PublicOnly,
            Intent = vm.Intent,
            Target = vm.Target,
            Resolver = vm.ResolverService,
            ResolverStatus = vm.IsResolving ? "Resolving..." : vm.ResolverStatus,
            ResolvedRa = vm.ResolvedRA,
            ResolvedDec = vm.ResolvedDec,
            RadiusDeg = vm.SearchRadius,
            PixelScale = vm.PixelScale,
            PixelScaleUnit = vm.PixelScaleUnit,
            SpatialCutout = vm.SpatialCutout,
            ObservationDate = vm.ObservationDate,
            DatePreset = vm.DatePreset,
            IntegrationTime = vm.IntegrationTimeMin,
            IntegrationTimeUnit = vm.IntegrationTimeUnit,
            TimeSpanRange = vm.TimeSpan,
            TimeSpanUnit = vm.TimeSpanUnit,
            SpectralCoverage = vm.SpectralCoverage,
            SpectralCoverageUnit = vm.SpectralCoverageUnit,
            SpectralSampling = vm.SpectralSampling,
            SpectralSamplingUnit = vm.SpectralSamplingUnit,
            ResolvingPower = vm.ResolvingPower,
            BandpassWidth = vm.BandpassWidth,
            BandpassWidthUnit = vm.BandpassWidthUnit,
            RestFrameEnergy = vm.RestFrameEnergy,
            RestFrameEnergyUnit = vm.RestFrameEnergyUnit,
            SpectralCutout = vm.SpectralCutout,
            MaxRecords = vm.MaxRecords,
            AdqlText = vm.AdqlText,
            IsSearching = vm.IsSearching,
            Bands = SelectedFacet(_dataTrainMgr.SelectedBands, vm.SelectedBands),
            Collections = SelectedFacet(_dataTrainMgr.SelectedCollections, vm.SelectedCollections),
            Instruments = SelectedFacet(_dataTrainMgr.SelectedInstruments, vm.SelectedInstruments),
            Filters = SelectedFacet(_dataTrainMgr.SelectedFilters, vm.SelectedFilters),
            CalLevels = SelectedFacet(_dataTrainMgr.SelectedCalLevels, vm.SelectedCalLevels),
            DataTypes = SelectedFacet(_dataTrainMgr.SelectedDataTypes, vm.SelectedDataTypes),
            ObsTypes = SelectedFacet(_dataTrainMgr.SelectedObsTypes, vm.SelectedObsTypes),
        };
    }

    /// <summary>The live facet selection: the data-train manager once loaded, else the VM's restored state.</summary>
    private IReadOnlyList<string> SelectedFacet(HashSet<string> manager, IEnumerable<string> viewModel)
        => (_dataTrainMgr.IsLoaded ? manager.AsEnumerable() : viewModel)
            .OrderBy(v => v, StringComparer.Ordinal).ToList();

    private SearchFacetsSnapshot BuildFacetsSnapshot()
    {
        var mgr = _dataTrainMgr;
        return new SearchFacetsSnapshot
        {
            Loaded = mgr.IsLoaded,
            RowCount = mgr.RowCount,
            Bands = Facet(mgr.AvailableBands, mgr.SelectedBands),
            Collections = Facet(mgr.AvailableCollections, mgr.SelectedCollections),
            Instruments = Facet(mgr.AvailableInstruments, mgr.SelectedInstruments),
            Filters = Facet(mgr.AvailableFilters, mgr.SelectedFilters),
            CalLevels = Facet(mgr.AvailableCalLevels, mgr.SelectedCalLevels),
            DataTypes = Facet(mgr.AvailableDataTypes, mgr.SelectedDataTypes),
            ObsTypes = Facet(mgr.AvailableObsTypes, mgr.SelectedObsTypes),
        };
    }

    private static SearchFacetView Facet(HashSet<string> available, HashSet<string> selected)
        => new(
            available.OrderBy(v => v, StringComparer.Ordinal).ToList(),
            selected.OrderBy(v => v, StringComparer.Ordinal).ToList());

    private SearchResultsSnapshot BuildResultsSnapshot(bool includeRows, int maxRows)
    {
        var vm = ViewModel;
        if (vm.Results is null || vm.Results.TotalRows == 0)
            return new SearchResultsSnapshot { HasResults = false, Status = vm.StatusMessage, Adql = vm.AdqlText };

        vm.UpdatePagination();
        var sort = vm.CurrentSort;
        var snapshot = new SearchResultsSnapshot
        {
            HasResults = true,
            Status = vm.StatusMessage,
            Adql = vm.AdqlText,
            TotalRows = vm.Results.TotalRows,
            FilteredRows = vm.FilteredRowCount,
            CurrentPage = vm.CurrentPage,
            TotalPages = vm.TotalPages,
            RowsPerPage = vm.RowsPerPage,
            PageStatus = vm.PageStatus,
            SortColumn = sort.Key,
            SortAscending = sort.Ascending,
            Filters = vm.ActiveColumnFilters,
            Columns = vm.ResultColumns
                .Where(c => c.Key is not ("download" or "preview"))
                .Select(c => new SearchResultColumnView(c.Key, c.Label, c.Visible))
                .ToList(),
        };
        if (!includeRows) return snapshot;

        var headers = vm.Results.Columns;
        var rows = vm.GetCurrentPageRows()
            .Take(maxRows)
            .Select(r => (IReadOnlyList<string>)headers.Select(r.Get).ToList())
            .ToList();
        return new SearchResultsSnapshot
        {
            HasResults = snapshot.HasResults,
            Status = snapshot.Status,
            Adql = snapshot.Adql,
            TotalRows = snapshot.TotalRows,
            FilteredRows = snapshot.FilteredRows,
            CurrentPage = snapshot.CurrentPage,
            TotalPages = snapshot.TotalPages,
            RowsPerPage = snapshot.RowsPerPage,
            PageStatus = snapshot.PageStatus,
            SortColumn = snapshot.SortColumn,
            SortAscending = snapshot.SortAscending,
            Filters = snapshot.Filters,
            Columns = snapshot.Columns,
            RowColumns = headers,
            Rows = rows,
        };
    }
}
