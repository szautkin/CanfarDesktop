using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.ViewModels.ImageDiscovery;

/// <summary>
/// Drives the find-by-package dialog: builds the faceted left-pane checkbox sections from the cache,
/// maintains the live <see cref="PackageQuery"/> as the user toggles checkboxes, refacets (greys out
/// values that would yield zero results), rebuilds the active-filter chips, and recomputes the
/// grouped right-pane image list (query → image-search → type-filter → group-by-project). 1-to-1 with
/// the macOS <c>ImageDiscoveryModel</c>. WinUI-free so the whole interaction model is unit-testable.
/// </summary>
public partial class ImageDiscoveryViewModel : ObservableObject
{
    private readonly ImageDiscoveryCoordinator _coordinator;

    // Pinned section order + titles, matching macOS PackageFilterPane exactly.
    private static readonly (PackageQuery.Category Category, string Title)[] SectionOrder =
    {
        (PackageQuery.Category.OsFamily, "OS family"),
        (PackageQuery.Category.OsVersion, "OS version"),
        (PackageQuery.Category.Python, "Python"),
        (PackageQuery.Category.R, "R"),
        (PackageQuery.Category.Dpkg, "System (apt / dpkg)"),
        (PackageQuery.Category.Rpm, "System (rpm)"),
        (PackageQuery.Category.Apk, "System (apk)"),
        (PackageQuery.Category.Capabilities, "Capabilities"),
    };

    private readonly List<ParsedImage> _allImages = new();
    private readonly Dictionary<string, ImageRowViewModel> _rowsById = new();
    private AllPackages _allPackages = new();
    private IReadOnlyList<ImageManifest> _discovered = Array.Empty<ImageManifest>();
    private bool _applying; // guards programmatic checkbox updates against the toggle callback

    public PackageQuery Query { get; private set; } = new();

    public ObservableCollection<FacetSectionViewModel> FilterSections { get; } = new();

    /// <summary>
    /// Flattened section-headers + visible value rows for a single VIRTUALIZED left-pane ListView
    /// (a non-virtualized CheckBox-per-row panel would leak on huge dpkg/rpm lists — the data-train
    /// rule). Headers are <see cref="FacetSectionViewModel"/>, rows are <see cref="FacetValueViewModel"/>.
    /// </summary>
    public ObservableCollection<object> FilterItems { get; } = new();

    public ObservableCollection<ActiveFilterChip> ActiveChips { get; } = new();
    public ObservableCollection<ImageRowGroup> FilteredGroups { get; } = new();
    public ObservableCollection<string> SessionTypeOptions { get; } = new();

    [ObservableProperty] private string _packageSearchText = string.Empty;
    [ObservableProperty] private string _imageSearchText = string.Empty;
    [ObservableProperty] private string _selectedSessionType = AllTypesOption; // "All" sentinel for the ComboBox
    [ObservableProperty] private ImageRowViewModel? _selectedRow;

    /// <summary>The session-type filter, or null when "All" is selected.</summary>
    private string? EffectiveType => SelectedSessionType == AllTypesOption ? null : SelectedSessionType;
    [ObservableProperty] private bool _isDiscoveryRunning;
    [ObservableProperty] private string _discoveredSubtitle = string.Empty;
    [ObservableProperty] private bool _hasActiveFilters;
    [ObservableProperty] private bool _hasFailures;
    [ObservableProperty] private bool _isEmptyResult;
    [ObservableProperty] private ManifestDetailViewModel? _detail;

