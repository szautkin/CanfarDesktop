namespace CanfarDesktop.Helpers.Notebook;

using System.Text.RegularExpressions;
using Windows.UI;

/// <summary>
/// Pure static parser for ANSI escape codes in text.
/// Extracts spans with foreground color and bold state.
/// Follows the ResultSorter pattern: static, deterministic, testable.
/// </summary>
public static partial class AnsiParser
{
    [GeneratedRegex(@"\x1b\[([0-9;]*)m")]
    private static partial Regex AnsiEscapeRegex();

    private static readonly Color[] StandardColors =
    [
        Color.FromArgb(255, 0, 0, 0),       // 0 Black
        Color.FromArgb(255, 205, 49, 49),    // 1 Red
        Color.FromArgb(255, 13, 188, 121),   // 2 Green
        Color.FromArgb(255, 229, 229, 16),   // 3 Yellow
        Color.FromArgb(255, 36, 114, 200),   // 4 Blue
        Color.FromArgb(255, 188, 63, 188),   // 5 Magenta
        Color.FromArgb(255, 17, 168, 205),   // 6 Cyan
        Color.FromArgb(255, 229, 229, 229),  // 7 White
    ];

    public static List<AnsiSpan> Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var spans = new List<AnsiSpan>();
        Color? currentFg = null;
        var currentBold = false;
        var lastIndex = 0;

        foreach (Match match in AnsiEscapeRegex().Matches(text))
        {
            if (match.Index > lastIndex)
            {
                spans.Add(new AnsiSpan
                {
                    Text = text[lastIndex..match.Index],
                    Foreground = currentFg,
                    IsBold = currentBold
                });
            }

            var codes = match.Groups[1].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .ToArray();

            if (codes.Length == 0) codes = [0];

            foreach (var code in codes)
            {
                switch (code)
                {
                    case 0: currentFg = null; currentBold = false; break;
                    case 1: currentBold = true; break;
                    case >= 30 and <= 37: currentFg = StandardColors[code - 30]; break;
                    case 39: currentFg = null; break;
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            spans.Add(new AnsiSpan
            {
                Text = text[lastIndex..],
                Foreground = currentFg,
                IsBold = currentBold
            });
        }

        return spans;
    }

    /// <summary>
    /// Strip all ANSI escape codes, returning plain text.
    /// </summary>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return AnsiEscapeRegex().Replace(text, "");
    }
}

public class AnsiSpan
{
    public required string Text { get; init; }
    public Color? Foreground { get; init; }
    public bool IsBold { get; init; }
}
