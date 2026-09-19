namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// The notebooks opened before, capped at 15 and newest first.
///
/// A <see cref="RecentFilesStore"/> with the Notebook folder and cap. Two things came free from the
/// collapse: the path is injectable now, so a test no longer writes to the real user's AppData, and
/// entries whose file has since been deleted are dropped at load rather than sitting in the welcome
/// page as rows that cannot be opened.
/// </summary>
public sealed class RecentNotebooksService : RecentFilesStore
{
    /// <param name="filePath">Overrides the default location, for tests.</param>
    public RecentNotebooksService(string? filePath = null)
        : base(area: "Notebook", fileName: "recent-notebooks.json", max: 15, filePath: filePath) { }
}
