using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.ViewModels.ImageDiscovery;

/// <summary>
/// One checkbox in the left filter pane: a single value in a category. <see cref="IsSelected"/> is
/// two-way bound to the CheckBox and pushes into the parent's <see cref="PackageQuery"/> via the
/// toggle callback; <see cref="IsEnabled"/> is the faceting flag (greyed when ticking it would yield
/// zero results); <see cref="IsVisible"/> is the left-pane search filter.
/// </summary>
public partial class FacetValueViewModel : ObservableObject
{
    public string Value { get; }
    public PackageQuery.Category Category { get; }

    private readonly Action<FacetValueViewModel, bool> _onToggled;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isVisible = true;

    public FacetValueViewModel(string value, PackageQuery.Category category, bool isSelected,
        Action<FacetValueViewModel, bool> onToggled)
    {
        Value = value;
        Category = category;
        _isSelected = isSelected; // field init: does NOT fire the toggle callback
        _onToggled = onToggled;
    }

    partial void OnIsSelectedChanged(bool value) => _onToggled(this, value);
}

/// <summary>
/// One collapsible section in the left filter pane (OS family / Python / System (apt / dpkg) / …),
/// holding its checkbox values. The badge mirrors the macOS "(count)" shown in the section header,
/// which reflects the search-filtered count.
/// </summary>
public partial class FacetSectionViewModel : ObservableObject
{
    public string Title { get; }
    public PackageQuery.Category Category { get; }
    public ObservableCollection<FacetValueViewModel> Values { get; } = new();

    [ObservableProperty] private int _countBadge;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _hasVisibleValues = true;

    public FacetSectionViewModel(string title, PackageQuery.Category category)
    {
        Title = title;
        Category = category;
    }
}
