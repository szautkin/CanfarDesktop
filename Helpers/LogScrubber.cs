using System.Text.RegularExpressions;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Redacts secrets (bearer tokens, Authorization headers) from log/diagnostic text
/// before it is written anywhere, so credentials never leak into crash logs.
/// </summary>
public static class LogScrubber
{
    // Whole Authorization header value (to end of line), e.g. "Authorization: Bearer xyz".
    private static readonly Regex AuthHeader = new(
        @"(?<prefix>authorization\s*[:=]\s*).+$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // Standalone "Bearer <token>" occurrences not part of a header.
    private static readonly Regex Bearer = new(
        @"bearer\s+[A-Za-z0-9\-\._~\+/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = AuthHeader.Replace(text, "${prefix}<redacted>");
        text = Bearer.Replace(text, "Bearer <redacted>");
        return text;
    }
}
