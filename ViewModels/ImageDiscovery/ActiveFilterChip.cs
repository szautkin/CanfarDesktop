using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.ViewModels.ImageDiscovery;

/// <summary>
/// One removable chip in the ActiveFiltersBar. <see cref="Category"/> is null for the session-type
/// chip; otherwise it points at the <see cref="PackageQuery"/> set the value lives in (so removal
/// can untick the matching left-pane checkbox).
/// </summary>
public sealed class ActiveFilterChip
{
    public string Id { get; }
    public string CategoryLabel { get; }
    public string Value { get; }
    public PackageQuery.Category? Category { get; }

    public ActiveFilterChip(string id, string categoryLabel, string value, PackageQuery.Category? category)
    {
        Id = id;
        CategoryLabel = categoryLabel;
        Value = value;
        Category = category;
    }
}
