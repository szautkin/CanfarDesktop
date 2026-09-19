namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>
/// The Search page's agent-facing surface: one method per <c>search_*</c> UI tool.
///
/// Implemented by the live page (<c>SearchPage.Mcp.cs</c>, which marshals every call to the UI
/// thread) and by test doubles. The tools themselves take a single delegate each — they never see
/// this interface — so a tool can be exercised with a lambda and nothing else.
///
/// Every method answers with an outcome record rather than throwing: "the Search page is not open"
/// and "that column does not exist" are answers an agent can act on, and a thrown exception across
/// the UI-thread boundary is not.
/// </summary>
public interface ISearchUiBridge
{
    Task<SearchFormView> GetFormAsync();
    Task<SearchFormApplied> SetFormAsync(SearchFormPatch patch);
    Task<SearchConstraintsView> GetConstraintsAsync();
    Task<SearchConstraintsApplied> SetConstraintsAsync(SearchConstraintsPatch patch);
    Task<SearchRunOutcome> RunSearchAsync();
    Task<SearchAdqlOutcome> SetAdqlAsync(string adql, bool execute);

    /// <summary>
    /// Run the editor's ADQL — the Execute button. Null <paramref name="adql"/> means "whatever is
    /// already in the editor", which is the case <c>set_adql_query</c> cannot express: it always
    /// stages text, and an agent that wants to run what a PERSON typed has nothing to stage.
    /// </summary>
    Task<SearchAdqlOutcome> ExecuteAdqlAsync(string? adql);
    Task<SearchResultsView> GetResultsAsync(SearchResultsQuery query);
    Task<SearchResultsViewApplied> SetResultsViewAsync(SearchResultsViewPatch patch);
    Task<SearchExportOutcome> ExportResultsAsync(string format, string path);
    Task<SearchRowDetailOutcome> ShowRowDetailAsync(int? row);
    Task<SearchRowDetailOutcome> ShowObservationDetailAsync(string publisherId);
    Task<SearchRunOutcome> RunSavedQueryAsync(string name);
    Task<SearchRecentRemoved> RemoveRecentSearchAsync(string match);
    Task<SearchFormApplied> ResetFormAsync();
    Task<SearchFormApplied> LoadRecentSearchAsync(string match);
    Task<SearchRecentRemoved> ClearRecentSearchesAsync();
}

// ── Form ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One field of the search form. <paramref name="Unit"/> and <paramref name="UnitOptions"/> are set
/// only for the fields that carry their own unit (a coverage in nm beside a sampling in GHz is
/// routine, so the unit belongs to the field, not to the form).
/// </summary>
public sealed record FormFieldView(string Name, string Value, string? Unit = null, IReadOnlyList<string>? UnitOptions = null);

/// <summary>The whole form as an agent sees it: the fields, the resolved sky position, and the ADQL the form would run.</summary>
public sealed record SearchFormView(
    bool Available,
    IReadOnlyList<FormFieldView> Fields,
    double? ResolvedRa,
    double? ResolvedDec,
    string ResolverService,
    string ResolverStatus,
    int MaxRecords,
    string Adql,
    string ActiveTab,
    string? Message = null)
{
    public static SearchFormView Unavailable(string message) => new(
        false, Array.Empty<FormFieldView>(), null, null, "ALL", string.Empty, 0, string.Empty, "unknown", message);
}

