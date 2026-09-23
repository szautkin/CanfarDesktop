using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Which viewer a mark belongs to. They keep separate marks because they keep separate files.</summary>
public enum AnnotationViewer { Fits, Cube }

/// <summary>
/// The little the annotation tools need from the running app.
///
/// Deliberately small: marks live in the store, and the store needs no window. What only the app can
/// answer is which file a viewer is currently showing — and, once something has changed, that the
/// canvas should redraw and perhaps pick a mark out.
/// </summary>
public interface IAnnotationHost
{
    /// <summary>The file the viewer is showing, or null when nothing is open in it.</summary>
    Task<string?> ActiveTargetAsync(AnnotationViewer viewer);

    /// <summary>
    /// Redraw a viewer after a change, optionally selecting a mark. False when the viewer is not
    /// showing that target — the marks are stored either way, and will be there when it is opened.
    /// </summary>
    Task<bool> RefreshAsync(AnnotationViewer viewer, string target, string? selectId);

    /// <summary>
    /// Let go of whatever mark the viewer has picked out. False when it is not showing that target.
    ///
    /// Its own method rather than a null <c>selectId</c> on <see cref="RefreshAsync"/>: null there
    /// means "leave the selection as it is", which every redraw-after-a-change caller depends on.
    /// Pointing at nothing is a third thing, and one nullable string cannot say three things.
    /// </summary>
    Task<bool> DeselectAsync(AnnotationViewer viewer, string target);
}

/// <summary>One mark, as an agent sees it.</summary>
public sealed record AnnotationView(
    string Id,
    string Kind,
    string Space,
    double X,
    double Y,
    double? Z,
    double? HalfWidth,
    double? HalfHeight,
    string? Text,
    string Author,
    string Colour,
    double FontSize,
    bool Bold,
    double Stroke,
    string? CreatedAt,
    int? Hdu = null)   // the extension, in a listing across a file's extensions
{
    public static AnnotationView From(Annotation a)
    {
        var style = a.EffectiveStyle;
        return new AnnotationView(
            a.Id, a.Kind.AsString(), a.Anchor.SpaceName,
            a.Anchor.X, a.Anchor.Y,
            a.Anchor.Space == AnchorSpace.Data ? a.Anchor.Z : null,
            a.Extent?.HalfWidth, a.Extent?.HalfHeight,
            string.IsNullOrEmpty(a.Text) ? null : a.Text,
            a.Author == MarkAuthor.Agent ? "agent" : "user",
            style.ColourHex(), style.FontSize, style.Bold, style.Stroke,
            string.IsNullOrEmpty(a.CreatedAt) ? null : a.CreatedAt);
    }
}

/// <summary>
/// The marks on one target. <paramref name="Shown"/> says whether the viewer is currently showing that
/// file — marks are stored against the file, not against the window, so they can be read and written
/// for a file nobody has open.
/// </summary>
public sealed record AnnotationListView(
    string Viewer,
    string? Target,
    bool Shown,
    int Count,
    IReadOnlyList<AnnotationView> Annotations,
    string? Message = null)
{
    public static AnnotationListView NothingOpen(string viewer, string message)
        => new(viewer, null, false, 0, Array.Empty<AnnotationView>(), message);
}

/// <summary>
/// The outcome of adding, changing or removing a mark.
///
/// <see cref="Removed"/> is only meaningful for a clear, where the caller did not name what it was
/// deleting and so cannot tell from <see cref="Remaining"/> how much it just took off.
/// </summary>
public sealed record AnnotationChange(
    bool Applied,
    string Viewer,
    string? Target,
    bool Shown,
    AnnotationView? Annotation,
    int Remaining,
    string? Message = null,
    int? Removed = null)
{
    public static AnnotationChange NothingOpen(string viewer, string message)
        => new(false, viewer, null, false, null, 0, message);
}
