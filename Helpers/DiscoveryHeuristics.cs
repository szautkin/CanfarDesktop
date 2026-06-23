using System.Text;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Helpers;

/// <summary>Which probe strategy applies to a target image.</summary>
public enum ProbeStrategy
{
    /// <summary>Run probe.sh inside the target image itself (image supports headless).</summary>
    InTarget,
    /// <summary>Launch a known-good headless host that introspects the target via syft.</summary>
    Inspector,
}

/// <summary>Pure decision helpers for the image-discovery coordinator (no I/O — fully testable).</summary>
public static class DiscoveryHeuristics
{
    /// <summary>Backoff schedule (seconds) for Skaha's "jobs.batch not found" submit race.</summary>
    public static readonly int[] SkahaRaceBackoffsSeconds = { 3, 7, 15 };

    /// <summary>Generate a fresh 8-char lowercase hex job-name suffix.</summary>
    public static string NewJobSuffix() => Guid.NewGuid().ToString("N")[..8].ToLowerInvariant();

    /// <summary>
    /// Build a Skaha session name safe for K8s DNS-1123 labels (≤63 chars, lowercase
    /// alphanumerics + hyphens, no leading/trailing or consecutive hyphens). <paramref name="suffix"/>
    /// is an 8-char unique tail (caller-supplied so the result is deterministic in tests).
    /// </summary>
    public static string MakeJobName(string prefix, string imageID, string suffix)
    {
        var budget = 63 - prefix.Length - 1 - suffix.Length - 1; // "<prefix>-<middle>-<suffix>"
        var safe = ImageManifest.Sanitize(imageID).ToLowerInvariant();
        var sliceLength = Math.Min(Math.Max(0, budget), safe.Length);
        var middle = safe[..sliceLength];

        var sb = new StringBuilder(middle.Length);
        foreach (var ch in middle)
        {
            var c = char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-';
            if (c == '-' && sb.Length > 0 && sb[^1] == '-') continue; // collapse runs
            sb.Append(c);
        }
        var trimmed = sb.ToString().Trim('-');
        return trimmed.Length == 0 ? $"{prefix}-{suffix}" : $"{prefix}-{trimmed}-{suffix}";
    }

    /// <summary>
    /// Strategy from the image's session types. Headless-capable → in-target probe; everything else
    /// (and unknown/null types) → inspector, which is strictly safer for private/unknown images.
    /// </summary>
    public static ProbeStrategy Strategy(IReadOnlyList<string>? types)
        => types is null ? ProbeStrategy.Inspector
         : types.Contains("headless") ? ProbeStrategy.InTarget
         : ProbeStrategy.Inspector;

    /// <summary>
    /// A "stub" manifest is one a failed probe wrote as a placeholder: no packages of any kind AND
    /// a probeNotes field describing the failure. We refuse to cache these so a fresh probe re-runs.
    /// </summary>
    public static bool IsStubManifest(ImageManifest m)
    {
        var hasPackages = m.DpkgPackages.Count > 0 || m.RpmPackages.Count > 0 || m.ApkPackages.Count > 0
            || m.PythonPackages.Count > 0 || m.RPackages.Count > 0 || m.CondaEnvs.Count > 0;
        if (hasPackages) return false;
        return !string.IsNullOrEmpty(m.ProbeNotes);
    }

    /// <summary>
    /// Match the specific Skaha error returned during the K8s informer-cache race (the POST created
    /// the job but the immediate GET 404'd). Other 5xx faults shouldn't trigger a retry.
    /// </summary>
    public static bool IsSkahaJobNotFoundRace(string message)
    {
        var msg = message.ToLowerInvariant();
        return msg.Contains("jobs.batch") && msg.Contains("not found");
    }
}