    public bool HasSelection => SelectedRow is not null;
    partial void OnSelectedRowChanged(ImageRowViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    public bool IsShowingDetail => Detail is not null;
    partial void OnDetailChanged(ManifestDetailViewModel? value) => OnPropertyChanged(nameof(IsShowingDetail));

    /// <summary>Open the inline detail panel for a discovered/failed row (manifest or failure view).</summary>
    private void ShowDetail(ImageRowViewModel? row)
    {
        if (row is null || row.State.Kind is RowStateKind.NeverDiscovered or RowStateKind.Running) return;
        Detail = new ManifestDetailViewModel(row, _coordinator, () => Detail = null);
    }

    public const string AllTypesOption = "All";

    public ImageDiscoveryViewModel(ImageDiscoveryCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>Hydrate from the catalogue + cache. Does NOT auto-probe (discovery is opt-in per row).</summary>
    public void Load(IReadOnlyList<RawImage> catalogue)
    {
        _allImages.Clear();
        _rowsById.Clear();
        foreach (var raw in catalogue)
        {
            var parsed = ImageParser.Parse(raw);
            _allImages.Add(parsed);
            _rowsById[parsed.Id] = new ImageRowViewModel(
                parsed,
                RowState.FromOutcome(_coordinator.Outcome(parsed.Id)),
                discover: (row, force) => RunDiscoveryAsync(row, force),
                dismiss: DismissError,
                details: ShowDetail);
        }

        SessionTypeOptions.Clear();
        SessionTypeOptions.Add(AllTypesOption);
        foreach (var t in FacetEngine.OrderedTypes(_allImages.SelectMany(i => i.Types)))
            SessionTypeOptions.Add(t);

        RefreshFromCache();
    }

    /// <summary>Re-read manifests/outcomes from the cache (after probes) and rebuild everything.</summary>
    public void RefreshFromCache()
    {
        _allPackages = _coordinator.AllPackages();
        _discovered = _coordinator.DiscoveredManifests();
        foreach (var (id, row) in _rowsById)
            if (row.State.Kind != RowStateKind.Running)
                row.State = RowState.FromOutcome(_coordinator.Outcome(id));

        BuildSections();
        AfterFilterChanged();
    }

    // ── Section building ─────────────────────────────────────────────────────

    private void BuildSections()
    {
        FilterSections.Clear();
        foreach (var (category, title) in SectionOrder)
        {
            var values = CandidateValues(category);
            if (values.Count == 0) continue;

            var section = new FacetSectionViewModel(title, category);
            foreach (var value in values)
                section.Values.Add(new FacetValueViewModel(value, category, SetFor(category).Contains(value), OnFacetToggled));
            section.CountBadge = section.Values.Count;
            FilterSections.Add(section);
        }
        ApplyPackageSearch();
    }

    /// <summary>The full candidate value list for a category (OS version scoped to selected families).</summary>
    private IReadOnlyList<string> CandidateValues(PackageQuery.Category category) => category switch
    {
        PackageQuery.Category.OsFamily => Sorted(_allPackages.OsFamilies),
        PackageQuery.Category.OsVersion => Sorted(OsVersionCandidates()),
        PackageQuery.Category.Python => Sorted(_allPackages.Python),
        PackageQuery.Category.R => Sorted(_allPackages.R),
        PackageQuery.Category.Dpkg => Sorted(_allPackages.Dpkg),
        PackageQuery.Category.Rpm => Sorted(_allPackages.Rpm),
        PackageQuery.Category.Apk => Sorted(_allPackages.Apk),
        PackageQuery.Category.Capabilities => Sorted(_discovered.SelectMany(m => m.Capabilities)),
        _ => Array.Empty<string>(),
    };

    // OS versions are scoped to the selected families (or all families when none selected), matching
    // macOS osVersionEntries — keeps the list relevant instead of showing every distro's versions.
    private IEnumerable<string> OsVersionCandidates()
    {
        IEnumerable<string> families = Query.OsFamilies.Count == 0
            ? (IEnumerable<string>)_allPackages.OsVersionsByFamily.Keys
            : Query.OsFamilies;
        return families.SelectMany(f => _allPackages.OsVersionsByFamily.TryGetValue(f, out var v) ? v : Enumerable.Empty<string>());
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values)
        => values.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

    // ── Toggle / query mutation ──────────────────────────────────────────────

    private void OnFacetToggled(FacetValueViewModel value, bool selected)
    {
        if (_applying) return;
        var set = SetFor(value.Category);
        if (selected) set.Add(value.Value); else set.Remove(value.Value);

        // Selecting/clearing an OS family changes which OS versions are relevant — rebuild that list.
        if (value.Category == PackageQuery.Category.OsFamily)
            RebuildOsVersionSection();

        AfterFilterChanged();
    }

    private void RebuildOsVersionSection()
    {
        var section = FilterSections.FirstOrDefault(s => s.Category == PackageQuery.Category.OsVersion);
        var values = CandidateValues(PackageQuery.Category.OsVersion);

        if (section is null && values.Count > 0)
        {
            // Insert in pinned position (right after OS family).
            section = new FacetSectionViewModel("OS version", PackageQuery.Category.OsVersion);
            FilterSections.Insert(IndexOfSection(PackageQuery.Category.OsFamily) + 1, section);
        }
        if (section is null) return; // none existed and none to show

        section.Values.Clear();
        foreach (var v in values)
            section.Values.Add(new FacetValueViewModel(v, PackageQuery.Category.OsVersion, Query.OsVersions.Contains(v), OnFacetToggled));
        section.CountBadge = section.Values.Count;
        if (values.Count == 0) FilterSections.Remove(section);

        ApplyPackageSearch(); // sets IsVisible on the new values + reflattens FilterItems
    }

    private int IndexOfSection(PackageQuery.Category category)
    {
        for (var i = 0; i < FilterSections.Count; i++)
            if (FilterSections[i].Category == category) return i;
        return FilterSections.Count - 1;
    }

    private HashSet<string> SetFor(PackageQuery.Category c) => c switch
    {
        PackageQuery.Category.OsFamily => Query.OsFamilies,
        PackageQuery.Category.OsVersion => Query.OsVersions,
        PackageQuery.Category.Python => Query.Python,
        PackageQuery.Category.R => Query.R,
        PackageQuery.Category.Dpkg => Query.Dpkg,
        PackageQuery.Category.Rpm => Query.Rpm,
        PackageQuery.Category.Apk => Query.Apk,
        PackageQuery.Category.Capabilities => Query.Capabilities,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    // ── Recompute pipeline ───────────────────────────────────────────────────

    private void AfterFilterChanged()
    {
        Refacet();
        RebuildChips();
        RecomputeFiltered();
        HasActiveFilters = !Query.IsEmpty || EffectiveType is not null;
        HasFailures = _rowsById.Values.Any(r => r.State.Kind == RowStateKind.Failed);
        DiscoveredSubtitle =
            $"Discovered {_rowsById.Values.Count(r => r.State.Kind == RowStateKind.Discovered)} of {_allImages.Count} images";
    }

    /// <summary>Grey out values that would collapse the results to empty (unless already ticked).</summary>
    private void Refacet()
    {
        foreach (var section in FilterSections)
        {
            var available = FacetEngine.AvailableValues(Query, section.Category, _discovered);
            foreach (var value in section.Values)
                value.IsEnabled = available.Contains(value.Value) || value.IsSelected;
        }
    }

    private void RebuildChips()
    {
        ActiveChips.Clear();
        if (EffectiveType is { } type)
            ActiveChips.Add(new ActiveFilterChip($"type:{type}", "Type", type, null) { RemoveCommand = RemoveChipCommand });

        AddChips(PackageQuery.Category.OsFamily, "OS family", Query.OsFamilies);
        AddChips(PackageQuery.Category.OsVersion, "OS version", Query.OsVersions);
        AddChips(PackageQuery.Category.Python, "Python", Query.Python);
        AddChips(PackageQuery.Category.R, "R", Query.R);
        AddChips(PackageQuery.Category.Dpkg, "apt / dpkg", Query.Dpkg);
        AddChips(PackageQuery.Category.Rpm, "rpm", Query.Rpm);
        AddChips(PackageQuery.Category.Apk, "apk", Query.Apk);
        AddChips(PackageQuery.Category.Capabilities, "Capability", Query.Capabilities);
    }

    private void AddChips(PackageQuery.Category category, string label, IEnumerable<string> values)
    {
        foreach (var v in values.OrderBy(x => x, StringComparer.Ordinal))
            ActiveChips.Add(new ActiveFilterChip($"{category}:{v}", label, v, category) { RemoveCommand = RemoveChipCommand });
    }

    private void RecomputeFiltered()
    {
        var search = ImageSearchText.Trim();
        var matched = new List<ParsedImage>();
        foreach (var img in _allImages)
        {
            var state = _rowsById[img.Id].State;
            var include = state.Kind == RowStateKind.Discovered
                ? (Query.IsEmpty || (state.Manifest is { } m && Query.Matches(m)))
                : Query.IsEmpty;
            if (!include) continue;

            if (search.Length > 0
                && img.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && img.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            // Cache-only images (no declared types) are never hidden by the type filter.
            if (EffectiveType is { } type && img.Types.Length > 0 && !img.Types.Contains(type))
                continue;

            matched.Add(img);
        }

        FilteredGroups.Clear();
        foreach (var (project, images) in ImageParser.GroupByProject(matched))
            FilteredGroups.Add(new ImageRowGroup(project, images.Select(i => _rowsById[i.Id])));

        IsEmptyResult = matched.Count == 0;
    }

    // ── Left-pane package search ─────────────────────────────────────────────

    partial void OnPackageSearchTextChanged(string value) => ApplyPackageSearch();

    private void ApplyPackageSearch()
    {
        var needle = PackageSearchText.Trim();
        foreach (var section in FilterSections)
        {
            var visible = 0;
            foreach (var v in section.Values)
            {
                v.IsVisible = needle.Length == 0
                    || v.Value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                if (v.IsVisible) visible++;
            }
            section.CountBadge = visible;
            section.HasVisibleValues = visible > 0;
        }
        RebuildFilterItems();
    }

    /// <summary>Reflatten <see cref="FilterSections"/> (visible rows only) into <see cref="FilterItems"/>.</summary>
    private void RebuildFilterItems()
    {
        FilterItems.Clear();
        foreach (var section in FilterSections)
        {
            var visible = section.Values.Where(v => v.IsVisible).ToList();
            if (visible.Count == 0) continue;
            FilterItems.Add(section);
            foreach (var v in visible) FilterItems.Add(v);
        }
    }

    partial void OnImageSearchTextChanged(string value) => RecomputeFiltered();

    partial void OnSelectedSessionTypeChanged(string value)
    {
        if (_applying) return;
        RebuildChips();
        RecomputeFiltered();
        HasActiveFilters = !Query.IsEmpty || EffectiveType is not null;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearAllFilters()
    {
        _applying = true;
        Query = new PackageQuery();
        foreach (var section in FilterSections)
            foreach (var v in section.Values)
                v.IsSelected = false;
        SelectedSessionType = AllTypesOption;
        _applying = false;

        RebuildOsVersionSection();
        AfterFilterChanged();
    }

    [RelayCommand]
    private void RemoveChip(ActiveFilterChip? chip)
    {
        if (chip is null) return;
        if (chip.Category is null) // session type
        {
            SelectedSessionType = AllTypesOption; // fires OnSelectedSessionTypeChanged → recompute
            return;
        }

        SetFor(chip.Category.Value).Remove(chip.Value);
        // Untick the matching checkbox silently.
        _applying = true;
        var section = FilterSections.FirstOrDefault(s => s.Category == chip.Category.Value);
        var value = section?.Values.FirstOrDefault(v => v.Value == chip.Value);
        if (value is not null) value.IsSelected = false;
        _applying = false;

        if (chip.Category.Value == PackageQuery.Category.OsFamily)
            RebuildOsVersionSection();
        AfterFilterChanged();
    }

    // ── Per-row discovery (opt-in probing; invoked from the row VMs' commands) ─

    private async Task RunDiscoveryAsync(ImageRowViewModel? row, bool force)
    {
        if (row is null) return;
        row.State = RowState.Running();
        IsDiscoveryRunning = true;
        AfterFilterChanged();
        try
        {
            await _coordinator.DiscoverAsync(row.Id, force);
        }
        catch
        {
            // Failure is persisted in the cache; the row picks it up below.
        }
        finally
        {
            row.State = RowState.FromOutcome(_coordinator.Outcome(row.Id));
            _allPackages = _coordinator.AllPackages();
            _discovered = _coordinator.DiscoveredManifests();
            BuildSections();           // a fresh probe may have surfaced new packages
            IsDiscoveryRunning = false;
            AfterFilterChanged();
        }
    }

    private void DismissError(ImageRowViewModel? row)
    {
        if (row is null) return;
        _coordinator.Invalidate(row.Id);
        row.State = RowState.NeverDiscovered;
        AfterFilterChanged();
    }

    [RelayCommand]
    private void ClearAllFailures()
    {
        foreach (var row in _rowsById.Values.Where(r => r.State.Kind == RowStateKind.Failed).ToList())
        {
            _coordinator.Invalidate(row.Id);
            row.State = RowState.NeverDiscovered;
        }
        AfterFilterChanged();
    }
}
