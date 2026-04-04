namespace CanfarDesktop.Helpers.Notebook;

using System.Text.RegularExpressions;
using Windows.UI;

/// <summary>
/// Simple regex-based Python syntax highlighter. Produces colored spans
/// for display in a RichTextBlock. Not a full parser — covers keywords,
/// strings, comments, numbers, decorators, and built-in functions.
/// </summary>
public static partial class PythonSyntaxHighlighter
{
    // Token types ordered by priority (first match wins)
    [GeneratedRegex(@"#[^\n]*")]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"(""""""[\s\S]*?""""""|'''[\s\S]*?'''|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')")]
    private static partial Regex StringRegex();

    [GeneratedRegex(@"@\w+")]
    private static partial Regex DecoratorRegex();

    [GeneratedRegex(@"\b(?:False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield)\b")]
    private static partial Regex KeywordRegex();

    [GeneratedRegex(@"\b(?:print|len|range|int|str|float|list|dict|set|tuple|type|isinstance|hasattr|getattr|setattr|open|super|property|staticmethod|classmethod|enumerate|zip|map|filter|sorted|reversed|abs|min|max|sum|round|input|format|repr|id|hex|oct|bin|chr|ord|any|all|next|iter)\b")]
    private static partial Regex BuiltinRegex();

    [GeneratedRegex(@"\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b")]
    private static partial Regex NumberRegex();

    public static List<SyntaxSpan> Highlight(string code)
    {
        if (string.IsNullOrEmpty(code))
            return [new SyntaxSpan { Text = "", Type = TokenType.Plain }];

        // Build a list of (start, end, type) for all tokens
        var tokens = new List<(int Start, int End, TokenType Type)>();

        AddMatches(tokens, CommentRegex(), code, TokenType.Comment);
        AddMatches(tokens, StringRegex(), code, TokenType.String);
        AddMatches(tokens, DecoratorRegex(), code, TokenType.Decorator);
        AddMatches(tokens, KeywordRegex(), code, TokenType.Keyword);
        AddMatches(tokens, BuiltinRegex(), code, TokenType.Builtin);
        AddMatches(tokens, NumberRegex(), code, TokenType.Number);

        // Sort by start position, remove overlaps (first match wins by priority)
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        var filtered = new List<(int Start, int End, TokenType Type)>();
        var lastEnd = 0;
        foreach (var token in tokens)
        {
            if (token.Start < lastEnd) continue; // overlaps with previous token
            filtered.Add(token);
            lastEnd = token.End;
        }

        // Build spans filling gaps with Plain text
        var spans = new List<SyntaxSpan>();
        var pos = 0;
        foreach (var (start, end, type) in filtered)
        {
            if (start > pos)
                spans.Add(new SyntaxSpan { Text = code[pos..start], Type = TokenType.Plain });
            spans.Add(new SyntaxSpan { Text = code[start..end], Type = type });
            pos = end;
        }
        if (pos < code.Length)
            spans.Add(new SyntaxSpan { Text = code[pos..], Type = TokenType.Plain });

        return spans;
    }

    private static void AddMatches(List<(int, int, TokenType)> tokens, Regex regex, string code, TokenType type)
    {
        foreach (Match m in regex.Matches(code))
            tokens.Add((m.Index, m.Index + m.Length, type));
    }

    /// <summary>
    /// Get the color for a token type. Light theme colors (VS Code inspired).
    /// </summary>
    public static Color GetColor(TokenType type, bool isDark = false) => type switch
    {
        TokenType.Keyword => isDark ? Color.FromArgb(255, 86, 156, 214) : Color.FromArgb(255, 0, 0, 255),
        TokenType.String => isDark ? Color.FromArgb(255, 206, 145, 120) : Color.FromArgb(255, 163, 21, 21),
        TokenType.Comment => isDark ? Color.FromArgb(255, 106, 153, 85) : Color.FromArgb(255, 0, 128, 0),
        TokenType.Number => isDark ? Color.FromArgb(255, 181, 206, 168) : Color.FromArgb(255, 9, 134, 88),
        TokenType.Builtin => isDark ? Color.FromArgb(255, 220, 220, 170) : Color.FromArgb(255, 121, 94, 38),
        TokenType.Decorator => isDark ? Color.FromArgb(255, 220, 220, 170) : Color.FromArgb(255, 121, 94, 38),
        _ => isDark ? Color.FromArgb(255, 212, 212, 212) : Color.FromArgb(255, 26, 26, 26),
    };
}

public enum TokenType
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Builtin,
    Decorator,
}

public class SyntaxSpan
{
    public required string Text { get; init; }
    public required TokenType Type { get; init; }
}
