namespace CanfarDesktop.Helpers;

/// <summary>Builds safe SQLite FTS5 MATCH expressions from free-text user input.</summary>
public static class FtsQuery
{
    /// <summary>
    /// Turn user input into a prefix MATCH expression where each whitespace token becomes a
    /// quoted prefix term (e.g. <c>galaxy m3</c> → <c>"galaxy"* "m3"*</c>). Quoting (and doubling
    /// internal quotes) neutralizes FTS5 operators so user input cannot inject query syntax.
    /// Tokens with no letters/digits are dropped (they would form an empty, erroring phrase).
    /// Returns an empty string when there is nothing to match on.
    /// </summary>
    public static string BuildPrefix(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var terms = new List<string>();
        foreach (var token in input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.Any(char.IsLetterOrDigit)) continue;
            var escaped = token.Replace("\"", "\"\"");
            terms.Add($"\"{escaped}\"*");
        }
        return string.Join(" ", terms);
    }
}
