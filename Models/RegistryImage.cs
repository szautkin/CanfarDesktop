namespace CanfarDesktop.Models;

/// <summary>
/// An image as the container registry describes it.
///
/// The platform's own catalogue (<c>/v1/image</c>) is the app's default source of images, and it is a
/// curated subset: Skaha lists what it will launch. The registry behind it holds a great deal more, and
/// someone who knows the image they want — a colleague's build, a tag Skaha has not picked up — has no
/// way to reach it from a list that does not contain it.
///
/// So this is the other door. A registry image enters the app only because someone searched for it and
/// added it, never by a background sweep: pulling a whole Harbor instance to populate a dashboard card
/// would be a great deal of traffic to answer a question nobody asked.
///
/// One type serves both roles, because they are the same image at two moments.
/// <see cref="AddedAt"/> is what separates them: null for a search result someone is looking at, set
/// once it is in their list.
/// </summary>
public sealed record RegistryImage(string Id, IReadOnlyList<string> Types, string? AddedAt = null)
{
    /// <summary>
    /// The session types the platform recognises, which is what the CANFAR registry names its labels
    /// after.
    ///
    /// A registry image carries labels for whatever its authors chose; only the ones that name a
    /// session type mean anything to a launch, and those are what the images widget filters by. Kept
    /// here rather than in the widget because the registry search and the widget must agree on it — one
    /// list, one meaning.
    /// </summary>
    public static readonly string[] SessionTypeLabels =
        ["notebook", "desktop", "desktop-app", "carta", "headless", "contributed"];

    /// <summary>
    /// Build one from a registry reference and the labels the registry reports.
    ///
    /// Labels that do not name a session type are dropped rather than kept as types: an image labelled
    /// "gpu" is not launchable as a "gpu" session, and offering it as one would produce a launch the
    /// platform refuses.
    /// </summary>
    public static RegistryImage FromLabels(string id, IEnumerable<string> labels, string? addedAt = null)
    {
        var types = labels
            .Select(l => (l ?? string.Empty).Trim().ToLowerInvariant())
            .Where(l => SessionTypeLabels.Contains(l))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new RegistryImage(id, types, addedAt);
    }

    /// <summary>Whether this image can be offered on the Standard launch tab for a session type.</summary>
    public bool IsLaunchableAs(string sessionType)
        => Types.Contains((sessionType ?? string.Empty).Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The project segment of the reference — <c>host/PROJECT/name:tag</c> — for grouping.</summary>
    public string? Project
    {
        get
        {
            var parts = Id.Split('/');
            return parts.Length >= 3 ? parts[1] : null;
        }
    }
}
