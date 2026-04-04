using System.Globalization;
using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure static utility for sorting search result rows.
/// Smart comparison: tries numeric first, falls back to string.
/// Does not mutate input — returns a new sorted list.
/// </summary>
public static class ResultSorter
{
    public static List<SearchResultRow> Sort(
        IReadOnlyList<SearchResultRow> rows,
        string columnHeader,
        bool ascending)
    {
        if (rows.Count == 0 || string.IsNullOrEmpty(columnHeader))
            return rows.ToList();

        var sorted = rows.ToList();
        sorted.Sort((a, b) =>
        {
            var va = a.Get(columnHeader);
            var vb = b.Get(columnHeader);
            var cmp = SmartCompare(va, vb);
            return ascending ? cmp : -cmp;
        });
        return sorted;
    }

    /// <summary>
    /// Compare two values: numeric if both parse, else string (case-insensitive).
    /// Empty strings sort last.
    /// </summary>
    internal static int SmartCompare(string a, string b)
    {
        var aEmpty = string.IsNullOrWhiteSpace(a);
        var bEmpty = string.IsNullOrWhiteSpace(b);

        if (aEmpty && bEmpty) return 0;
        if (aEmpty) return 1;  // empty sorts last
        if (bEmpty) return -1;

        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
            return da.CompareTo(db);

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
