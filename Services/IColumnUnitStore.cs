namespace CanfarDesktop.Services;

/// <summary>
/// Persists the user's per-column display-unit choices for the search results grid (e.g. RA shown as
/// H:M:S vs degrees, wavelength as nm vs GHz). Null means "use the column default". Mirrors the macOS
/// <c>ColumnUnitStore</c> protocol.
/// </summary>
public interface IColumnUnitStore
{
    string? GetSelectedUnit(string columnKey);
    void SetSelectedUnit(string columnKey, string? unitId);
    void ClearAll();
}
