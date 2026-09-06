using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// Marks on the FITS canvas: drawing them, and the presses that create, move and resize them.
///
/// What a press MEANS is not decided here — <see cref="AnnotationGeometry.GrabAt"/> decides it, and the
/// cube viewer asks the same question of the same function. Two canvases that each decided for
/// themselves would eventually disagree about whether a grip or the shape under it wins, and the
/// answer to that is not obvious enough to be worth arriving at twice.
/// </summary>
public sealed partial class FitsViewerPage
{
    private IAnnotationStore? _annotationStore;
    private List<Annotation> _annotations = [];

    /// <summary>The file the marks currently on screen were loaded for.</summary>
    private string? _loadedTarget;

    /// <summary>The pencil. While it is armed, a press on empty canvas draws rather than pans.</summary>
    public bool DrawingArmed { get; set; }

    /// <summary>What a newly drawn mark will be.</summary>
    public AnnotationKind DrawingKind { get; set; } = AnnotationKind.Circle;

    private string? _editingId;
    private string? _selectedId;

    private MarkGrab _grab = new MarkGrab.None();
    private (double X, double Y) _dragOrigin;

    /// <summary>The canvas as the renderer sees it. Rebuilt per use: the transform moves under it.</summary>
    private FitsAnnotationSurface Surface() => new(
        (x, y) => { var p = ImageToScreen(new Windows.Foundation.Point(x, y)); return (p.X, p.Y); },
        () => ViewModel.ImageData?.Wcs is { IsValid: true } wcs ? wcs : null);

    public void AttachAnnotationStore(IAnnotationStore store) => _annotationStore = store;

    /// <summary>
    /// The file whose marks belong on this canvas — the answer <c>annotate_fits</c> needs.
    ///
    /// Taken from the view model rather than pushed in when a tab opens: the path is already there, and
    /// a second copy of it is a second thing that can be stale. A tab with nothing loaded has none.
    /// </summary>
    public string? AnnotationTarget
        => string.IsNullOrWhiteSpace(ViewModel.FilePath) ? null : ViewModel.FilePath;

    /// <summary>
    /// Load this file's marks if the file has changed under us. Called from the render, which is called
    /// from every view change — so a newly opened image picks up its marks without anyone remembering to
    /// tell it to.
    /// </summary>
    private void EnsureAnnotationsLoaded()
    {
        var target = AnnotationTarget;
        if (string.Equals(target, _loadedTarget, StringComparison.Ordinal)) return;

        _loadedTarget = target;
        _editingId = _selectedId = null;
        _annotations = target is null || _annotationStore is null
            ? []
            : _annotationStore.LoadFor(target).ToList();
    }

    /// <summary>
    /// Re-read and redraw, optionally picking a mark out. Called after an agent has changed something:
    /// the store is the truth, and this view had a copy of it.
    /// </summary>
    public bool RefreshAnnotations(string target, string? selectId)
    {
        if (AnnotationTarget is not { } mine || !string.Equals(mine, target, StringComparison.OrdinalIgnoreCase))
            return false;

        _annotations = _annotationStore?.LoadFor(target).ToList() ?? [];
        if (selectId is not null) _selectedId = selectId;

        RenderAnnotations();
        return true;
    }

    /// <summary>Draw what is there. Also called from every pan, zoom and rotation — the marks move with the image.</summary>
    private void RenderAnnotations()
    {
        EnsureAnnotationsLoaded();
        AnnotationCanvas.EditingId = _editingId;
        AnnotationCanvas.SelectedId = _selectedId;
        AnnotationCanvas.Render(_annotations, Surface(), ImageCanvas.ActualWidth);
    }

