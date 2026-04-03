using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly ITAPService _tapService;
    private readonly ISearchStoreService _storeService;
    private List<DataTrainRow> _allDataTrainRows = [];
    private CancellationTokenSource? _resolverCts;

    #region Observation

    [ObservableProperty] private string _observationId = string.Empty;
    [ObservableProperty] private string _proposalPi = string.Empty;
    [ObservableProperty] private string _proposalId = string.Empty;
    [ObservableProperty] private string _proposalTitle = string.Empty;
    [ObservableProperty] private string _proposalKeywords = string.Empty;
    [ObservableProperty] private string _intent = string.Empty;
    [ObservableProperty] private bool _publicOnly;

    #endregion

    #region Spatial

    [ObservableProperty] private string _target = string.Empty;
    [ObservableProperty] private string _resolverService = "ALL";
    [ObservableProperty] private string _resolverStatus = string.Empty;
    [ObservableProperty] private double? _resolvedRA;
    [ObservableProperty] private double? _resolvedDec;
    [ObservableProperty] private double _searchRadius = 0.0167;
    [ObservableProperty] private string _pixelScale = string.Empty;
    [ObservableProperty] private string _pixelScaleUnit = "arcsec";
    [ObservableProperty] private bool _spatialCutout;

    #endregion

    #region Temporal

    [ObservableProperty] private string _observationDate = string.Empty;
    [ObservableProperty] private string _datePreset = string.Empty;
    [ObservableProperty] private string _dateStart = string.Empty;
    [ObservableProperty] private string _dateEnd = string.Empty;
    [ObservableProperty] private string _integrationTimeMin = string.Empty;
    [ObservableProperty] private string _integrationTimeMax = string.Empty;
    [ObservableProperty] private string _integrationTimeUnit = "s";
    [ObservableProperty] private string _timeSpan = string.Empty;
    [ObservableProperty] private string _timeSpanUnit = "d";
    [ObservableProperty] private string _dataRelease = string.Empty;

    #endregion

    #region Spectral

    [ObservableProperty] private string _wavelengthMin = string.Empty;
    [ObservableProperty] private string _wavelengthMax = string.Empty;
    [ObservableProperty] private string _spectralCoverage = string.Empty;
    [ObservableProperty] private string _spectralCoverageUnit = "nm";
    [ObservableProperty] private string _spectralSampling = string.Empty;
    [ObservableProperty] private string _spectralSamplingUnit = "nm";
    [ObservableProperty] private string _resolvingPower = string.Empty;
    [ObservableProperty] private string _bandpassWidth = string.Empty;
    [ObservableProperty] private string _bandpassWidthUnit = "nm";
    [ObservableProperty] private string _restFrameEnergy = string.Empty;
    [ObservableProperty] private string _restFrameEnergyUnit = "nm";
    [ObservableProperty] private bool _spectralCutout;

    #endregion

    #region General

    [ObservableProperty] private int _maxRecords = 10000;
    [ObservableProperty] private string _adqlText = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isLoadingDataTrain;
    [ObservableProperty] private bool _isResolving;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private SearchResults? _results;

    // Pagination
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _rowsPerPage = 50;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private string _pageStatus = string.Empty;

    public int[] RowsPerPageOptions { get; } = [25, 50, 100, 250, 500];

    // Column visibility
    public ObservableCollection<ResultColumnInfo> ResultColumns { get; } = [];

    #endregion

    #region Data train collections

    public ObservableCollection<string> AvailableBands { get; } = [];
    public ObservableCollection<string> AvailableCollections { get; } = [];
    public ObservableCollection<string> AvailableInstruments { get; } = [];
    public ObservableCollection<string> AvailableFilters { get; } = [];
    public ObservableCollection<string> AvailableCalLevels { get; } = [];
    public ObservableCollection<string> AvailableDataTypes { get; } = [];
    public ObservableCollection<string> AvailableObsTypes { get; } = [];

    public ObservableCollection<string> SelectedBands { get; } = [];
    public ObservableCollection<string> SelectedCollections { get; } = [];
    public ObservableCollection<string> SelectedInstruments { get; } = [];
    public ObservableCollection<string> SelectedFilters { get; } = [];
    public ObservableCollection<string> SelectedCalLevels { get; } = [];
    public ObservableCollection<string> SelectedDataTypes { get; } = [];
    public ObservableCollection<string> SelectedObsTypes { get; } = [];

    #endregion

    #region Side panel collections

    public ObservableCollection<RecentSearch> RecentSearches { get; } = [];
    public ObservableCollection<SavedQuery> SavedQueries { get; } = [];

    #endregion

    // For ComboBox binding
    public string[] ResolverServices { get; } = ["ALL", "SIMBAD", "NED", "VIZIER", "NONE"];

    public SearchViewModel(ITAPService tapService, ISearchStoreService storeService)
    {
        _tapService = tapService;
        _storeService = storeService;
    }

    #region Data train

    private static readonly string? DataTrainCachePath = GetDataTrainCachePath();

    private static string? GetDataTrainCachePath()
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "datatrain_cache.json");
        }
        catch { return null; }
    }

    public async Task LoadDataTrainAsync()
    {
        IsLoadingDataTrain = true;
        try
        {
            // 1. Try cache first for instant UI
            var cached = await Task.Run(LoadDataTrainFromCache);
            if (cached.Count > 0)
            {
                _allDataTrainRows = cached;
                RefreshDataTrainOptions();
                System.Diagnostics.Debug.WriteLine($"Data train from cache: {cached.Count} rows");
            }

            // 2. Fetch fresh from network in background (update cache for next launch)
            _ = Task.Run(async () =>
            {
                try
                {
                    var fresh = await _tapService.GetDataTrainAsync();
                    if (fresh.Count > 0)
                    {
                        _allDataTrainRows = fresh;
                        SaveDataTrainToCache(fresh);
                        System.Diagnostics.Debug.WriteLine($"Data train cache updated: {fresh.Count} rows");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Data train network fetch failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data train load failed: {ex.Message}");
        }
        finally
        {
            IsLoadingDataTrain = false;
        }
    }

    private static List<DataTrainRow> LoadDataTrainFromCache()
    {
        if (DataTrainCachePath is null || !File.Exists(DataTrainCachePath)) return [];
        try
        {
            var json = File.ReadAllText(DataTrainCachePath);
            return System.Text.Json.JsonSerializer.Deserialize<List<DataTrainRow>>(json) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data train cache read failed: {ex.Message}");
            return [];
        }
    }

    private static void SaveDataTrainToCache(List<DataTrainRow> rows)
    {
        if (DataTrainCachePath is null) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(rows);
            File.WriteAllText(DataTrainCachePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data train cache write failed: {ex.Message}");
        }
    }

    public void RefreshDataTrainOptions()
    {
        System.Diagnostics.Debug.WriteLine($">>> RefreshDataTrainOptions: {_allDataTrainRows.Count} rows, stack={Environment.StackTrace[..200]}");
        var rows = _allDataTrainRows;

        // Bands always show all
        SetOptions(AvailableBands, rows, r => r.Band);

        // Cascade: each step materializes the filtered list for the next
        if (SelectedBands.Count > 0)
        {
            var set = new HashSet<string>(SelectedBands);
            rows = rows.Where(r => set.Contains(r.Band)).ToList();
        }
        SetOptionsAndPrune(AvailableCollections, SelectedCollections, rows, r => r.Collection);

        if (SelectedCollections.Count > 0)
        {
            var set = new HashSet<string>(SelectedCollections);
            rows = rows.Where(r => set.Contains(r.Collection)).ToList();
        }
        SetOptionsAndPrune(AvailableInstruments, SelectedInstruments, rows, r => r.Instrument);

        if (SelectedInstruments.Count > 0)
        {
            var set = new HashSet<string>(SelectedInstruments);
            rows = rows.Where(r => set.Contains(r.Instrument)).ToList();
        }
        SetOptionsAndPrune(AvailableFilters, SelectedFilters, rows, r => r.Filter);

        if (SelectedFilters.Count > 0)
        {
            var set = new HashSet<string>(SelectedFilters);
            rows = rows.Where(r => set.Contains(r.Filter)).ToList();
        }
        SetOptionsAndPrune(AvailableCalLevels, SelectedCalLevels, rows, r => r.CalibrationLevel);

        if (SelectedCalLevels.Count > 0)
        {
            var set = new HashSet<string>(SelectedCalLevels);
            rows = rows.Where(r => set.Contains(r.CalibrationLevel)).ToList();
        }
        SetOptionsAndPrune(AvailableDataTypes, SelectedDataTypes, rows, r => r.DataProductType);

        if (SelectedDataTypes.Count > 0)
        {
            var set = new HashSet<string>(SelectedDataTypes);
            rows = rows.Where(r => set.Contains(r.DataProductType)).ToList();
        }
        SetOptionsAndPrune(AvailableObsTypes, SelectedObsTypes, rows, r => r.ObservationType);
    }

    private static void SetOptions(ObservableCollection<string> available,
        List<DataTrainRow> rows, Func<DataTrainRow, string> selector)
    {
        var values = new SortedSet<string>();
        foreach (var r in rows)
        {
            var v = selector(r);
            if (!string.IsNullOrWhiteSpace(v)) values.Add(v);
        }
        available.Clear();
        foreach (var v in values) available.Add(v);
    }

    private static void SetOptionsAndPrune(ObservableCollection<string> available,
        ObservableCollection<string> selected,
        List<DataTrainRow> rows, Func<DataTrainRow, string> selector)
    {
        var values = new SortedSet<string>();
        foreach (var r in rows)
        {
            var v = selector(r);
            if (!string.IsNullOrWhiteSpace(v)) values.Add(v);
        }
        available.Clear();
        foreach (var v in values) available.Add(v);

        // Prune invalid selections
        for (var i = selected.Count - 1; i >= 0; i--)
        {
            if (!values.Contains(selected[i]))
                selected.RemoveAt(i);
        }
    }

    #endregion

    #region Target resolver

    partial void OnTargetChanged(string value)
    {
        if (ResolverService == "NONE" || string.IsNullOrWhiteSpace(value))
        {
            ResolvedRA = null;
            ResolvedDec = null;
            ResolverStatus = string.Empty;
            return;
        }
        _ = ResolveTargetDebouncedAsync(value);
    }

    private async Task ResolveTargetDebouncedAsync(string target)
    {
        _resolverCts?.Cancel();
        _resolverCts?.Dispose();
        _resolverCts = new CancellationTokenSource();
        var token = _resolverCts.Token;

        try
        {
            await Task.Delay(500, token);
            if (token.IsCancellationRequested) return;

            IsResolving = true;
            ResolverStatus = "Resolving...";
            var result = await _tapService.ResolveTargetAsync(target, ResolverService);

            if (token.IsCancellationRequested) return;

            if (result is not null)
            {
                ResolvedRA = result.RA;
                ResolvedDec = result.Dec;
                ResolverStatus = $"RA: {result.RA:F4}  Dec: {result.Dec:F4}";
                if (result.ObjectType is not null)
                    ResolverStatus += $"  ({result.ObjectType})";
            }
            else
            {
                ResolvedRA = null;
                ResolvedDec = null;
                ResolverStatus = "Not found";
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Resolver error: {ex.Message}");
            ResolverStatus = "Resolver error";
        }
        finally
        {
            IsResolving = false;
        }
    }

    #endregion

    #region Date preset

    partial void OnDatePresetChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var now = DateTime.UtcNow;
        var start = value switch
        {
            "Last24h" => now.AddDays(-1),
            "LastWeek" => now.AddDays(-7),
            "LastMonth" => now.AddMonths(-1),
            _ => (DateTime?)null
        };

        if (start is not null)
            ObservationDate = $"{start.Value:yyyy-MM-dd}..{now:yyyy-MM-dd}";
    }

    #endregion

    #region Search execution

    [RelayCommand]
    public async Task SearchAsync()
    {
        var state = BuildFormState();
        var adql = ADQLBuilder.Build(state);
        AdqlText = adql;
        await ExecuteAdqlAsync(adql);

        if (Results is not null && Results.TotalRows > 0)
        {
            _storeService.SaveRecentSearch(new RecentSearch
            {
                Summary = BuildSearchSummary(state),
                Adql = adql,
                FormState = state,
                ResultCount = Results.TotalRows,
                SearchedAt = DateTime.UtcNow
            });
            LoadRecentSearchesFromStore();
        }
    }

    [RelayCommand]
    public async Task ExecuteAdqlAsync(string? adql = null)
    {
        var query = adql ?? AdqlText;
        if (string.IsNullOrWhiteSpace(query)) return;

        IsSearching = true;
        HasError = false;
        StatusMessage = "Searching...";

        try
        {
            Results = await _tapService.ExecuteQueryAsync(query, MaxRecords);
            BuildColumns();
            CurrentPage = 1;
            UpdatePagination();
            StatusMessage = $"{Results.TotalRows} rows returned" +
                (Results.TotalRows >= MaxRecords ? $" (limit: {MaxRecords})" : "");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
            StatusMessage = "Search failed";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private static string BuildSearchSummary(SearchFormState s)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Target)) parts.Add(s.Target);
        if (!string.IsNullOrWhiteSpace(s.Collections)) parts.Add(s.Collections);
        if (!string.IsNullOrWhiteSpace(s.ObservationDate)) parts.Add(s.ObservationDate);
        else if (!string.IsNullOrWhiteSpace(s.DatePreset)) parts.Add(s.DatePreset);
        if (!string.IsNullOrWhiteSpace(s.SpectralCoverage)) parts.Add($"{s.SpectralCoverage}{s.SpectralCoverageUnit}");
        if (!string.IsNullOrWhiteSpace(s.Bands)) parts.Add(s.Bands);
        return parts.Count > 0 ? string.Join(", ", parts) : "General search";
    }

    #endregion

    #region Pagination + columns

    private void BuildColumns()
    {
        ResultColumns.Clear();
        if (Results is null) return;

        // Virtual columns (not from TAP CSV — rendered as buttons/images)
        ResultColumns.Add(new ResultColumnInfo
        {
            Key = "download", Label = "\u2B73", Header = "__download__",
            Visible = true, Width = CellFormatter.ColumnWidth("download")
        });
        ResultColumns.Add(new ResultColumnInfo
        {
            Key = "preview", Label = "\uD83D\uDDBC", Header = "__preview__",
            Visible = true, Width = CellFormatter.ColumnWidth("preview")
        });

        // Real TAP columns
        foreach (var header in Results.Columns)
        {
            var key = CellFormatter.CleanKey(header);
            ResultColumns.Add(new ResultColumnInfo
            {
                Key = key,
                Label = header.Replace("\"", "").Trim(),
                Header = header,
                Visible = CellFormatter.DefaultVisibleKeys.Contains(key),
                Width = CellFormatter.ColumnWidth(key)
            });
        }
    }

    public void UpdatePagination()
    {
        if (Results is null || Results.TotalRows == 0)
        {
            TotalPages = 1;
            PageStatus = "";
            return;
        }
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)Results.TotalRows / RowsPerPage));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        var start = (CurrentPage - 1) * RowsPerPage + 1;
        var end = Math.Min(CurrentPage * RowsPerPage, Results.TotalRows);
        PageStatus = $"Showing {start}-{end} of {Results.TotalRows}";
    }

    public List<SearchResultRow> GetCurrentPageRows()
    {
        if (Results is null) return [];
        var skip = (CurrentPage - 1) * RowsPerPage;
        return Results.Rows.Skip(skip).Take(RowsPerPage).ToList();
    }

    public void GoToNextPage()
    {
        if (CurrentPage < TotalPages) { CurrentPage++; UpdatePagination(); }
    }

    public void GoToPreviousPage()
    {
        if (CurrentPage > 1) { CurrentPage--; UpdatePagination(); }
    }

    public void GoToFirstPage()
    {
        CurrentPage = 1; UpdatePagination();
    }

    public void GoToLastPage()
    {
        CurrentPage = TotalPages; UpdatePagination();
    }

    public string[] GetVisibleColumnKeys() =>
        ResultColumns.Where(c => c.Visible).Select(c => c.Key).ToArray();

    public string GetColumnLabel(string key) =>
        ResultColumns.FirstOrDefault(c => c.Key == key)?.Label ?? key;

    public string GetColumnHeader(string key) =>
        ResultColumns.FirstOrDefault(c => c.Key == key)?.Header ?? key;

    public int GetColumnWidth(string key) =>
        ResultColumns.FirstOrDefault(c => c.Key == key)?.Width ?? 80;

    public void ToggleColumnVisibility(string key)
    {
        var col = ResultColumns.FirstOrDefault(c => c.Key == key);
        if (col is not null) col.Visible = !col.Visible;
    }

    public string FormatCell(string columnKey, string rawValue) =>
        CellFormatter.Format(columnKey, rawValue);

    #endregion

    #region Recent searches + saved queries

    public void LoadRecentSearchesFromStore()
    {
        RecentSearches.Clear();
        foreach (var s in _storeService.LoadRecentSearches())
            RecentSearches.Add(s);
    }

    public void LoadSavedQueriesFromStore()
    {
        SavedQueries.Clear();
        foreach (var q in _storeService.LoadSavedQueries())
            SavedQueries.Add(q);
    }

    public void LoadFromRecentSearch(RecentSearch search)
    {
        LoadFromFormState(search.FormState);
        AdqlText = search.Adql;
    }

    public void RemoveRecentSearch(RecentSearch search)
    {
        RecentSearches.Remove(search);
        // Re-save the remaining list
        _storeService.ClearRecentSearches();
        foreach (var s in RecentSearches)
            _storeService.SaveRecentSearch(s);
    }

    public void ClearAllRecentSearches()
    {
        _storeService.ClearRecentSearches();
        RecentSearches.Clear();
    }

    public void SaveCurrentQuery(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(AdqlText)) return;
        var query = new SavedQuery { Name = name, Adql = AdqlText, SavedAt = DateTime.UtcNow };
        _storeService.SaveQuery(query);
        LoadSavedQueriesFromStore();
    }

    public void LoadSavedQuery(SavedQuery query)
    {
        AdqlText = query.Adql;
    }

    public void DeleteSavedQuery(SavedQuery query)
    {
        _storeService.DeleteQuery(query.Name);
        LoadSavedQueriesFromStore();
    }

    #endregion

    #region Form state

    public SearchFormState BuildFormState() => new()
    {
        ObservationId = ObservationId,
        ProposalPi = ProposalPi,
        ProposalId = ProposalId,
        ProposalTitle = ProposalTitle,
        ProposalKeywords = ProposalKeywords,
        Intent = Intent,
        PublicOnly = PublicOnly,
        Target = Target,
        ResolverService = ResolverService,
        ResolvedRA = ResolvedRA,
        ResolvedDec = ResolvedDec,
        SearchRadius = SearchRadius,
        PixelScale = PixelScale,
        PixelScaleUnit = PixelScaleUnit,
        SpatialCutout = SpatialCutout,
        ObservationDate = ObservationDate,
        DatePreset = DatePreset,
        DateStart = DateStart,
        DateEnd = DateEnd,
        IntegrationTimeMin = IntegrationTimeMin,
        IntegrationTimeMax = IntegrationTimeMax,
        IntegrationTimeUnit = IntegrationTimeUnit,
        TimeSpan = TimeSpan,
        TimeSpanUnit = TimeSpanUnit,
        DataRelease = DataRelease,
        WavelengthMin = WavelengthMin,
        WavelengthMax = WavelengthMax,
        SpectralCoverage = SpectralCoverage,
        SpectralCoverageUnit = SpectralCoverageUnit,
        SpectralSampling = SpectralSampling,
        SpectralSamplingUnit = SpectralSamplingUnit,
        ResolvingPower = ResolvingPower,
        BandpassWidth = BandpassWidth,
        BandpassWidthUnit = BandpassWidthUnit,
        RestFrameEnergy = RestFrameEnergy,
        RestFrameEnergyUnit = RestFrameEnergyUnit,
        SpectralCutout = SpectralCutout,
        Bands = string.Join(",", SelectedBands),
        Collections = string.Join(",", SelectedCollections),
        Instruments = string.Join(",", SelectedInstruments),
        Filters = string.Join(",", SelectedFilters),
        CalibrationLevels = string.Join(",", SelectedCalLevels),
        DataProductTypes = string.Join(",", SelectedDataTypes),
        ObservationTypes = string.Join(",", SelectedObsTypes),
        MaxRecords = MaxRecords
    };

    public void LoadFromFormState(SearchFormState s)
    {
        ObservationId = s.ObservationId;
        ProposalPi = s.ProposalPi;
        ProposalId = s.ProposalId;
        ProposalTitle = s.ProposalTitle;
        ProposalKeywords = s.ProposalKeywords;
        Intent = s.Intent;
        PublicOnly = s.PublicOnly;
        Target = s.Target;
        ResolverService = s.ResolverService;
        ResolvedRA = s.ResolvedRA;
        ResolvedDec = s.ResolvedDec;
        SearchRadius = s.SearchRadius;
        PixelScale = s.PixelScale;
        PixelScaleUnit = s.PixelScaleUnit;
        SpatialCutout = s.SpatialCutout;
        ObservationDate = s.ObservationDate;
        DatePreset = s.DatePreset;
        DateStart = s.DateStart;
        DateEnd = s.DateEnd;
        IntegrationTimeMin = s.IntegrationTimeMin;
        IntegrationTimeMax = s.IntegrationTimeMax;
        IntegrationTimeUnit = s.IntegrationTimeUnit;
        TimeSpan = s.TimeSpan;
        TimeSpanUnit = s.TimeSpanUnit;
        DataRelease = s.DataRelease;
        WavelengthMin = s.WavelengthMin;
        WavelengthMax = s.WavelengthMax;
        SpectralCoverage = s.SpectralCoverage;
        SpectralCoverageUnit = s.SpectralCoverageUnit;
        SpectralSampling = s.SpectralSampling;
        SpectralSamplingUnit = s.SpectralSamplingUnit;
        ResolvingPower = s.ResolvingPower;
        BandpassWidth = s.BandpassWidth;
        BandpassWidthUnit = s.BandpassWidthUnit;
        RestFrameEnergy = s.RestFrameEnergy;
        RestFrameEnergyUnit = s.RestFrameEnergyUnit;
        SpectralCutout = s.SpectralCutout;
        MaxRecords = s.MaxRecords;
    }

    public void ClearForm()
    {
        ObservationId = ProposalPi = ProposalId = ProposalTitle = ProposalKeywords = string.Empty;
        Intent = string.Empty;
        PublicOnly = false;
        Target = string.Empty;
        ResolverService = "ALL";
        ResolvedRA = ResolvedDec = null;
        SearchRadius = 0.0167;
        PixelScale = string.Empty;
        PixelScaleUnit = "arcsec";
        SpatialCutout = false;
        ObservationDate = DatePreset = DateStart = DateEnd = string.Empty;
        IntegrationTimeMin = IntegrationTimeMax = string.Empty;
        IntegrationTimeUnit = "s";
        TimeSpan = string.Empty;
        TimeSpanUnit = "d";
        DataRelease = string.Empty;
        WavelengthMin = WavelengthMax = string.Empty;
        SpectralCoverage = SpectralSampling = ResolvingPower = BandpassWidth = RestFrameEnergy = string.Empty;
        SpectralCoverageUnit = SpectralSamplingUnit = BandpassWidthUnit = RestFrameEnergyUnit = "nm";
        SpectralCutout = false;
        SelectedBands.Clear();
        SelectedCollections.Clear();
        SelectedInstruments.Clear();
        SelectedFilters.Clear();
        SelectedCalLevels.Clear();
        SelectedDataTypes.Clear();
        SelectedObsTypes.Clear();
        RefreshDataTrainOptions();
        ResolverStatus = string.Empty;
    }

    #endregion

    #region Export

    public string ExportResultsCsv()
    {
        if (Results is null || Results.Rows.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", Results.Columns.Select(QuoteCsv)));
        foreach (var row in Results.Rows)
            sb.AppendLine(string.Join(",", Results.Columns.Select(c => QuoteCsv(row.Get(c)))));
        return sb.ToString();
    }

    public string ExportResultsTsv()
    {
        if (Results is null || Results.Rows.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join("\t", Results.Columns));
        foreach (var row in Results.Rows)
            sb.AppendLine(string.Join("\t", Results.Columns.Select(c => row.Get(c).Replace("\t", " "))));
        return sb.ToString();
    }

    private static string QuoteCsv(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;

    #endregion
}
