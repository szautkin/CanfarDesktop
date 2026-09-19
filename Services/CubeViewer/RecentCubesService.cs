namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// The cubes the 3D viewer has opened before, capped at 8 and newest first.
///
/// A <see cref="RecentFilesStore"/> with this viewer's own folder and cap. It used to be its own copy
/// of that class — its doc comment said so, "mirrors RecentNotebooksService" — and the FITS viewer
/// would have made a third.
/// </summary>
public sealed class RecentCubesService : RecentFilesStore
{
    /// <param name="filePath">Overrides the default location, for tests.</param>
    public RecentCubesService(string? filePath = null)
        : base(area: "CubeViewer", fileName: "recent-cubes.json", max: 8, filePath: filePath) { }
}
