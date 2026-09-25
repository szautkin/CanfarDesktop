namespace CanfarDesktop.Models;

/// <summary>How a job ended.</summary>
public enum JobOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// What launched a job, so the history can tell a probe the app ran on the user's behalf from a job the
/// user submitted themselves.
/// </summary>
public enum JobOrigin
{
    /// <summary>A headless job the user launched.</summary>
    User,

    /// <summary>An image-inspection probe run by the discovery coordinator.</summary>
    ImageProbe,
}

/// <summary>
/// One finished batch job, kept after CANFAR has forgotten it.
///
/// Skaha reaps headless jobs, and the image-discovery coordinator deletes its own probe jobs as soon as
/// they finish — success or failure. Between the two, the Portal's Batch Jobs card could show a job fail
/// and then have nothing to say about it a minute later: the job was gone from the listing, its logs and
/// events gone with it, and the only trace a count that had ticked from Running to Failed.
/// </summary>
public sealed record JobRecord
{
    /// <summary>Skaha session id. Also the de-duplication key.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Image { get; init; } = string.Empty;

    public JobOrigin Origin { get; init; }

    public JobOutcome Outcome { get; init; }

    /// <summary>
    /// The status string Skaha last reported, kept verbatim — "Failed", "Succeeded", "Terminating",
    /// whatever it actually said. The outcome above is this app's reading of it; this is the evidence.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>When the job started, if Skaha said. ISO-8601.</summary>
    public string StartedAt { get; init; } = string.Empty;

    /// <summary>When it was recorded as finished. ISO-8601.</summary>
    public string FinishedAt { get; init; } = string.Empty;

    /// <summary>
    /// Why it failed, in as much detail as could be recovered.
    ///
    /// This is the whole point of the history. A status of "Failed" is not a reason, and by the time
    /// anyone reads it the job — and its logs — are gone.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>The image being inspected, for <see cref="JobOrigin.ImageProbe"/>.</summary>
    public string? TargetImage { get; init; }

    /// <summary>A one-line summary for a collapsed row.</summary>
    public string Summary => TargetImage is { Length: > 0 } target
        ? $"{OriginLabel(Origin)} — {target}"
        : OriginLabel(Origin);

    public static string OriginLabel(JobOrigin origin)
        => origin == JobOrigin.ImageProbe ? "Image inspection" : "Batch job";
}
