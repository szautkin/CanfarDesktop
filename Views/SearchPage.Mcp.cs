using System.Diagnostics;
using System.Globalization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views;

/// <summary>
/// The Search page's agent-facing half: the live implementation of <see cref="ISearchUiBridge"/>.
///
/// It lives in its own file rather than in <c>SearchPage.xaml.cs</c> because it is a different job —
/// the page's own file is 1,200 lines of building and reacting to widgets, and steering them from
/// outside is a surface with its own vocabulary and its own failure modes. (The cube viewer's
/// <c>CubeViewerPage.Mcp.cs</c> is split the same way.)
///
/// Every method here is entered from an MCP connection thread and does its work through
/// <see cref="UiDispatch"/>, because everything it touches is a XAML object. Two rules hold
/// throughout:
///
/// * <b>Answer, do not throw, about state.</b> "No results yet", "the data train has not loaded" and
///   "no such column" are answers an agent can act on.
/// * <b>Validate everything, then apply.</b> A patch naming one field that does not exist changes
///   nothing at all. A half-applied form is a state neither side can reason about — and with the
///   facets, which cascade, it is not even a state the page can rebuild.
/// </summary>
public sealed partial class SearchPage : ISearchUiBridge
{
    // ── Field vocabulary ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// How one form field is read and written, and the unit field it defers to. One table serves
    /// get_search_form and set_search_form, so the two can never disagree about what a field is
    /// called — the failure mode of writing them separately is a field an agent can read and not set.
    ///
    /// <see cref="Bind"/> parses a value and returns the writer for it, rather than parsing and writing
    /// in one step. That split is what makes a patch atomic: every value in the patch is bound first,
    /// so a bad number in the last field is raised before the first field has been touched — and the
    /// check and the write are the same code, so they cannot drift apart.
    /// </summary>
    private sealed record FormFieldSpec(
        string Name,
        Func<SearchViewModel, string> Get,
        Func<string?, Action<SearchViewModel>> Bind,
        string? UnitField = null,
        Func<IReadOnlyList<string>>? Options = null);

    private static string Bool(bool b) => b ? "true" : "false";

    /// <summary>Parse a form boolean. Anything else is named rather than silently read as false.</summary>
    private static bool ParseBool(string name, string? v) => (v ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" or "" => false,
        _ => throw new McpToolException(new InvalidArgument($"'{name}' takes true or false — got '{v}'")),
    };

