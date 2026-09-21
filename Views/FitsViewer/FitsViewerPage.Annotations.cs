using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// This canvas, as <see cref="MarkEditor"/> sees it.
///
/// Everything a person does to a mark lives in that class, shared with the cube viewer. What is here is
/// only what is genuinely different about a flat image: where a mark lands on screen, and what a press
/// means in sky or image-pixel coordinates.
///
/// It used to be six hundred lines that were nearly the cube's six hundred, and the cost was not the
/// duplication — it was the drift. Fixes landed in one viewer and not the other, and a person who had
/// used one found the other subtly wrong.
/// </summary>
public sealed partial class FitsViewerPage : IMarkCanvas
{
    private IAnnotationStore? _annotationStore;
    private MarkEditor? _marks;
    private Controls.MarkPanelBinding? _panelBinding;

    /// <summary>The marks on this canvas. Built on first use, once the store has been attached.</summary>
    private MarkEditor Marks => _marks ??= new MarkEditor(
        this,
        _annotationStore,
        new Services.SettingsMarkStylePreference(
            () => App.Services.GetService(typeof(Services.ISettingsService)) as Services.ISettingsService));

    public void AttachAnnotationStore(IAnnotationStore store) => _annotationStore = store;

    /// <summary>The pencil. While it is armed, a press on empty canvas draws rather than pans.</summary>
    public bool DrawingArmed
    {
        get => Marks.DrawArmed;
        set => Marks.SetDrawArmed(value);
    }

    /// <summary>What a newly drawn mark will be.</summary>
    public AnnotationKind DrawingKind
    {
        get => Marks.Kind;
        set => MarksPanelControl.Kind = value;
    }

    // ── IMarkCanvas: the two things a flat image does differently ───────────────────────────────

    /// <summary>
    /// The file whose marks belong on this canvas — the answer <c>annotate_fits</c> needs.
    ///
    /// Taken from the view model rather than pushed in when a tab opens: the path is already there, and
    /// a second copy of it is a second thing that can be stale. A tab with nothing loaded has none.
    /// </summary>
    public string? Target => string.IsNullOrWhiteSpace(ViewModel.FilePath) ? null : ViewModel.FilePath;

    /// <summary>The canvas as the renderer sees it. Rebuilt per use: the transform moves under it.</summary>
    public IAnnotationSurface Surface => new FitsAnnotationSurface(
        (x, y) => { var p = ImageToScreen(new Point(x, y)); return (p.X, p.Y); },
        () => ViewModel.ImageData?.Wcs is { IsValid: true } wcs ? wcs : null,
        () => ViewModel.ImageData is { } img ? (img.Width, img.Height) : (0, 0));

    /// <summary>
    /// The mark under a point on the canvas, if any.
    ///
    /// Asked by the press-owner decision, which has to know whether a press would land on a mark
    /// before deciding whose press it is — through the same hit test the marks themselves use, so the
    /// two cannot disagree about what "on a mark" means.
    /// </summary>
    private string? MarkAt(Point at)
        => AnnotationGeometry.AnnotationAt(Marks.Marks, Surface, at.X, at.Y);

    public IMarkLabelField Label => MarkEditorField;

    /// <summary>The field hangs off the same element presses are measured against, so no translation.</summary>
    public (double X, double Y) ToLabelHost(double x, double y) => (x, y);

    /// <summary>
    /// Run something after the current input event.
    ///
    /// Opening the naming field inline would put a text box up in the middle of the pointer event that
    /// created the mark.
    /// </summary>
    public void Later(Action what) => DispatcherQueue.TryEnqueue(() => what());

