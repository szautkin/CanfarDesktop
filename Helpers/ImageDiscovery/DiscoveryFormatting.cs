using System.Globalization;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Helpers.ImageDiscovery;

/// <summary>
/// Pure presentation helpers for image-discovery rows (failure labels, relative time, package
/// counts, recovery heuristic). 1-to-1 with the macOS <c>ImageDiscoveryModel</c> static helpers.
/// </summary>
public static class DiscoveryFormatting
{
    /// <summary>Short pill label for a failure category (mirrors macOS <c>categoryLabel</c>).</summary>
    public static string CategoryLabel(FailureCategory category) => category switch
    {
        FailureCategory.JobSubmitFailed => "Submit failed",
        FailureCategory.JobTimedOut => "Timed out",
        FailureCategory.ManifestFetchFailed => "No manifest",
        FailureCategory.ManifestParseFailed => "Bad manifest",
        FailureCategory.Cancelled => "Cancelled",
        _ => "Failed",
    };

    /// <summary>
    /// Compact relative time ("just now", "5m ago", "3d ago"); falls back to a short absolute date
    /// ("May 14") beyond 14 days so the row stays narrow. Mirrors macOS <c>timeAgo</c>.
    /// </summary>
    public static string TimeAgo(DateTimeOffset date, DateTimeOffset now)
    {
        var elapsed = (now - date).TotalSeconds;
        if (elapsed < 30) return "just now"; // also covers future/clock-skew (negative)
        if (elapsed < 60) return $"{(int)elapsed}s ago";
        if (elapsed < 3_600) return $"{(int)(elapsed / 60)}m ago";
        if (elapsed < 86_400) return $"{(int)(elapsed / 3_600)}h ago";
        if (elapsed < 14 * 86_400) return $"{(int)(elapsed / 86_400)}d ago";
        return date.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    public static string TimeAgo(DateTimeOffset date) => TimeAgo(date, DateTimeOffset.UtcNow);

    /// <summary>
    /// True when a timed-out failure might still be rescued by the coordinator's background grace
    /// poll (the manifest may yet land at VOSpace). Conservative 10-minute window from the attempt,
    /// matching the macOS grace budget. Mirrors macOS <c>isLikelyStillRecovering</c>.
    /// </summary>
    public static bool IsLikelyStillRecovering(FailureCategory category, DateTimeOffset attemptedAt, DateTimeOffset now)
        => category == FailureCategory.JobTimedOut && (now - attemptedAt).TotalMinutes < 10;

    public static bool IsLikelyStillRecovering(FailureCategory category, DateTimeOffset attemptedAt)
        => IsLikelyStillRecovering(category, attemptedAt, DateTimeOffset.UtcNow);

    /// <summary>Total installed packages across every ecosystem (dpkg + rpm + apk + python + R).</summary>
    public static int PackageCount(ImageManifest m)
        => m.DpkgPackages.Count + m.RpmPackages.Count + m.ApkPackages.Count
           + m.PythonPackages.Count + m.RPackages.Count;
}
