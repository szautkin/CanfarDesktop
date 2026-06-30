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
        var img = (image ?? string.Empty).Trim();
        if (img.Length == 0) return string.Empty;
        if (img.Contains('/')) return img; // already host/project-qualified — leave it alone

        var h = (host ?? string.Empty).Trim().TrimEnd('/');
        if (h.Length == 0) return img; // no host to prefix with

        var repo = (repository ?? string.Empty).Trim().Trim('/');
        return repo.Length == 0 ? $"{h}/{img}" : $"{h}/{repo}/{img}";
    }
}
