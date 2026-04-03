using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure logic: manages data train rows, cascade filtering, and selected values.
/// No UI dependencies — fully testable.
/// </summary>
public class DataTrainManager
{
    private List<DataTrainRow> _rows = [];

    // All distinct values per column (computed from _rows)
    public List<string> AllBands { get; private set; } = [];
    public List<string> AllCollections { get; private set; } = [];
    public List<string> AllInstruments { get; private set; } = [];
    public List<string> AllFilters { get; private set; } = [];
    public List<string> AllCalLevels { get; private set; } = [];
    public List<string> AllDataTypes { get; private set; } = [];
    public List<string> AllObsTypes { get; private set; } = [];

    // Available (filtered by cascade) per column
    public HashSet<string> AvailableBands { get; private set; } = [];
    public HashSet<string> AvailableCollections { get; private set; } = [];
    public HashSet<string> AvailableInstruments { get; private set; } = [];
    public HashSet<string> AvailableFilters { get; private set; } = [];
    public HashSet<string> AvailableCalLevels { get; private set; } = [];
    public HashSet<string> AvailableDataTypes { get; private set; } = [];
    public HashSet<string> AvailableObsTypes { get; private set; } = [];

    // User selections
    public HashSet<string> SelectedBands { get; } = [];
    public HashSet<string> SelectedCollections { get; } = [];
    public HashSet<string> SelectedInstruments { get; } = [];
    public HashSet<string> SelectedFilters { get; } = [];
    public HashSet<string> SelectedCalLevels { get; } = [];
    public HashSet<string> SelectedDataTypes { get; } = [];
    public HashSet<string> SelectedObsTypes { get; } = [];

    public int RowCount => _rows.Count;
    public bool IsLoaded => _rows.Count > 0;

    public void Load(List<DataTrainRow> rows)
    {
        _rows = rows;
        AllBands = Distinct(rows, r => r.Band);
        AllCollections = Distinct(rows, r => r.Collection);
        AllInstruments = Distinct(rows, r => r.Instrument);
        AllFilters = Distinct(rows, r => r.Filter);
        AllCalLevels = Distinct(rows, r => r.CalibrationLevel);
        AllDataTypes = Distinct(rows, r => r.DataProductType);
        AllObsTypes = Distinct(rows, r => r.ObservationType);
        Refresh();
    }

    public void Toggle(int columnIndex, string value)
    {
        var set = GetSelected(columnIndex);
        if (!set.Remove(value)) set.Add(value);
        // Clear downstream selections
        for (var i = columnIndex + 1; i < 7; i++)
            GetSelected(i).Clear();
        Refresh();
    }

    public void ClearAll()
    {
        SelectedBands.Clear();
        SelectedCollections.Clear();
        SelectedInstruments.Clear();
        SelectedFilters.Clear();
        SelectedCalLevels.Clear();
        SelectedDataTypes.Clear();
        SelectedObsTypes.Clear();
        Refresh();
    }

    /// <summary>
    /// Cascade filter: each column's available set is determined by upstream selections.
    /// Prunes invalid downstream selections.
    /// </summary>
    public void Refresh()
    {
        AvailableBands = new HashSet<string>(AllBands);
        var rows = _rows;

        rows = Filter(rows, SelectedBands, r => r.Band);
        AvailableCollections = DistinctSet(rows, r => r.Collection);
        Prune(SelectedCollections, AvailableCollections);

        rows = Filter(rows, SelectedCollections, r => r.Collection);
        AvailableInstruments = DistinctSet(rows, r => r.Instrument);
        Prune(SelectedInstruments, AvailableInstruments);

        rows = Filter(rows, SelectedInstruments, r => r.Instrument);
        AvailableFilters = DistinctSet(rows, r => r.Filter);
        Prune(SelectedFilters, AvailableFilters);

        rows = Filter(rows, SelectedFilters, r => r.Filter);
        AvailableCalLevels = DistinctSet(rows, r => r.CalibrationLevel);
        Prune(SelectedCalLevels, AvailableCalLevels);

        rows = Filter(rows, SelectedCalLevels, r => r.CalibrationLevel);
        AvailableDataTypes = DistinctSet(rows, r => r.DataProductType);
        Prune(SelectedDataTypes, AvailableDataTypes);

        rows = Filter(rows, SelectedDataTypes, r => r.DataProductType);
        AvailableObsTypes = DistinctSet(rows, r => r.ObservationType);
        Prune(SelectedObsTypes, AvailableObsTypes);
    }

    /// <summary>Build comma-separated selection strings for ADQL builder.</summary>
    public string BandsString => Join(SelectedBands);
    public string CollectionsString => Join(SelectedCollections);
    public string InstrumentsString => Join(SelectedInstruments);
    public string FiltersString => Join(SelectedFilters);
    public string CalLevelsString => Join(SelectedCalLevels);
    public string DataTypesString => Join(SelectedDataTypes);
    public string ObsTypesString => Join(SelectedObsTypes);

    private HashSet<string> GetSelected(int index) => index switch
    {
        0 => SelectedBands,
        1 => SelectedCollections,
        2 => SelectedInstruments,
        3 => SelectedFilters,
        4 => SelectedCalLevels,
        5 => SelectedDataTypes,
        6 => SelectedObsTypes,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static List<string> Distinct(List<DataTrainRow> rows, Func<DataTrainRow, string> selector)
    {
        var set = new SortedSet<string>();
        foreach (var r in rows)
        {
            var v = selector(r);
            if (!string.IsNullOrWhiteSpace(v)) set.Add(v);
        }
        return [.. set];
    }

    private static HashSet<string> DistinctSet(List<DataTrainRow> rows, Func<DataTrainRow, string> selector)
    {
        var set = new HashSet<string>();
        foreach (var r in rows)
        {
            var v = selector(r);
            if (!string.IsNullOrWhiteSpace(v)) set.Add(v);
        }
        return set;
    }

    private static List<DataTrainRow> Filter(List<DataTrainRow> rows, HashSet<string> selected, Func<DataTrainRow, string> selector)
    {
        if (selected.Count == 0) return rows;
        return rows.Where(r => selected.Contains(selector(r))).ToList();
    }

    private static void Prune(HashSet<string> selected, HashSet<string> available)
    {
        selected.IntersectWith(available);
    }

    private static string Join(HashSet<string> set) =>
        set.Count > 0 ? string.Join(",", set) : string.Empty;
}
