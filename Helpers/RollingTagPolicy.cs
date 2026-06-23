using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Detects "rolling" image tags whose content drifts in place (<c>:latest</c>, <c>:dev</c>, …)
/// and applies a 24h freshness window so stale cache entries can be flagged. Versioned tags
/// (<c>:24.07</c>, <c>:1.1.2</c>) are effectively immutable and never go stale by this policy.
/// </summary>
public static class RollingTagPolicy
{
    public static readonly TimeSpan StalenessWindow = TimeSpan.FromHours(24);

    private static readonly HashSet<string> RollingSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "latest", "dev", "nightly", "main", "edge", "staging", "unstable",
    };

    public static bool IsRollingTag(string imageID)
    {
        var colon = imageID.LastIndexOf(':');
        if (colon < 0) return false;
        var tag = imageID[(colon + 1)..];
        return RollingSuffixes.Contains(tag);
    }

    public static bool IsStale(ImageManifest manifest, DateTimeOffset now)
        => IsRollingTag(manifest.ImageID) && (now - manifest.CapturedAt) > StalenessWindow;

    /// <summary>"Discovered 3 days ago — rediscover for fresh data", or null for fresh manifests.</summary>
    public static string? StaleAgeLabel(ImageManifest manifest, DateTimeOffset now)
    {
        if (!IsStale(manifest, now)) return null;
        var elapsed = now - manifest.CapturedAt;
        string phrase;
        if (elapsed.TotalDays >= 1)
        {
            var d = (int)elapsed.TotalDays;
            phrase = $"{d} day{(d == 1 ? "" : "s")}";
        }
        else
        {
            var h = (int)elapsed.TotalHours;
            phrase = $"{h} hour{(h == 1 ? "" : "s")}";
        }
        return $"Discovered {phrase} ago — rediscover for fresh data";
    }
}