/// <summary>Field name → new value. A null value clears the field.</summary>
public sealed record SearchFormPatch(IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// The outcome of a <c>set_search_form</c>. When <paramref name="Unknown"/> is non-empty NOTHING was
/// applied: a patch naming a field that does not exist is a mistake to report, not a subset to
/// silently honour. <paramref name="KnownFields"/> is the vocabulary the caller should have used.
/// </summary>
public sealed record SearchFormApplied(
    bool Applied,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> KnownFields,
    SearchFormView Form,
    string? Message = null)
{
    public static SearchFormApplied Unavailable(string message) => new(
        false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        SearchFormView.Unavailable(message), message);
}

// ── Constraints (the faceted data train) ────────────────────────────────────────────────────────

/// <summary>One facet column: everything it currently offers, and what is selected in it.</summary>
public sealed record FacetView(string Name, IReadOnlyList<string> Available, IReadOnlyList<string> Selected);

public sealed record SearchConstraintsView(bool Available, IReadOnlyList<FacetView> Facets, string? Message = null)
{
    public static SearchConstraintsView Unavailable(string message) => new(false, Array.Empty<FacetView>(), message);
}

/// <summary>Facet name → the full selection for it (replaces, not merges).</summary>
public sealed record SearchConstraintsPatch(IReadOnlyDictionary<string, IReadOnlyList<string>> Facets);

/// <summary>
/// The outcome of a <c>set_search_constraints</c>. As with the form, an unknown facet or a value the
/// facet does not offer applies nothing — the facets CASCADE, so a partially-applied selection is a
/// state the caller cannot reason about.
/// </summary>
public sealed record SearchConstraintsApplied(
    bool Applied,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<FacetValueRejection> Rejected,
    IReadOnlyList<string> KnownFacets,
    SearchConstraintsView Constraints,
    string? Message = null)
{
    public static SearchConstraintsApplied Unavailable(string message) => new(
        false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FacetValueRejection>(),
        Array.Empty<string>(), SearchConstraintsView.Unavailable(message), message);
}

/// <summary>A value a facet does not offer, with the values it does (capped by the caller).</summary>
public sealed record FacetValueRejection(string Facet, string Value, IReadOnlyList<string> DidYouMean);

// ── Running ─────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The outcome of running a query. <paramref name="Truncated"/> means the row cap was reached and
/// MORE rows match — the sample is incomplete, which is not something to discover later.
/// </summary>
public sealed record SearchRunOutcome(
    bool Ran,
    string Adql,
    int TotalRows,
    bool Truncated,
    int MaxRecords,
    double ElapsedMs,
    string? Error = null,
    string? Message = null)
{
    public static SearchRunOutcome Unavailable(string message) => new(
        false, string.Empty, 0, false, 0, 0, null, message);
}

public sealed record SearchAdqlOutcome(
    bool Applied,
    string Adql,
    bool Executed,
    SearchRunOutcome? Run = null,
    string? Message = null)
{
    public static SearchAdqlOutcome Unavailable(string message) => new(false, string.Empty, false, null, message);
}

// ── Results ─────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// What to read back. <paramref name="AllColumns"/> false returns only the columns the grid is
/// showing; true returns every column the query selected — without it a caller sees the dozen on
/// screen and cannot know the other twenty-nine exist.
/// </summary>
public sealed record SearchResultsQuery(bool AllColumns = false, int? Page = null, int? Limit = null);

/// <summary>One column of the results grid, whether or not it is currently shown.</summary>
public sealed record ResultColumnView(
    string Key,
    string Label,
    string Header,
    bool Visible,
    string? Unit = null,
    IReadOnlyList<string>? UnitOptions = null,
    string? Filter = null);

/// <summary>
/// The results grid as an agent sees it. <paramref name="RowColumns"/> is answered even when
/// <paramref name="Rows"/> is empty — "which columns exist" and "what is on this page" are separate
/// questions, and a caller that has to run a query to learn the vocabulary asks twice.
/// </summary>
public sealed record SearchResultsView(
    bool Available,
    int TotalRows,
    int FilteredRows,
    int Page,
    int TotalPages,
    int RowsPerPage,
    IReadOnlyList<int> RowsPerPageOptions,
    string? SortColumn,
    bool SortAscending,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ResultColumnView> RowColumns,
    int? SelectedRow,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Message = null,
    IReadOnlyList<int>? SelectedRows = null)
{
    public static SearchResultsView Unavailable(string message) => new(
        false, 0, 0, 1, 1, 0, Array.Empty<int>(), null, true,
        Array.Empty<string>(), Array.Empty<ResultColumnView>(), null,
        Array.Empty<IReadOnlyList<string>>(), message);
}

/// <summary>
/// A change to how the results are shown. Every member is optional; only what is set is touched.
/// Column keys match case-insensitively — the keys are derived from CSV headers, and an agent that
/// read "Cal. Lev." cannot be expected to reproduce the cleaned key exactly.
/// </summary>
public sealed record SearchResultsViewPatch(
    int? Page = null,
    int? RowsPerPage = null,
    string? SortColumn = null,
    bool? SortAscending = null,
    IReadOnlyDictionary<string, string?>? Filters = null,
    IReadOnlyDictionary<string, bool>? Visible = null,
    IReadOnlyDictionary<string, string?>? Units = null,
    int? SelectRow = null,
    bool ResetFilters = false);

/// <summary>A display unit a column does not take, and the ones it does.</summary>
public sealed record UnitRejection(string Column, string Requested, IReadOnlyList<string> Accepts);

public sealed record SearchResultsViewApplied(
    bool Applied,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> UnknownColumns,
    IReadOnlyList<UnitRejection> RejectedUnits,
    SearchResultsView View,
    string? Message = null)
{
    public static SearchResultsViewApplied Unavailable(string message) => new(
        false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<UnitRejection>(),
        SearchResultsView.Unavailable(message), message);
}

// ── Export, detail, history ─────────────────────────────────────────────────────────────────────

public sealed record SearchExportOutcome(
    bool Exported, string Format, string Path, int Rows, long Bytes, string? Message = null)
{
    public static SearchExportOutcome Unavailable(string message) => new(false, string.Empty, string.Empty, 0, 0, message);
}

public sealed record SearchRowDetailOutcome(bool Opened, int? Row, string? PublisherId, string? Message = null)
{
    public static SearchRowDetailOutcome Unavailable(string message) => new(false, null, null, message);
}

public sealed record SearchRecentRemoved(bool Removed, string? Summary, int Remaining, string? Message = null)
{
    public static SearchRecentRemoved Unavailable(string message) => new(false, null, 0, message);
}
