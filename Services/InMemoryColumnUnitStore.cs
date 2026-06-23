using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>Dictionary-backed column-unit store (tests, design-time, and the default ctor fallback).</summary>
public class InMemoryColumnUnitStore : IColumnUnitStore
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public string? GetSelectedUnit(string columnKey)
        => _map.TryGetValue(CellFormatter.CleanKey(columnKey), out var v) ? v : null;

    public void SetSelectedUnit(string columnKey, string? unitId)
    {
        var key = CellFormatter.CleanKey(columnKey);
        if (string.IsNullOrEmpty(unitId))
        {
            _map.Remove(key);
            return;
        }
        // Reject a unit the column doesn't offer (stale persisted value, bad input).
        if (!ColumnUnitCatalog.AvailableUnits(columnKey).Any(c => c.Id == unitId)) return;
        _map[key] = unitId;
    }

    public void ClearAll() => _map.Clear();
}
