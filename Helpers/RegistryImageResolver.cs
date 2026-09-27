namespace CanfarDesktop.Helpers;

/// <summary>
/// Resolves a possibly-short image name to a full registry reference using the configured registry host
/// and repository/project. A bare name (no <c>/</c>) is treated as short and prefixed with the host (and
/// the repo/project when set); a name that already contains a <c>/</c> is assumed already-qualified and
/// returned unchanged. Lets the Image Discovery + AI Compute settings accept short image names instead of
/// the full <c>host/project/name:tag</c> every time. Pure — unit-testable.
/// </summary>
public static class RegistryImageResolver
{
    public static string Resolve(string? image, string? host, string? repository)
    {
        var img = Normalize(image);
        if (img.Length == 0) return string.Empty;
        if (img.Contains('/')) return img; // already host/project-qualified — leave it alone

        var h = Normalize(host).TrimEnd('/');
        if (h.Length == 0) return img; // no host to prefix with

        var repo = (repository ?? string.Empty).Trim().Trim('/');
        return repo.Length == 0 ? $"{h}/{img}" : $"{h}/{repo}/{img}";
    }

    /// <summary>
    /// An image reference or registry host as typed or pasted, made one: without whitespace or a web
    /// scheme, neither of which a reference can have and both of which a copy from a browser brings.
    /// "https:// images.canfar.net/…" was kept as typed, reported by get_compute_state as a URI that was
    /// not one, and refused by the platform at launch (QA D6).
    /// </summary>
    public static string Normalize(string? value)
    {
        var text = string.Concat((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        return scheme >= 0 ? text[(scheme + 3)..] : text;
    }
}
