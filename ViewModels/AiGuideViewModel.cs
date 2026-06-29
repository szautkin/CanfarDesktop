using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.ViewModels;

/// <summary>
/// One editable built-in-tool row: its default description, the effective (possibly overridden) one,
/// and the in-progress edit state for the inline accordion editor. WinUI-free so the logic is testable.
/// </summary>
public partial class AiGuideToolRowViewModel : ObservableObject
{
    public string Name { get; }
    public string DefaultDescription { get; }
    public string CategoryId { get; }
    public string CategoryTitle { get; }

    [ObservableProperty] private string _effectiveDescription;
    [ObservableProperty] private bool _isOverridden;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _editText = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public int CharCount => (EditText ?? string.Empty).Trim().Length;
    public string CharCountText => $"{CharCount}/{AiGuideService.MaxDescriptionChars}";
    public bool IsOverLimit => CharCount > AiGuideService.MaxDescriptionChars;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public AiGuideToolRowViewModel(string name, string defaultDescription, string effectiveDescription,
        bool isOverridden, string categoryId, string categoryTitle)
    {
        Name = name;
        DefaultDescription = defaultDescription;
        _effectiveDescription = effectiveDescription;
        _isOverridden = isOverridden;
        CategoryId = categoryId;
        CategoryTitle = categoryTitle;
    }

    partial void OnEditTextChanged(string value)
    {
        OnPropertyChanged(nameof(CharCount));
        OnPropertyChanged(nameof(CharCountText));
        OnPropertyChanged(nameof(IsOverLimit));
    }

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value) return;
        // Opening the editor pre-fills with the current override (blank when there isn't one — the
        // built-in default is shown read-only above it).
        EditText = IsOverridden ? EffectiveDescription : string.Empty;
        ErrorMessage = string.Empty;
    }
}

/// <summary>One user guide-tool row for the "My Guides" list.</summary>
public sealed class AiGuideGuideRowViewModel
{
    public Guid Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string? Body { get; }
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public string BodyInfo => HasBody ? $"Returns {Body!.Trim().Length} characters of instructions" : string.Empty;

    public AiGuideGuideRowViewModel(Guid id, string name, string description, string? body)
    {
        Id = id;
        Name = name;
        Description = description;
        Body = body;
    }
}

/// <summary>A category: its metadata, its tool rows, and override-count state. Rendered as a launchpad
/// tile, a full "everything" card, and the focus-panel body — all over the same instance.</summary>
public partial class AiGuideCategoryGroup : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Glyph { get; }
    public string Summary { get; }
    public ObservableCollection<AiGuideToolRowViewModel> Tools { get; } = new();

    [ObservableProperty] private int _overriddenCount;
    [ObservableProperty] private bool _hasOverrides;

    public int ToolCount => Tools.Count;
    public string ToolCountText => $"{Tools.Count} {(Tools.Count == 1 ? "tool" : "tools")}";
    public string OverriddenBadgeText => $"{OverriddenCount} overridden";

    public AiGuideCategoryGroup(string id, string title, string glyph, string summary)
    {
        Id = id;
        Title = title;
        Glyph = glyph;
        Summary = summary;
    }

    public void RecalcOverridden()
    {
        OverriddenCount = Tools.Count(t => t.IsOverridden);
        HasOverrides = OverriddenCount > 0;
        OnPropertyChanged(nameof(OverriddenBadgeText));
    }
}

/// <summary>
/// Backs the AI Guide dashboard. Mirrors the macOS AIGuideView: a tiles launchpad (default) of category
/// tiles that open a centered focus panel, a "See everything" full grid, a flat search view, and the
/// "My Guides" list — all over one set of row instances so an edit reflects everywhere. Pure logic over
/// <see cref="AiGuideService"/> + a tool-input provider, so it's unit-testable without WinUI.
/// </summary>
public partial class AiGuideViewModel : ObservableObject
{
    private readonly AiGuideService _service;
    private readonly Func<IReadOnlyList<AiGuideToolInput>> _toolInputs;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _showTiles = true; // launchpad is the default mode (1:1 with macOS)
    [ObservableProperty] private bool _isFocusOpen;
    [ObservableProperty] private AiGuideCategoryGroup? _focusedCategory;
    [ObservableProperty] private int _toolCount;
    [ObservableProperty] private int _overriddenCount;
    [ObservableProperty] private bool _hasGuides;
    [ObservableProperty] private bool _hasCategories;

    public ObservableCollection<AiGuideCategoryGroup> Categories { get; } = new();
    public ObservableCollection<AiGuideGuideRowViewModel> Guides { get; } = new();
    public ObservableCollection<AiGuideToolRowViewModel> SearchResults { get; } = new();

    /// <summary>Paired with <see cref="ShowTiles"/> for the two-radio segmented toggle.</summary>
    public bool ShowEverything
    {
        get => !ShowTiles;
        set => ShowTiles = !value;
    }