    private void SaveAnnotations()
    {
        if (AnnotationTarget is not { } target || _annotationStore is null) return;

        try
        {
            _annotationStore.SaveFor(target, _annotations);
        }
        catch (Exception ex)
        {
            // A save that quietly did nothing loses a drawing, and they find out the next time they open
            // the file. Nothing here can put up a dialog, so it goes to the log and the marks stay on
            // screen — the user has not lost them yet.
            System.Diagnostics.Debug.WriteLine($"Could not save annotations for {target}: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the annotation layer wants this press. True means the canvas must not pan.
    ///
    /// Called before the pan handling rather than after, because a press that takes hold of a mark and
    /// ALSO starts a pan drags the image out from under the mark being moved.
    /// </summary>
    private bool TryBeginAnnotationGesture(Windows.Foundation.Point at)
    {
        if (AnnotationTarget is null) return false;

        var surface = Surface();
        _grab = AnnotationGeometry.GrabAt(_annotations, surface, _editingId, DrawingArmed, at.X, at.Y);
        _dragOrigin = (at.X, at.Y);

        switch (_grab)
        {
            case MarkGrab.None:
                return false;

            case MarkGrab.Place:
                var anchor = AnchorAt(at, surface);
                if (anchor is null) return false;

                // Placed with a size, then dragged to the size you want: a mark that appeared with no
                // extent would be invisible until the drag ended, and a drag that starts on nothing
                // looks like it did nothing.
                var mark = new Annotation
                {
                    Id = "m" + Guid.NewGuid().ToString("N")[..8],
                    Kind = DrawingKind,
                    Anchor = anchor,
                    Extent = DrawingKind.NeedsExtent()
                        ? Extent.Square(AnnotationGeometry.HalfFromDrag(surface, anchor, 12))
                        : null,
                    Author = MarkAuthor.User,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                };

                // A callout or a text mark with no words yet cannot be stored — it would fail its own
                // validation — so drawing one puts it straight into editing, where the label is typed.
                _annotations.Add(mark);
                _editingId = _selectedId = mark.Id;
                _grab = mark.Extent is null ? new MarkGrab.None() : new MarkGrab.Resize(mark.Id);
                RenderAnnotations();
                return true;

            case MarkGrab.Move move:
                _selectedId = move.Id;
                RenderAnnotations();
                return true;

            case MarkGrab.Resize resize:
                _selectedId = resize.Id;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Continue a move or a resize. Returns true while the annotation layer owns the pointer.
    /// </summary>
    private bool ContinueAnnotationGesture(Windows.Foundation.Point at)
    {
        var surface = Surface();

        switch (_grab)
        {
            case MarkGrab.Move move:
            {
                var index = _annotations.FindIndex(a => a.Id == move.Id);
                if (index < 0) return false;

                // Where the shape was taken hold of is subtracted, so it does not jump to centre itself
                // under the pointer the moment it starts moving.
                var target = new Windows.Foundation.Point(at.X - move.GrabDx, at.Y - move.GrabDy);
                if (AnchorAt(target, surface, _annotations[index].Anchor.Space) is not { } anchor) return true;

                _annotations[index] = _annotations[index] with { Anchor = anchor };
                RenderAnnotations();
                return true;
            }

            case MarkGrab.Resize resize:
            {
                var index = _annotations.FindIndex(a => a.Id == resize.Id);
                if (index < 0) return false;

                var half = AnnotationGeometry.ResizeHalf(_annotations[index], surface, at.X, at.Y);
                if (half is null) return true;

                _annotations[index] = _annotations[index] with { Extent = Extent.Square(half.Value) };
                RenderAnnotations();
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Finish a gesture and persist. Returns true if the annotation layer had the pointer.</summary>
    private bool EndAnnotationGesture()
    {
        if (_grab is MarkGrab.None) return false;

        _grab = new MarkGrab.None();

        // Anything that failed its own validation during the drag — a shape dragged to nothing — is
        // dropped rather than stored: the store would refuse it, and a mark that is there until you
        // reopen the file is worse than one that never appeared.
        _annotations.RemoveAll(a => a.Validate() is not null);

        SaveAnnotations();
        RenderAnnotations();
        return true;
    }

    /// <summary>
    /// The anchor a screen point corresponds to, in the space asked for. Sky whenever the image has WCS,
    /// because a sky mark points at the same place in a different image of the same field.
    /// </summary>
    private AnnotationAnchor? AnchorAt(Windows.Foundation.Point screen, FitsAnnotationSurface surface, AnchorSpace? space = null)
    {
        var pixel = e_PointToImage(screen);
        var wcs = ViewModel.ImageData?.Wcs is { IsValid: true } valid ? valid : null;

        var wanted = space ?? (wcs is not null ? AnchorSpace.Sky : AnchorSpace.ImagePixel);

        if (wanted == AnchorSpace.Sky && wcs is not null)
        {
            var (ra, dec) = wcs.PixelToWorld(pixel.X, pixel.Y);
            var sky = AnnotationAnchor.Sky(ra, dec);
            // A projection can put a point off the sky at the edge of a wide field. An image pixel is
            // always somewhere, so it is the fallback rather than a refusal.
            if (sky.IsValid) return sky;
        }

        var imagePixel = AnnotationAnchor.ImagePixel(pixel.X, pixel.Y);
        return imagePixel.IsValid ? imagePixel : null;
    }

    /// <summary>Give up editing, keeping whatever is valid. Called when the pencil is put down.</summary>
    public void EndAnnotationEditing()
    {
        _editingId = null;
        _annotations.RemoveAll(a => a.Validate() is not null);
        SaveAnnotations();
        RenderAnnotations();
    }
}
