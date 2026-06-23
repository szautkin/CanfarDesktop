using Windows.Storage;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>
/// Column-unit store persisted in <see cref="ApplicationData.Current"/> LocalSettings (keys prefixed
/// <c>search.col.unit.</c>), so display-unit choices survive app restarts. Falls back to no-op when
/// LocalSettings is unavailable (unpackaged).
/// </summary>
public class LocalSettingsColumnUnitStore : IColumnUnitStore
{
    private const string Prefix = "search.col.unit.";
    private readonly ApplicationDataContainer? _settings;

    public LocalSettingsColumnUnitStore()
    {
        try { _settings = ApplicationData.Current.LocalSettings; }
        catch { _settings = null; }
    }

    public string? GetSelectedUnit(string columnKey)
        => _settings?.Values.TryGetValue(Prefix + CellFormatter.CleanKey(columnKey), out var v) == true ? v as string : null;

    public void SetSelectedUnit(string columnKey, string? unitId)
    {
        if (_settings is null) return;
        var key = Prefix + CellFormatter.CleanKey(columnKey);
        if (string.IsNullOrEmpty(unitId))
        {
            _settings.Values.Remove(key);
            return;
        }
        if (!ColumnUnitCatalog.AvailableUnits(columnKey).Any(c => c.Id == unitId)) return;
        _settings.Values[key] = unitId;
    }

    public void ClearAll()
    {
        if (_settings is null) return;
        foreach (var k in _settings.Values.Keys.Where(k => k.StartsWith(Prefix)).ToList())
            _settings.Values.Remove(k);
    }
}
