using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure static utility for filtering search result rows by column values.
/// Case-insensitive Contains matching. AND logic across multiple columns.
/// Does not mutate input — returns a new filtered list.
/// </summary>
public static class ResultFilter
{
    public static List<SearchResultRow> Filter(
        IReadOnlyList<SearchResultRow> rows,
        IReadOnlyDictionary<string, string> filters,
        Func<string, string> getHeader)
    {
        if (rows.Count == 0 || filters.Count == 0)
            return rows.ToList();

        // Pre-resolve headers for each filter key
        var resolvedFilters = new List<(string header, string text)>();
        foreach (var (key, text) in filters)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var header = getHeader(key);
            if (!string.IsNullOrEmpty(header))
                resolvedFilters.Add((header, text));
        }

        if (resolvedFilters.Count == 0)
            return rows.ToList();

        return rows.Where(row =>
            resolvedFilters.All(f =>
                row.Get(f.header).Contains(f.text, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