    /// <summary>
    /// The anchor a press means: sky whenever the image has WCS, because a sky mark points at the same
    /// place in a DIFFERENT image of the same field.
    ///
    /// A mark being moved keeps the space it was pinned in, so dragging a sky mark does not quietly
    /// demote it to pixels the first time it is touched.
    /// </summary>
    public AnnotationAnchor? AnchorFor(double x, double y, Annotation? moving)
    {
        if (ImageFrame() is not { } frame) return null;

        var pressed = e_PointToImage(new Point(x, y));

        // Kept on the image, through the same rectangle the surface tests marks against.
        //
        // The canvas is larger than the picture whenever the view is zoomed out, so a press can land in
        // the margin beside it, and a drag can carry a mark past the edge. Both used to produce an
        // anchor at a pixel the image does not have; the surface now declines to place those, so left
        // alone the mark would be created and never drawn, or would disappear mid-drag.
        //
        // The two cases want different answers, and which one this is is exactly what `moving` says.
        // A press in the margin is a miss: no mark. A drag that runs off the edge is a gesture, and the
        // mark slides along the border rather than sticking or vanishing.
        if (moving is null && !frame.Contains(pressed.X, pressed.Y)) return null;

        var pixel = frame.Clamp(pressed.X, pressed.Y);
        var wcs = ViewModel.ImageData?.Wcs is { IsValid: true } valid ? valid : null;
        var wanted = moving?.Anchor.Space ?? (wcs is not null ? AnchorSpace.Sky : AnchorSpace.ImagePixel);

        if (wanted == AnchorSpace.Sky)
        {
            // Through the surface's own converter, so the press → sky direction and the sky → screen
            // direction cannot end up disagreeing about which way up the pixels are.
            var sky = FitsAnnotationSurface.SkyAt(wcs, ViewModel.ImageData?.Height ?? 0, pixel.X, pixel.Y);

            // A projection can put a point off the sky at the edge of a wide field. An image pixel is
            // always somewhere, so it is the fallback rather than a refusal.
            if (sky is not null) return sky;
        }

        var imagePixel = AnnotationAnchor.ImagePixel(pixel.X, pixel.Y);
        return imagePixel.IsValid ? imagePixel : null;
    }

    /// <summary>
    /// The loaded image's extent in display pixels, or null when there is nothing to annotate.
    /// The one rectangle both the placing of a mark and the drawing of it are measured against.
    /// </summary>
    private FitsRegion? ImageFrame()
        => ViewModel.ImageData is { Width: > 0, Height: > 0 } img
            ? FitsRegion.WholeImage(img.Width, img.Height)
            : null;

    public void Draw(IReadOnlyList<Annotation> marks, string? selectedId, string? editingId)
    {
        AnnotationCanvas.EditingId = editingId;
        AnnotationCanvas.SelectedId = selectedId;
        AnnotationCanvas.Render(marks, Surface, ImageCanvas.ActualWidth);
    }

    /// <summary>
    /// Nothing to do: a flat image draws every mark it can at once, so one that is picked out is already
    /// as visible as it is going to get. The cube has a channel to go to; this does not.
    /// </summary>
    public void Reveal(Annotation mark) { }

    // ── What the page and the tab host call ────────────────────────────────────────────────────

    /// <summary>The file the marks on screen belong to.</summary>
    public string? AnnotationTarget => Target;

    /// <summary>Draw what is there. Called from every pan, zoom and rotation — marks move with the image.</summary>
    private void RenderAnnotations() => Marks.Render();

    /// <summary>Re-read and redraw after an agent has changed something.</summary>
    public bool RefreshAnnotations(string target, string? selectId) => Marks.Refresh(target, selectId);

    /// <summary>Let go of whatever mark is picked out.</summary>
    public bool DeselectAnnotation(string target) => Marks.Deselect(target);

    private bool TryBeginAnnotationGesture(Point at) => Marks.TryBegin(at.X, at.Y);

    private bool ContinueAnnotationGesture(Point at) => Marks.Continue(at.X, at.Y);

    private bool EndAnnotationGesture() => Marks.End();

    /// <summary>Remove the mark that is picked out, if there is one.</summary>
    public void DeleteSelectedMark() => Marks.DeleteSelected();

    /// <summary>Give up naming, keeping whatever is valid. Called when the pencil is put down.</summary>
    public void EndAnnotationEditing() => Marks.EndEditing();

    /// <summary>A double-press on a mark is the way to relabel one that is already there.</summary>
    private void OnCanvasDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var at = e.GetPosition(ImageCanvas);
        if (Marks.TryRelabelAt(at.X, at.Y)) e.Handled = true;
    }
}