    private static double ParseDouble(string name, string? v)
        => double.TryParse((v ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new McpToolException(new InvalidArgument($"'{name}' takes a number — got '{v}'"));

    private static int ParseInt(string name, string? v)
        => int.TryParse((v ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw new McpToolException(new InvalidArgument($"'{name}' takes a whole number — got '{v}'"));

    /// <summary>Accept one of a fixed set, case-insensitively, and name the set when it is not one of them.</summary>
    private static string OneOf(string name, string? v, IReadOnlyList<string> allowed, bool emptyOk = true)
    {
        var s = (v ?? string.Empty).Trim();
        if (s.Length == 0 && emptyOk) return string.Empty;
        var match = allowed.FirstOrDefault(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new McpToolException(new InvalidArgument(
            $"'{name}' takes one of: {string.Join(", ", allowed)} — got '{s}'"));
    }

    private static readonly string[] IntentValues = ["science", "calibration"];

    /// <summary>Mirrors <see cref="SearchViewModel.ResolverServices"/>, which is the dropdown's own list.</summary>
    private static readonly string[] ResolverServiceValues = ["ALL", "SIMBAD", "NED", "VIZIER", "NONE"];

    private static readonly FormFieldSpec[] FormSpecs = BuildFormSpecs();

    private static FormFieldSpec[] BuildFormSpecs()
    {
        static IReadOnlyList<string> Spectral() => UnitConverter.SpectralUnits;
        static IReadOnlyList<string> Time() => UnitConverter.TimeUnits;
        static IReadOnlyList<string> Pixel() => UnitConverter.PixelScaleUnits;

        // Free text: the form's own fields take range syntax ("2020..2021", "> 2019"), so the only
        // thing to do to a string here is trim it.
        static FormFieldSpec Text(string name, Func<SearchViewModel, string> get, Action<SearchViewModel, string> set,
                                  string? unitField = null, Func<IReadOnlyList<string>>? options = null)
            => new(name, get, v => { var s = (v ?? string.Empty).Trim(); return vm => set(vm, s); }, unitField, options);

        static FormFieldSpec Flag(string name, Func<SearchViewModel, bool> get, Action<SearchViewModel, bool> set)
            => new(name, vm => Bool(get(vm)), v => { var b = ParseBool(name, v); return vm => set(vm, b); });

        static FormFieldSpec Choice(string name, Func<SearchViewModel, string> get, Action<SearchViewModel, string> set,
                                    Func<IReadOnlyList<string>> options, bool emptyOk = true)
            => new(name, get, v => { var c = OneOf(name, v, options(), emptyOk); return vm => set(vm, c); }, null, options);

        FormFieldSpec[] specs =
        [
            // Observation
            Text("observationId", vm => vm.ObservationId, (vm, v) => vm.ObservationId = v),
            Text("proposalPi", vm => vm.ProposalPi, (vm, v) => vm.ProposalPi = v),
            Text("proposalId", vm => vm.ProposalId, (vm, v) => vm.ProposalId = v),
            Text("proposalTitle", vm => vm.ProposalTitle, (vm, v) => vm.ProposalTitle = v),
            Text("proposalKeywords", vm => vm.ProposalKeywords, (vm, v) => vm.ProposalKeywords = v),
            Choice("intent", vm => vm.Intent, (vm, v) => vm.Intent = v, () => IntentValues),
            Flag("publicOnly", vm => vm.PublicOnly, (vm, v) => vm.PublicOnly = v),

            // Spatial
            Text("target", vm => vm.Target, (vm, v) => vm.Target = v),
            Choice("resolverService", vm => vm.ResolverService, (vm, v) => vm.ResolverService = v,
                   () => ResolverServiceValues, emptyOk: false),
            new("searchRadius", vm => vm.SearchRadius.ToString("G10", CultureInfo.InvariantCulture),
                v => { var d = ParseDouble("searchRadius", v); return vm => vm.SearchRadius = d; }),
            Text("pixelScale", vm => vm.PixelScale, (vm, v) => vm.PixelScale = v, "pixelScaleUnit", Pixel),
            Choice("pixelScaleUnit", vm => vm.PixelScaleUnit, (vm, v) => vm.PixelScaleUnit = v, Pixel, emptyOk: false),
            Flag("spatialCutout", vm => vm.SpatialCutout, (vm, v) => vm.SpatialCutout = v),

            // Temporal
            Text("observationDate", vm => vm.ObservationDate, (vm, v) => vm.ObservationDate = v),
            Text("datePreset", vm => vm.DatePreset, (vm, v) => vm.DatePreset = v),
            Text("dateStart", vm => vm.DateStart, (vm, v) => vm.DateStart = v),
            Text("dateEnd", vm => vm.DateEnd, (vm, v) => vm.DateEnd = v),
            Text("integrationTimeMin", vm => vm.IntegrationTimeMin, (vm, v) => vm.IntegrationTimeMin = v, "integrationTimeUnit", Time),
            Text("integrationTimeMax", vm => vm.IntegrationTimeMax, (vm, v) => vm.IntegrationTimeMax = v, "integrationTimeUnit", Time),
            Choice("integrationTimeUnit", vm => vm.IntegrationTimeUnit, (vm, v) => vm.IntegrationTimeUnit = v, Time, emptyOk: false),
            Text("timeSpan", vm => vm.TimeSpan, (vm, v) => vm.TimeSpan = v, "timeSpanUnit", Time),
            Choice("timeSpanUnit", vm => vm.TimeSpanUnit, (vm, v) => vm.TimeSpanUnit = v, Time, emptyOk: false),
            Text("dataRelease", vm => vm.DataRelease, (vm, v) => vm.DataRelease = v),

            // Spectral — each value field carries its OWN unit: a coverage in nm beside a sampling in
            // GHz is routine, and one shared dropdown could not say it.
            Text("wavelengthMin", vm => vm.WavelengthMin, (vm, v) => vm.WavelengthMin = v),
            Text("wavelengthMax", vm => vm.WavelengthMax, (vm, v) => vm.WavelengthMax = v),
            Text("spectralCoverage", vm => vm.SpectralCoverage, (vm, v) => vm.SpectralCoverage = v, "spectralCoverageUnit", Spectral),
            Choice("spectralCoverageUnit", vm => vm.SpectralCoverageUnit, (vm, v) => vm.SpectralCoverageUnit = v, Spectral, emptyOk: false),
            Text("spectralSampling", vm => vm.SpectralSampling, (vm, v) => vm.SpectralSampling = v, "spectralSamplingUnit", Spectral),
            Choice("spectralSamplingUnit", vm => vm.SpectralSamplingUnit, (vm, v) => vm.SpectralSamplingUnit = v, Spectral, emptyOk: false),
            Text("resolvingPower", vm => vm.ResolvingPower, (vm, v) => vm.ResolvingPower = v),
            Text("bandpassWidth", vm => vm.BandpassWidth, (vm, v) => vm.BandpassWidth = v, "bandpassWidthUnit", Spectral),
            Choice("bandpassWidthUnit", vm => vm.BandpassWidthUnit, (vm, v) => vm.BandpassWidthUnit = v, Spectral, emptyOk: false),
            Text("restFrameEnergy", vm => vm.RestFrameEnergy, (vm, v) => vm.RestFrameEnergy = v, "restFrameEnergyUnit", Spectral),
            Choice("restFrameEnergyUnit", vm => vm.RestFrameEnergyUnit, (vm, v) => vm.RestFrameEnergyUnit = v, Spectral, emptyOk: false),
            Flag("spectralCutout", vm => vm.SpectralCutout, (vm, v) => vm.SpectralCutout = v),

            // General
            new("maxRecords", vm => vm.MaxRecords.ToString(CultureInfo.InvariantCulture),
                v => { var i = ParseInt("maxRecords", v); return vm => vm.MaxRecords = i; }),
        ];

        AssertVocabularyMatches("set_search_form", specs.Select(s => s.Name), SearchToolArgs.FormFields);
        return specs;
    }

    /// <summary>
    /// The two halves of the vocabulary agree: what a tool's schema ADVERTISES
    /// (<see cref="SearchToolArgs"/>) and what this file can actually APPLY. They are separate because
    /// a tool must not depend on a XAML page — and separate things drift. An advertised name with
    /// nothing behind it would be accepted, reported as applied, and do nothing at all, which is the
    /// one failure an agent cannot see. Checked once, when the table is built, in Debug builds.
    /// </summary>
    [Conditional("DEBUG")]
    private static void AssertVocabularyMatches(string tool, IEnumerable<string> implemented, IEnumerable<string> advertised)
    {
        var mine = implemented.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var theirs = advertised.OrderBy(n => n, StringComparer.Ordinal).ToList();
        Debug.Assert(mine.SequenceEqual(theirs, StringComparer.Ordinal),
            $"{tool}: the schema and the page disagree about the vocabulary. " +
            $"Only in the schema: {string.Join(", ", theirs.Except(mine))}. " +
            $"Only in the page: {string.Join(", ", mine.Except(theirs))}.");
    }

    /// <summary>How one facet is read and written. Same reason as <see cref="FormSpecs"/>: one table, two directions.</summary>
    private sealed record FacetSpec(string Name, int ColumnIndex,
        Func<DataTrainManager, IReadOnlyCollection<string>> Available,
        Func<DataTrainManager, HashSet<string>> Selected);

    private static readonly FacetSpec[] FacetSpecs = BuildFacetSpecs();

    private static FacetSpec[] BuildFacetSpecs()
    {
        // The column index is the DataTrainManager's own ordering (and the Tag on each facet ListView
        // in the XAML), which is also the cascade order: bands narrow collections, collections narrow
        // instruments, and so on down.
        FacetSpec[] specs =
        [
            new("bands", 0, m => m.AvailableBands, m => m.SelectedBands),
            new("collections", 1, m => m.AvailableCollections, m => m.SelectedCollections),
            new("instruments", 2, m => m.AvailableInstruments, m => m.SelectedInstruments),
            new("filters", 3, m => m.AvailableFilters, m => m.SelectedFilters),
            new("calibrationLevels", 4, m => m.AvailableCalLevels, m => m.SelectedCalLevels),
            new("dataProductTypes", 5, m => m.AvailableDataTypes, m => m.SelectedDataTypes),
            new("observationTypes", 6, m => m.AvailableObsTypes, m => m.SelectedObsTypes),
        ];

        AssertVocabularyMatches("set_search_constraints", specs.Select(s => s.Name), SearchToolArgs.Facets);
        return specs;
    }

    /// <summary>The two grid columns that are buttons rather than data. Not part of the agent's vocabulary.</summary>
    private static readonly string[] VirtualColumnKeys = ["download", "preview"];

    // ── Form ────────────────────────────────────────────────────────────────────────────────────

    Task<SearchFormView> ISearchUiBridge.GetFormAsync()
        => UiDispatch.OnUi(DispatcherQueue, CaptureForm, SearchFormView.Unavailable("the Search page could not be reached"));

    private SearchFormView CaptureForm()
    {
        var vm = ViewModel;
        var fields = FormSpecs.Select(spec =>
        {
            var unit = spec.UnitField is null ? null : FormSpecs.First(f => f.Name == spec.UnitField).Get(vm);
            return new FormFieldView(spec.Name, spec.Get(vm), unit, spec.Options?.Invoke());
        }).ToList();

        return new SearchFormView(
            Available: true,
            Fields: fields,
            ResolvedRa: vm.ResolvedRA,
            ResolvedDec: vm.ResolvedDec,
            ResolverService: vm.ResolverService,
            ResolverStatus: vm.ResolverStatus,
            MaxRecords: vm.MaxRecords,
            Adql: vm.AdqlText,
            ActiveTab: TabName(MainPivot.SelectedIndex));
    }

    private static string TabName(int pivotIndex) => pivotIndex switch
    {
        0 => "form",
        1 => "results",
        2 => "adql",
        _ => "unknown",
    };

    Task<SearchFormApplied> ISearchUiBridge.SetFormAsync(SearchFormPatch patch)
        => UiDispatch.OnUi(DispatcherQueue, () => ApplyForm(patch),
            SearchFormApplied.Unavailable("the Search page could not be reached"));

    private SearchFormApplied ApplyForm(SearchFormPatch patch)
    {
        var known = FormSpecs.Select(f => f.Name).ToList();
        var unknown = patch.Fields.Keys
            .Where(k => !FormSpecs.Any(f => string.Equals(f.Name, k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unknown.Count > 0)
            return new SearchFormApplied(false, Array.Empty<string>(), unknown, known, CaptureForm(),
                "nothing was applied: a field name the form does not have");

        // Bind every value first. Binding parses, so a bad number in the last field of the patch is
        // raised here — before the first field has been written — and the form is left as it was.
        var writers = new List<(FormFieldSpec Spec, Action<SearchViewModel> Write)>();
        foreach (var (name, value) in patch.Fields)
        {
            var spec = FormSpecs.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            writers.Add((spec, spec.Bind(value)));
        }

        var changed = new List<string>();
        foreach (var (spec, write) in writers)
        {
            var before = spec.Get(ViewModel);
            write(ViewModel);
            if (!string.Equals(before, spec.Get(ViewModel), StringComparison.Ordinal))
                changed.Add(spec.Name);
        }

        ShowSearchForm();
        return new SearchFormApplied(true, changed, Array.Empty<string>(), known, CaptureForm());
    }

    // ── Constraints ─────────────────────────────────────────────────────────────────────────────

    Task<SearchConstraintsView> ISearchUiBridge.GetConstraintsAsync()
        => UiDispatch.OnUi(DispatcherQueue, CaptureConstraints,
            SearchConstraintsView.Unavailable("the Search page could not be reached"));

    private SearchConstraintsView CaptureConstraints()
    {
        if (!_dataTrainMgr.IsLoaded)
            return SearchConstraintsView.Unavailable("the data train has not loaded yet — try again shortly");

        var facets = FacetSpecs.Select(f => new FacetView(
            f.Name,
            f.Available(_dataTrainMgr).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(),
            f.Selected(_dataTrainMgr).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList())).ToList();

        return new SearchConstraintsView(true, facets);
    }

    Task<SearchConstraintsApplied> ISearchUiBridge.SetConstraintsAsync(SearchConstraintsPatch patch)
        => UiDispatch.OnUi(DispatcherQueue, () => ApplyConstraints(patch),
            SearchConstraintsApplied.Unavailable("the Search page could not be reached"));

    private SearchConstraintsApplied ApplyConstraints(SearchConstraintsPatch patch)
    {
        var known = FacetSpecs.Select(f => f.Name).ToList();

        if (!_dataTrainMgr.IsLoaded)
            return SearchConstraintsApplied.Unavailable("the data train has not loaded yet — try again shortly");

        var unknown = patch.Facets.Keys
            .Where(k => !FacetSpecs.Any(f => string.Equals(f.Name, k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unknown.Count > 0)
            return new SearchConstraintsApplied(false, Array.Empty<string>(), unknown,
                Array.Empty<FacetValueRejection>(), known, CaptureConstraints(),
                "nothing was applied: no such facet");

        // Check every value against what its facet currently offers, before touching any of them. The
        // facets cascade, so applying one selection changes what the next one offers — validating as we
        // went would judge later facets against a train the caller never saw.
        var rejected = new List<FacetValueRejection>();
        foreach (var (name, values) in patch.Facets)
        {
            var spec = FacetSpecs.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            var available = spec.Available(_dataTrainMgr);
            foreach (var v in values)
            {
                if (!available.Contains(v))
                    rejected.Add(new FacetValueRejection(spec.Name, v,
                        SearchToolArgs.DidYouMean(v, available.ToList())));
            }
        }

        if (rejected.Count > 0)
            return new SearchConstraintsApplied(false, Array.Empty<string>(), Array.Empty<string>(),
                rejected, known, CaptureConstraints(), "nothing was applied: a value the facet does not offer");

        var changed = new List<string>();
        foreach (var (name, values) in patch.Facets)
        {
            var spec = FacetSpecs.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            var selected = spec.Selected(_dataTrainMgr);
            if (selected.SetEquals(values)) continue;
            selected.Clear();
            foreach (var v in values) selected.Add(v);
            changed.Add(spec.Name);
        }

        if (changed.Count > 0)
        {
            _dataTrainMgr.Refresh();
            if (_dataTrainUIBuilt) SyncAllTrainLists();
            SyncDataTrainToViewModel();
        }

        ShowSearchForm();
        return new SearchConstraintsApplied(true, changed, Array.Empty<string>(),
            Array.Empty<FacetValueRejection>(), known, CaptureConstraints());
    }

    // ── Running ─────────────────────────────────────────────────────────────────────────────────

    Task<SearchRunOutcome> ISearchUiBridge.RunSearchAsync()
        => UiDispatch.OnUiAsync(DispatcherQueue, RunSearchOnUi,
            SearchRunOutcome.Unavailable("the Search page could not be reached"));

    private async Task<SearchRunOutcome> RunSearchOnUi()
    {
        if (ViewModel.IsSearching)
            return SearchRunOutcome.Unavailable("a search is already running");

        SyncDataTrainToViewModel();
        return await ExecuteAndShowAsync(() => ViewModel.SearchCommand.ExecuteAsync(null));
    }

    /// <summary>
    /// Run a query the way the buttons do — execute, then show the Results tab with a fresh render —
    /// and report what happened. Both entry points (the form and the ADQL editor) go through here, so
    /// an agent-run query leaves the page in exactly the state a click would have.
    /// </summary>
    private async Task<SearchRunOutcome> ExecuteAndShowAsync(Func<Task> execute)
    {
        var sw = Stopwatch.StartNew();
        await execute();
        sw.Stop();

        var vm = ViewModel;
        if (vm.HasError)
            return new SearchRunOutcome(false, vm.AdqlText, 0, false, vm.MaxRecords, sw.Elapsed.TotalMilliseconds,
                vm.ErrorMessage, "the query failed");

        if (vm.Results is null)
            return new SearchRunOutcome(false, vm.AdqlText, 0, false, vm.MaxRecords, sw.Elapsed.TotalMilliseconds,
                null, "the query returned nothing at all");

        RowsPerPageCombo.SelectedItem = vm.RowsPerPage;
        RenderResultsPage(resetScroll: true);
        MainPivot.SelectedIndex = 1;

        var total = vm.Results.TotalRows;
        return new SearchRunOutcome(true, vm.AdqlText, total, total >= vm.MaxRecords, vm.MaxRecords,
            sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// The pre-flight, for a query that arrived over MCP. A query an agent sends is a query a person
    /// could have typed, so it gets the same check the Execute button gets — reported as a refusal
    /// naming the offending text, rather than spent on a round trip the service will reject.
    /// </summary>
    private SearchRunOutcome? RefuseIfInvalid(string adql)
    {
        var problems = AdqlValidator.Problems(adql, _tapSchema.Cached());
        if (problems.Count == 0) return null;

        return new SearchRunOutcome(false, adql, 0, false, ViewModel.MaxRecords, 0,
            string.Join("; ", problems.Select(p => p.Describe())),
            "the query was not sent: it names something this service does not have");
    }

    Task<SearchAdqlOutcome> ISearchUiBridge.SetAdqlAsync(string adql, bool execute)
        => UiDispatch.OnUiAsync(DispatcherQueue, async () =>
        {
            if (execute && ViewModel.IsSearching)
                return SearchAdqlOutcome.Unavailable("a search is already running");

            ViewModel.AdqlText = adql;
            MainPivot.SelectedIndex = 2;
            RecheckAdql();

            if (!execute)
                return new SearchAdqlOutcome(true, adql, false);

            // Applied but not run: the user can see and fix it, which is the point of putting it in the
            // editor rather than running it headlessly.
            if (RefuseIfInvalid(adql) is { } refused)
                return new SearchAdqlOutcome(true, adql, false, refused, refused.Message);

            var run = await ExecuteAndShowAsync(() => ViewModel.ExecuteAdqlCommand.ExecuteAsync(null));
            return new SearchAdqlOutcome(true, adql, true, run, run.Message);
        }, SearchAdqlOutcome.Unavailable("the Search page could not be reached"));

    Task<SearchRunOutcome> ISearchUiBridge.RunSavedQueryAsync(string name)
        => UiDispatch.OnUiAsync(DispatcherQueue, async () =>
        {
            var query = ViewModel.SavedQueries.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.Ordinal))
                     ?? ViewModel.SavedQueries.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));

            if (query is null)
                return SearchRunOutcome.Unavailable(
                    ViewModel.SavedQueries.Count == 0
                        ? "there are no saved queries"
                        : $"no saved query named '{name}'. Saved: {string.Join(", ", ViewModel.SavedQueries.Select(q => q.Name))}");

            if (ViewModel.IsSearching)
                return SearchRunOutcome.Unavailable("a search is already running");

            ViewModel.LoadSavedQuery(query);
            MainPivot.SelectedIndex = 2;
            RecheckAdql();

            // A saved query can go stale: it was written against the schema of the day it was saved.
            if (RefuseIfInvalid(ViewModel.AdqlText) is { } refused) return refused;

            return await ExecuteAndShowAsync(() => ViewModel.ExecuteAdqlCommand.ExecuteAsync(null));
        }, SearchRunOutcome.Unavailable("the Search page could not be reached"));

    // ── Results ─────────────────────────────────────────────────────────────────────────────────

    Task<SearchResultsView> ISearchUiBridge.GetResultsAsync(SearchResultsQuery query)
        => UiDispatch.OnUi(DispatcherQueue, () => CaptureResults(query),
            SearchResultsView.Unavailable("the Search page could not be reached"));

    /// <summary>Every column of the grid that carries data, in grid order. The two action columns are buttons.</summary>
    private List<ResultColumnInfo> DataColumns() =>
        ViewModel.ResultColumns.Where(c => !VirtualColumnKeys.Contains(c.Key)).ToList();

    private ResultColumnInfo? FindColumn(string key) =>
        ViewModel.ResultColumns.FirstOrDefault(c =>
            string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase) && !VirtualColumnKeys.Contains(c.Key));

    private SearchResultsView CaptureResults(SearchResultsQuery query)
    {
        var vm = ViewModel;
        var dataColumns = DataColumns();

        var rowColumns = dataColumns.Select(c => new ResultColumnView(
            c.Key, c.Label, c.Header, c.Visible,
            vm.SelectedUnit(c.Key) ?? ColumnUnitCatalog.DefaultUnitId(c.Key),
            ColumnUnitCatalog.HasMenu(c.Key)
                ? ColumnUnitCatalog.AvailableUnits(c.Key).Select(u => u.Id).ToList()
                : null,
            vm.ActiveColumnFilters.TryGetValue(c.Key, out var f) ? f : null)).ToList();

        // The column vocabulary is answered even with no results: "which columns exist" and "what is on
        // this page" are separate questions, and a caller made to run a query to learn the first asks twice.
        if (vm.Results is null || vm.Results.TotalRows == 0)
            return new SearchResultsView(true, 0, 0, 1, 1, vm.RowsPerPage, vm.RowsPerPageOptions,
                vm.CurrentSort.Key, vm.CurrentSort.Ascending, Array.Empty<string>(), rowColumns, null,
                Array.Empty<IReadOnlyList<string>>(), vm.Results is null ? "no search has been run" : "the query matched no rows");

        var chosen = query.AllColumns ? dataColumns : dataColumns.Where(c => c.Visible).ToList();
        var processed = vm.ProcessedRows;
        var pageSize = query.Limit is > 0 ? query.Limit.Value : vm.RowsPerPage;
        var page = query.Page is > 0 ? query.Page.Value : vm.CurrentPage;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)processed.Count / pageSize));
        if (page > totalPages) page = totalPages;

        var rows = processed
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => (IReadOnlyList<string>)chosen.Select(c => vm.FormatCell(c.Key, r.Get(c.Header))).ToList())
            .ToList();

        return new SearchResultsView(
            Available: true,
            TotalRows: vm.Results.TotalRows,
            FilteredRows: processed.Count,
            Page: page,
            TotalPages: totalPages,
            RowsPerPage: pageSize,
            RowsPerPageOptions: vm.RowsPerPageOptions,
            SortColumn: vm.CurrentSort.Key,
            SortAscending: vm.CurrentSort.Ascending,
            Columns: chosen.Select(c => c.Key).ToList(),
            RowColumns: rowColumns,
            SelectedRow: PrimaryRow,
            Rows: rows,
            // The whole highlighted set, not just the primary: a person can Ctrl- or Shift-click four
            // rows to compare them, and an agent reading one index cannot tell that happened.
            SelectedRows: _selection.Selected);
    }

    Task<SearchResultsViewApplied> ISearchUiBridge.SetResultsViewAsync(SearchResultsViewPatch patch)
        => UiDispatch.OnUi(DispatcherQueue, () => ApplyResultsView(patch),
            SearchResultsViewApplied.Unavailable("the Search page could not be reached"));

    private SearchResultsViewApplied ApplyResultsView(SearchResultsViewPatch patch)
    {
        var vm = ViewModel;

        // ── validate ──
        var unknown = new List<string>();
        void CheckColumns(IEnumerable<string>? keys)
        {
            foreach (var k in keys ?? Enumerable.Empty<string>())
                if (FindColumn(k) is null) unknown.Add(k);
        }
        CheckColumns(patch.Filters?.Keys);
        CheckColumns(patch.Visible?.Keys);
        CheckColumns(patch.Units?.Keys);
        if (patch.SortColumn is { Length: > 0 } sortKey && FindColumn(sortKey) is null) unknown.Add(sortKey);

        if (unknown.Count > 0)
            return new SearchResultsViewApplied(false, Array.Empty<string>(), unknown.Distinct().ToList(),
                Array.Empty<UnitRejection>(), CaptureResults(new SearchResultsQuery()),
                "nothing was applied: no such column");

        var rejectedUnits = new List<UnitRejection>();
        foreach (var (key, unitId) in patch.Units ?? new Dictionary<string, string?>())
        {
            if (unitId is null) continue;   // null restores the column's default
            var column = FindColumn(key)!;
            var accepts = ColumnUnitCatalog.AvailableUnits(column.Key).Select(u => u.Id).ToList();
            if (!accepts.Contains(unitId, StringComparer.OrdinalIgnoreCase))
                rejectedUnits.Add(new UnitRejection(column.Key, unitId, accepts));
        }

        if (rejectedUnits.Count > 0)
            return new SearchResultsViewApplied(false, Array.Empty<string>(), Array.Empty<string>(),
                rejectedUnits, CaptureResults(new SearchResultsQuery()),
                "nothing was applied: a display unit the column does not take");

        if (patch.RowsPerPage is { } rpp && !vm.RowsPerPageOptions.Contains(rpp))
            throw new McpToolException(new InvalidArgument(
                $"rowsPerPage must be one of: {string.Join(", ", vm.RowsPerPageOptions)} — got {rpp}"));

        // ── apply ──
        var changed = new List<string>();
        var rebuildHeader = false;
        var newRows = false;

        if (patch.ResetFilters)
        {
            vm.ResetFiltersAndSort();
            changed.Add("filters");
            changed.Add("sort");
            rebuildHeader = true;
            newRows = true;
        }

        foreach (var (key, text) in patch.Filters ?? new Dictionary<string, string?>())
        {
            var column = FindColumn(key)!;
            vm.SetColumnFilter(column.Key, text ?? string.Empty);
            changed.Add($"filter:{column.Key}");
            rebuildHeader = true;
            newRows = true;
        }

        foreach (var (key, visible) in patch.Visible ?? new Dictionary<string, bool>())
        {
            var column = FindColumn(key)!;
            if (column.Visible == visible) continue;
            vm.ToggleColumnVisibility(column.Key);
            changed.Add($"visible:{column.Key}");
            rebuildHeader = true;
        }

        foreach (var (key, unitId) in patch.Units ?? new Dictionary<string, string?>())
        {
            var column = FindColumn(key)!;
            vm.SetUnit(column.Key, unitId);
            changed.Add($"unit:{column.Key}");
        }

        if (patch.SortColumn is { Length: > 0 } sc)
        {
            vm.SetSort(FindColumn(sc)!.Key, patch.SortAscending ?? true);
            changed.Add("sort");
            newRows = true;
        }
        else if (patch.SortAscending is { } asc && vm.CurrentSort.Key is { } existing)
        {
            vm.SetSort(existing, asc);
            changed.Add("sort");
            newRows = true;
        }

        if (patch.RowsPerPage is { } size && size != vm.RowsPerPage)
        {
            vm.RowsPerPage = size;
            vm.CurrentPage = 1;
            RowsPerPageCombo.SelectedItem = size;
            changed.Add("rowsPerPage");
            newRows = true;
        }

        vm.UpdatePagination();

        if (patch.Page is { } page)
        {
            var target = Math.Clamp(page, 1, Math.Max(1, vm.TotalPages));
            if (target != vm.CurrentPage)
            {
                vm.CurrentPage = target;
                vm.UpdatePagination();
                changed.Add("page");
                newRows = true;
            }
        }

        RenderResultsPage(rebuildHeader, resetScroll: newRows);

        // After the render, because a render with resetScroll drops the selection — the rows under it
        // are not the rows it was chosen from.
        if (patch.SelectRow is { } row)
        {
            if (!SelectRow(row))
                return new SearchResultsViewApplied(true, changed, Array.Empty<string>(), Array.Empty<UnitRejection>(),
                    CaptureResults(new SearchResultsQuery()),
                    $"everything else was applied, but this page has no row {row}");
            changed.Add("selectRow");
        }

        // Show what was changed — but only when there is something to see. Switching to an empty
        // Results tab because someone pre-set a column width is a worse answer than staying put.
        if (ViewModel.Results is { TotalRows: > 0 }) MainPivot.SelectedIndex = 1;

        return new SearchResultsViewApplied(true, changed, Array.Empty<string>(), Array.Empty<UnitRejection>(),
            CaptureResults(new SearchResultsQuery()));
    }

    // ── Export ──────────────────────────────────────────────────────────────────────────────────

    async Task<SearchExportOutcome> ISearchUiBridge.ExportResultsAsync(string format, string path)
    {
        // The text is built on the UI thread (it reads the results the grid holds); the file is written
        // off it, because a multi-megabyte export is exactly the kind of work that stops the window
        // repainting for a second and gets reported as a hang.
        var built = await UiDispatch.OnUi(DispatcherQueue, () =>
        {
            var vm = ViewModel;
            if (vm.Results is null || vm.Results.TotalRows == 0) return (Text: (string?)null, Rows: 0);
            return (Text: format == "tsv" ? vm.ExportResultsTsv() : vm.ExportResultsCsv(), Rows: vm.Results.TotalRows);
        }, (Text: (string?)null, Rows: 0));

        if (built.Text is null)
            return SearchExportOutcome.Unavailable("there are no results to export");

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, built.Text);
        }
        catch (Exception ex)
        {
            return new SearchExportOutcome(false, format, path, 0, 0, $"could not write the file: {ex.Message}");
        }

        return new SearchExportOutcome(true, format, path, built.Rows, new FileInfo(path).Length);
    }

