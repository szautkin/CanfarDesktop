namespace CanfarDesktop.Services.Fits;

/// <summary>
/// The FITS images opened before, capped at 8 and newest first — the list the viewer's empty state
/// offers, which it did not have while the cube viewer next to it did.
///
/// Same store, same cap and same behaviour as the cube's, because they are the same list of the same
/// kind of thing in two windows.
/// </summary>
public sealed class RecentFitsService : RecentFilesStore
{
    /// <param name="filePath">Overrides the default location, for tests.</param>
    public RecentFitsService(string? filePath = null)
        : base(area: "FitsViewer", fileName: "recent-fits.json", max: 8, filePath: filePath) { }
}