    public int CategoryCount => Categories.Count;
    public string ToolsChipText => $"{ToolCount} tools";
    public string OverriddenChipText => $"{OverriddenCount} overridden";
    public string CategoriesChipText => $"{CategoryCount} categories";
    public bool HasOverriddenStat => OverriddenCount > 0;
    public string MatchCountText => IsSearching
        ? $"{SearchResults.Count} of {ToolCount} tools match “{(SearchText ?? string.Empty).Trim()}”"
        : string.Empty;

    public AiGuideViewModel(AiGuideService service, Func<IReadOnlyList<AiGuideToolInput>> toolInputs)
    {
        _service = service;
        _toolInputs = toolInputs;
    }

    partial void OnShowTilesChanged(bool value) => OnPropertyChanged(nameof(ShowEverything));

    /// <summary>(Re)build the category cards + guide list from the live tool surface and stored state.</summary>
    public void Load()
    {
        var rows = _service.RowsForTools(_toolInputs());
        var byCat = rows
            .GroupBy(r => r.Category, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Name, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        Categories.Clear();
        foreach (var cat in AiGuideCatalog.AllCategories) // ordered; Other last
        {
            if (!byCat.TryGetValue(cat.Id, out var catRows) || catRows.Count == 0) continue;
            var group = new AiGuideCategoryGroup(cat.Id, cat.Title, cat.Glyph, cat.Summary);
            foreach (var r in catRows)
                group.Tools.Add(new AiGuideToolRowViewModel(r.Name, r.DefaultDescription, r.EffectiveDescription, r.IsOverridden, cat.Id, cat.Title));
            group.RecalcOverridden();
            Categories.Add(group);
        }

        ToolCount = rows.Count;
        OverriddenCount = rows.Count(r => r.IsOverridden);
        HasCategories = Categories.Count > 0;
        NotifyHeader();
        LoadGuides();
        ApplyFilter();
    }

    public void LoadGuides()
    {
        Guides.Clear();
        foreach (var g in _service.Snapshot().Guides)
            Guides.Add(new AiGuideGuideRowViewModel(g.Id, g.Name, g.Description, g.Body));
        HasGuides = Guides.Count > 0;
    }

    /// <summary>Persist the row's edited description as an override. Sets <c>ErrorMessage</c> on failure.</summary>
    public void SaveOverride(AiGuideToolRowViewModel row)
    {
        try
        {
            _service.SetOverride(row.Name, row.EditText ?? string.Empty);
            row.ErrorMessage = string.Empty;
            row.EffectiveDescription = _service.EffectiveDescription(row.Name, row.DefaultDescription);
            row.IsOverridden = _service.IsOverridden(row.Name);
            row.IsExpanded = false;
            RecalcStats();
        }
        catch (AiGuideValidationException ex)
        {
            row.ErrorMessage = ex.Message;
        }
    }

    /// <summary>Reset the row to its built-in description (clear the override).</summary>
    public void ResetOverride(AiGuideToolRowViewModel row)
    {
        _service.ClearOverride(row.Name);
        row.ErrorMessage = string.Empty;
        row.EditText = string.Empty;
        row.EffectiveDescription = row.DefaultDescription;
        row.IsOverridden = false;
        row.IsExpanded = false;
        RecalcStats();
    }

    public void DeleteGuide(Guid id)
    {
        _service.DeleteGuide(id);
        LoadGuides();
    }

    /// <summary>Open a category's focus panel (the centered overlay).</summary>
    public void OpenCategory(AiGuideCategoryGroup group)
    {
        FocusedCategory = group;
        IsFocusOpen = true;
    }

    /// <summary>Close the focus panel, collapsing any in-progress inline edit first.</summary>
    public void CloseFocus()
    {
        if (FocusedCategory is { } g)
            foreach (var t in g.Tools) t.IsExpanded = false;
        FocusedCategory = null;
        IsFocusOpen = false;
    }

    public void ClearSearch() => SearchText = string.Empty;

    private void RecalcStats()
    {
        OverriddenCount = Categories.SelectMany(c => c.Tools).Count(t => t.IsOverridden);
        foreach (var c in Categories) c.RecalcOverridden();
        NotifyHeader();
    }

    private void NotifyHeader()
    {
        OnPropertyChanged(nameof(CategoryCount));
        OnPropertyChanged(nameof(ToolsChipText));
        OnPropertyChanged(nameof(OverriddenChipText));
        OnPropertyChanged(nameof(CategoriesChipText));
        OnPropertyChanged(nameof(HasOverriddenStat));
        OnPropertyChanged(nameof(MatchCountText));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = (SearchText ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            IsSearching = false;
            SearchResults.Clear();
            OnPropertyChanged(nameof(MatchCountText));
            return;
        }

        if (IsFocusOpen) CloseFocus(); // search supersedes the overlay (macOS parity)
        IsSearching = true;

        var matches = Categories.SelectMany(c => c.Tools)
            .Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || t.EffectiveDescription.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        SearchResults.Clear();
        foreach (var m in matches) SearchResults.Add(m);
        OnPropertyChanged(nameof(MatchCountText));
    }
}