    // ── Detail + history ────────────────────────────────────────────────────────────────────────

    Task<SearchRowDetailOutcome> ISearchUiBridge.ShowRowDetailAsync(int? row)
        => UiDispatch.OnUi(DispatcherQueue, () =>
        {
            var pageRows = ViewModel.GetCurrentPageRows();
            if (pageRows.Count == 0)
                return SearchRowDetailOutcome.Unavailable("there are no results on screen");

            var index = row ?? PrimaryRow;
            if (index is null)
                return SearchRowDetailOutcome.Unavailable(
                    "no row is highlighted — pass `row`, or highlight one with set_search_results_view.selectRow");

            if (index < 0 || index >= pageRows.Count)
                return SearchRowDetailOutcome.Unavailable(
                    $"this page has rows 0-{pageRows.Count - 1}; there is no row {index}");

            var target = pageRows[index.Value];
            var publisherId = target.Get(ViewModel.GetColumnHeader("publisherid"));
            if (string.IsNullOrEmpty(publisherId))
                return new SearchRowDetailOutcome(false, index, null, "that row carries no publisher id to open");

            SelectRow(index.Value);
            ShowRowDetail(target);
            return new SearchRowDetailOutcome(true, index, publisherId);
        }, SearchRowDetailOutcome.Unavailable("the Search page could not be reached"));

