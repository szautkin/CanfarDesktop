using System.Text;
using System.Text.RegularExpressions;

namespace CanfarDesktop.Helpers;

/// <summary>
/// A failed HTTP response as an exception that says why — the status, and the service's own words
/// when it sent any.
///
/// <para>EnsureSuccessStatusCode says only "Response status code does not indicate success: 400".
/// CADC says a great deal more in the body — "UsageFault: CIRCLE does not intersect the data",
/// "PermissionDenied" — and that is the part a person can act on. The status bar shows a download's
/// failure message, so this is what reaches it.</para>
/// </summary>
public static partial class HttpFailure
{
    /// <summary>At most this much of a body is read: an error page can be large, and a line is enough.</summary>
    private const int MaxBodyBytes = 8192;

    /// <summary>At most this much of it is kept.</summary>
    public const int MaxDetail = 300;

    public static async Task<HttpRequestException> FromAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        string? detail = null;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[MaxBodyBytes];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await body.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            detail = Clean(Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch
        {
            // No body to be had: the status is still worth saying.
        }

        var status = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})";
        return new HttpRequestException(detail is { Length: > 0 } ? $"{status}: {detail}" : status, null, response.StatusCode);
    }

    /// <summary>A body reduced to one readable line: tags out, whitespace folded, trimmed to <see cref="MaxDetail"/>.</summary>
    public static string? Clean(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        var text = Tags().Replace(body, " ");
        text = Spaces().Replace(System.Net.WebUtility.HtmlDecode(text), " ").Trim();
        if (text.Length == 0) return null;
        return text.Length <= MaxDetail ? text : text[..(MaxDetail - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
