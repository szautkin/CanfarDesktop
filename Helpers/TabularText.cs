namespace CanfarDesktop.Helpers;

/// <summary>
/// Rows as tab-separated text, with a header line — what a spreadsheet, a text editor and TOPCAT all
/// take when pasted. A value's own tabs and line breaks would split its row, so they become spaces.
/// </summary>
public static class TabularText
{
    public static string Of(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        => string.Join(Environment.NewLine,
            new[] { Line(headers) }.Concat(rows.Select(Line)));

    private static string Line(IReadOnlyList<string> values)
        => string.Join('\t', values.Select(v => (v ?? string.Empty).Replace('\t', ' ').Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')));
}