    Task<SearchRowDetailOutcome> ISearchUiBridge.ShowObservationDetailAsync(string publisherId)
        => UiDispatch.OnUi(DispatcherQueue, () =>
        {
            ObservationDetailRequested?.Invoke(publisherId);
            return new SearchRowDetailOutcome(true, null, publisherId);
        }, SearchRowDetailOutcome.Unavailable("the Search page could not be reached"));

    Task<SearchRecentRemoved> ISearchUiBridge.RemoveRecentSearchAsync(string match)
        => UiDispatch.OnUi(DispatcherQueue, () =>
        {
            var recents = ViewModel.RecentSearches;
            var found = recents.FirstOrDefault(s => string.Equals(s.Summary, match, StringComparison.Ordinal))
                     ?? recents.FirstOrDefault(s => string.Equals(s.Adql, match, StringComparison.Ordinal))
                     ?? recents.FirstOrDefault(s => string.Equals(s.Summary, match, StringComparison.OrdinalIgnoreCase));

            if (found is null)
                return new SearchRecentRemoved(false, null, recents.Count,
                    recents.Count == 0
                        ? "the recent-searches rail is empty"
                        : $"no recent search matching '{match}' — match on the summary or the exact ADQL");

            ViewModel.RemoveRecentSearch(found);
            return new SearchRecentRemoved(true, found.Summary, ViewModel.RecentSearches.Count);
        }, SearchRecentRemoved.Unavailable("the Search page could not be reached"));

