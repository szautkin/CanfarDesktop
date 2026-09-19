using System.Text.RegularExpressions;

namespace CanfarDesktop.Helpers;

/// <summary>
/// One probed service.
///
/// <para><b>Reachable</b> — the host answered at all. <b>Available</b> — the service's own
/// <c>/availability</c> document says it is up: true, false when it declares itself down (with
/// <see cref="Note"/> carrying its reason), or null when it publishes no document this probe could
/// read, which is "unknown" and deliberately not "down".</para>
///
/// <para><b>RequiresAuth</b> — using this service needs a signed-in session. A signed-out user can
/// have every service healthy and none of them usable, which is why that is counted separately.</para>
/// </summary>
public sealed record ServiceProbeResult(
    string Name, string Url, bool Reachable, int? StatusCode, long LatencyMs, string? Error,
    bool? Available = null, string? Note = null, bool RequiresAuth = false)
{
    /// <summary>Up as far as anyone can tell: reachable, and not declaring itself down.</summary>
    public bool IsHealthy => Reachable && Available != false;

    /// <summary>Healthy AND not gated behind a sign-in the caller may not have.</summary>
    public bool IsUsableAnonymously => IsHealthy && !RequiresAuth;
}

/// <summary>Counts over a probe run, so a caller does not have to re-derive the same two sums.</summary>
public sealed record ServiceHealthSummary(int Count, int HealthyCount, int UsableCount);

/// <summary>
/// Everything about the IVOA availability question that does not need an HTTP client: where a
/// service's document lives, what it says, and what a set of answers adds up to.
///
/// Extracted from <see cref="Services.ServiceHealthProbe"/> so the contract is unit-testable without
/// any network, the same way <see cref="DataLinkArtifactSelector"/> is.
/// </summary>
public static class ServiceAvailability
{
    /// <summary>
    /// Where a service's availability document lives: at the SERVICE root, so a configured endpoint
    /// that already points into the service has its sub-path trimmed first.
    ///
    /// <para>Trimmed by name rather than by "drop the last segment", because most of these bases ARE
    /// the service root and dropping a segment would walk out of them.</para>
    /// </summary>
    public static string UrlFor(string baseUrl)
    {
        var root = (baseUrl ?? string.Empty).TrimEnd('/');
        foreach (var sub in ServiceSubPaths)
        {
            if (root.EndsWith(sub, StringComparison.OrdinalIgnoreCase))
            {
                root = root[..^sub.Length];
                break;
            }
        }
        return root.TrimEnd('/') + "/availability";
    }

    /// <summary>Sub-paths a configured endpoint may carry below its service root.</summary>
    private static readonly string[] ServiceSubPaths = ["/nodes", "/files", "/sync", "/async", "/home"];

    /// <summary>
    /// Read a VOSI availability document. Namespace-agnostic, because the prefix varies between
    /// services and a prefix is not information. Returns (null, null) for anything unreadable — a
    /// service that publishes no document is unknown, not down.
    /// </summary>
    public static (bool? Available, string? Note) Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);

        var available = Regex.Match(body, @"<(?:\w+:)?available\s*>\s*(true|false)\s*<", RegexOptions.IgnoreCase);
        if (!available.Success) return (null, null);

        var note = Regex.Match(body, @"<(?:\w+:)?note\s*>(.*?)</(?:\w+:)?note\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return (
            string.Equals(available.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase),
            note.Success && !string.IsNullOrWhiteSpace(note.Groups[1].Value) ? note.Groups[1].Value.Trim() : null);
    }

    /// <summary>Counts over a set of results: healthy, and of those, usable without signing in.</summary>
    public static ServiceHealthSummary Summarize(IReadOnlyList<ServiceProbeResult> results)
        => new(results.Count, results.Count(r => r.IsHealthy), results.Count(r => r.IsUsableAnonymously));
}
