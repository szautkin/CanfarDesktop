using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>
/// Typed failure from the discovery pipeline. Carries a stable <see cref="FailureCategory"/> for
/// the cache and a user-facing message (the exception <see cref="Exception.Message"/>).
/// </summary>
public class ImageDiscoveryException : Exception
{
    public FailureCategory Category { get; }

    public ImageDiscoveryException(FailureCategory category, string displayMessage) : base(displayMessage)
        => Category = category;

    public static ImageDiscoveryException JobSubmitFailed(string message)
        => new(FailureCategory.JobSubmitFailed, $"Probe submit failed: {message}");

    public static ImageDiscoveryException JobTimedOut()
        => new(FailureCategory.JobTimedOut, "Probe timed out");

    public static ImageDiscoveryException ManifestFetchFailed(string message)
        => new(FailureCategory.ManifestFetchFailed, $"Manifest fetch failed: {message}");

    public static ImageDiscoveryException ManifestParseFailed(string detail)
        => new(FailureCategory.ManifestParseFailed, $"Manifest parse failed: {detail}");

    public static ImageDiscoveryException Cancelled()
        => new(FailureCategory.Cancelled, "Discovery cancelled");

    public static ImageDiscoveryException Unknown(string message)
        => new(FailureCategory.Unknown, message);
}