    /// <summary>
    /// The Execute button. Staging is optional here — with no `adql` it runs what is already in the
    /// editor, which is how an agent runs a query the PERSON wrote rather than one it supplied.
    /// </summary>
    Task<SearchAdqlOutcome> ISearchUiBridge.ExecuteAdqlAsync(string? adql)
        => ((ISearchUiBridge)this).SetAdqlAsync(adql ?? ViewModel.AdqlText ?? string.Empty, execute: true);

    // ── Getting back to a known state ───────────────────────────────────────────────────────────

    /// <summary>
    /// Empty the form, the way the Clear button does.
    ///
    /// It goes through the same three steps a click does — the view model, the data-train manager, and
    /// the train lists — because clearing only the view model leaves the facet columns still ticked and
    /// the next search silently constrained by boxes nobody can see checked.
    /// </summary>
    Task<SearchFormApplied> ISearchUiBridge.ResetFormAsync()
        => UiDispatch.OnUi(DispatcherQueue, () =>
        {
            ViewModel.ClearForm();
            _dataTrainMgr.ClearAll();
            if (_dataTrainUIBuilt) SyncAllTrainLists();

            var known = FormSpecs.Select(f => f.Name).ToList();
            return new SearchFormApplied(true, known, Array.Empty<string>(), known, CaptureForm());
        }, SearchFormApplied.Unavailable("the Search page could not be reached"));

