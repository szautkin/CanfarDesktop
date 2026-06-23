using System.Text.Json.Serialization;

namespace CanfarDesktop.Models.ImageDiscovery;

/// <summary>Stable failure categories stored in the discovery cache.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FailureCategory
{
    JobSubmitFailed,
    JobTimedOut,
    ManifestFetchFailed,
    ManifestParseFailed,
    Cancelled,
    Unknown,
}

/// <summary>
/// What the cache last knew about an image: a successful manifest, or a typed failure with the
/// attempt timestamp (so the UI can show "Failed 2d ago — retry?" vs "never discovered").
/// </summary>
public record LastOutcome
{
    public bool IsSuccess { get; init; }
    public string ImageID { get; init; } = string.Empty;
    public ImageManifest? Manifest { get; init; }
    public FailureCategory? Category { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public string? JobID { get; init; }

    public static LastOutcome Success(ImageManifest manifest) => new()
    {
        IsSuccess = true,
        ImageID = manifest.ImageID,
        Manifest = manifest,
        AttemptedAt = manifest.CapturedAt,
    };

    public static LastOutcome Failure(string imageID, FailureCategory category, string message,
        DateTimeOffset attemptedAt, string? jobID) => new()
    {
        IsSuccess = false,
        ImageID = imageID,
        Category = category,
        Message = message,
        AttemptedAt = attemptedAt,
        JobID = jobID,
    };
}

/// <summary>A near-miss image with its coverage score and the list of unsatisfied constraints.</summary>
public record PartialMatch(string ImageID, double Score, IReadOnlyList<string> Missing);

/// <summary>Distinct package names across all cached manifests, for the discovery filter pane.</summary>
public class AllPackages
{
    public HashSet<string> OsFamilies { get; } = new();
    public Dictionary<string, HashSet<string>> OsVersionsByFamily { get; } = new();
    public HashSet<string> Dpkg { get; } = new();
    public HashSet<string> Rpm { get; } = new();
    public HashSet<string> Apk { get; } = new();
    public HashSet<string> Python { get; } = new();
    public HashSet<string> R { get; } = new();

    public bool IsEmpty =>
        OsFamilies.Count == 0 && OsVersionsByFamily.Count == 0 && Dpkg.Count == 0 && Rpm.Count == 0
        && Apk.Count == 0 && Python.Count == 0 && R.Count == 0;
}
