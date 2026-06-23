namespace CanfarDesktop.Models;

/// <summary>
/// A user's research annotation for a downloaded observation, keyed by publisher ID.
/// Stored in SQLite and full-text indexed (note + tags).
/// </summary>
public record ObservationNote
{
    public string PublisherID { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public int Rating { get; init; }                       // 0–5 stars (0 = unrated)
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>True when there is nothing worth persisting (blank note, unrated, no tags).</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Note) && Rating == 0 && Tags.Count == 0;
}