    /// <summary>
    /// Put one of the recent searches back in the form — the rail's own button, reachable.
    ///
    /// Matched the same way remove_recent_search matches, so the string an agent read out of
    /// list_recent_searches works in either tool rather than in one of them.
    /// </summary>
    Task<SearchFormApplied> ISearchUiBridge.LoadRecentSearchAsync(string match)
        => UiDispatch.OnUi(DispatcherQueue, () =>
        {
            var recents = ViewModel.RecentSearches;
            var found = recents.FirstOrDefault(s => string.Equals(s.Summary, match, StringComparison.Ordinal))
                     ?? recents.FirstOrDefault(s => string.Equals(s.Adql, match, StringComparison.Ordinal))
                     ?? recents.FirstOrDefault(s => string.Equals(s.Summary, match, StringComparison.OrdinalIgnoreCase));

            var known = FormSpecs.Select(f => f.Name).ToList();
            if (found is null)
                return new SearchFormApplied(false, Array.Empty<string>(), Array.Empty<string>(), known, CaptureForm(),
                    recents.Count == 0
                        ? "there are no recent searches"
                        : $"no recent search matching '{match}' — match on the summary or the exact ADQL");

            ViewModel.LoadFromRecentSearch(found);
            SyncDataTrainToViewModel();

            return new SearchFormApplied(true, known, Array.Empty<string>(), known, CaptureForm());
        }, SearchFormApplied.Unavailable("the Search page could not be reached"));

    /// <summary>Empty the recent-searches rail. Saved queries are a different list and are untouched.</summary>
    Task<SearchRecentRemoved> ISearchUiBridge.ClearRecentSearchesAsync()
        => UiDispatch.OnUi(DispatcherQueue, () =>
        {
            var had = ViewModel.RecentSearches.Count;
            if (had == 0) return new SearchRecentRemoved(false, null, 0, "the recent-searches rail is already empty");

            ViewModel.ClearAllRecentSearches();
            return new SearchRecentRemoved(true, null, 0, $"cleared {had} recent searches");
        }, SearchRecentRemoved.Unavailable("the Search page could not be reached"));
}
