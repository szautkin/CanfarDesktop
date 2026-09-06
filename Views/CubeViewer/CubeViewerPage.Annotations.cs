using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Marks in the cube: on the slice, and in the volume.
///
/// The two views are one set of marks seen two ways. A mark lives on a channel — the slice showing that
/// channel draws it, the others do not, and the volume draws all of them because that is what the
/// volume is for. Neither view stores anything: both ask a surface where a voxel lands, and the surface
/// is the only thing that differs between them.
///
/// Drawing and editing happen on the SLICE. Placing a mark in a perspective view means choosing a depth
/// from a flat press, and every answer to that is a guess; the slice already has a channel, so a press
/// there means exactly one voxel.
/// </summary>
public sealed partial class CubeViewerPage
{
    private IAnnotationStore? _annotationStore;
    private List<Annotation> _annotations = [];
    private string? _loadedAnnotationTarget;

    /// <summary>The pencil. While armed, a press on the slice draws rather than panning.</summary>
    public bool DrawingArmed { get; set; }

    /// <summary>What a newly drawn mark will be.</summary>
    public AnnotationKind DrawingKind { get; set; } = AnnotationKind.Circle;

    private string? _editingId;
    private string? _selectedId;
    private MarkGrab _grab = new MarkGrab.None();

    public void AttachAnnotationStore(IAnnotationStore store) => _annotationStore = store;

    /// <summary>The cube whose marks these are — the answer <c>annotate_cube</c> needs.</summary>
    public string? AnnotationTarget => string.IsNullOrWhiteSpace(_cubePath) ? null : _cubePath;

    /// <summary>The volume view's surface for the current camera, or null before the panel has a size.</summary>
    private CubeVolumeAnnotationSurface VolumeSurface()
    {
        var projector = _volume is null
            ? null
            : CubeProjector.Create(
                ViewModel.CameraAzimuth, ViewModel.CameraElevation, ViewModel.CameraDistance,
                ViewModel.SpectralScale, _volNx, _volNy,
                RenderPanel.ActualWidth, RenderPanel.ActualHeight);

        return new CubeVolumeAnnotationSurface(projector, _volume?.Nx ?? 1, _volume?.Ny ?? 1, _volume?.Nz ?? 1);
    }

    /// <summary>The slice view's surface for the channel on screen.</summary>
    private CubeSliceAnnotationSurface SliceSurface() => new(
        ViewModel.Channel,
        _volume?.Nx ?? 1, _volume?.Ny ?? 1,
        _sliceDispNx, _sliceDispNy,
        SliceViewport.ActualWidth, SliceViewport.ActualHeight,
        _sliceZoom, _slicePanX, _slicePanY);

    /// <summary>
    /// Load this cube's marks if the file has changed under us, then draw both views.
    ///
    /// Called from the volume overlay's own update and from every slice render, which between them
    /// cover every camera move, channel change, zoom and pan — so nothing has to remember to ask.
    /// </summary>
    private void RenderAnnotations()
    {
        EnsureAnnotationsLoaded();

        VolumeAnnotationCanvas.EditingId = SliceAnnotationCanvas.EditingId = _editingId;
        VolumeAnnotationCanvas.SelectedId = SliceAnnotationCanvas.SelectedId = _selectedId;

        // Each view is drawn only while it is the one on screen: rendering the other's marks into a
        // hidden canvas is work with nothing to show for it, on a panel that repaints at 60fps.
        if (ViewModel.ViewMode == CubeViewMode.Slice)
            SliceAnnotationCanvas.Render(_annotations, SliceSurface(), SliceViewport.ActualWidth);
        else
            VolumeAnnotationCanvas.Render(_annotations, VolumeSurface(), RenderPanel.ActualWidth);
    }

    private void EnsureAnnotationsLoaded()
    {
        var target = AnnotationTarget;
        if (string.Equals(target, _loadedAnnotationTarget, StringComparison.Ordinal)) return;

        _loadedAnnotationTarget = target;
        _editingId = _selectedId = null;
        _annotations = target is null || _annotationStore is null
            ? []
            : _annotationStore.LoadFor(target).ToList();
    }

    /// <summary>Re-read and redraw after something changed the marks elsewhere.</summary>
    public bool RefreshAnnotations(string target, string? selectId)
    {
        if (AnnotationTarget is not { } mine || !string.Equals(mine, target, StringComparison.OrdinalIgnoreCase))
            return false;

        _annotations = _annotationStore?.LoadFor(target).ToList() ?? [];
        if (selectId is not null) _selectedId = selectId;

        RenderAnnotations();
        return true;
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
            System.Diagnostics.Debug.WriteLine($"Could not save annotations for {target}: {ex.Message}");
        }
    }

    // ── Drawing, on the slice ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the marks want this press. True means the slice must not pan.
    ///
    /// Asked before the pan handling, for the reason the FITS canvas asks first: a press that takes hold
    /// of a mark and also starts a pan drags the image out from under the mark being moved.
    /// </summary>
    private bool TryBeginAnnotationGesture(Windows.Foundation.Point at)
    {
        if (AnnotationTarget is null || _volume is null) return false;

        var surface = SliceSurface();
        _grab = AnnotationGeometry.GrabAt(_annotations, surface, _editingId, DrawingArmed, at.X, at.Y);

        switch (_grab)
        {
            case MarkGrab.None:
                return false;

            case MarkGrab.Place:
                if (VoxelAt(at) is not { } anchor) return false;

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

                _annotations.Add(mark);
                _editingId = _selectedId = mark.Id;
                _grab = mark.Extent is null ? new MarkGrab.None() : new MarkGrab.Resize(mark.Id);
                RenderAnnotations();

                if (mark.Kind is AnnotationKind.Callout or AnnotationKind.Text)
                    DispatcherQueue.TryEnqueue(() => BeginLabelEdit(mark.Id));

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

    private bool ContinueAnnotationGesture(Windows.Foundation.Point at)
    {
        var surface = SliceSurface();

        switch (_grab)
        {
            case MarkGrab.Move move:
            {
                var index = _annotations.FindIndex(a => a.Id == move.Id);
                if (index < 0) return false;

                var target = new Windows.Foundation.Point(at.X - move.GrabDx, at.Y - move.GrabDy);
                if (VoxelAt(target) is not { } anchor) return true;

                // The channel is the mark's, not the scrubber's: dragging a mark sideways must not move
                // it to whichever channel happens to be on screen.
                _annotations[index] = _annotations[index] with
                {
                    Anchor = AnnotationAnchor.Data(anchor.X, anchor.Y, _annotations[index].Anchor.Z),
                };
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

    private bool EndAnnotationGesture()
    {
        if (_grab is MarkGrab.None) return false;

        _grab = new MarkGrab.None();
        _annotations.RemoveAll(a => a.Validate() is not null);
        SaveAnnotations();
        RenderAnnotations();
        return true;
    }

    /// <summary>
    /// The voxel a press on the slice is over, on the channel currently shown. Null in the letterbox
    /// margin, where there is no data under the pointer.
    /// </summary>
    private AnnotationAnchor? VoxelAt(Windows.Foundation.Point at)
    {
        if (_volume is null || MapToPixel(at) is not { } display) return null;

        // MapToPixel answers in the slice's DISPLAY pixels, which are the down-sampled volume's; the
        // anchor is in the cube's own voxels, so it goes back through the same mapping the readout uses.
        var voxel = AnnotationAnchor.Data(
            MapDispToVolume(display.x, _sliceDispNx, _volume.Nx),
            MapDispToVolume(display.y, _sliceDispNy, _volume.Ny),
            ViewModel.Channel);

        return voxel.IsValid ? voxel : null;
    }

    /// <summary>Remove the mark that is picked out, if there is one.</summary>
    public void DeleteSelectedMark()
    {
        if (_selectedId is null) return;

        _annotations.RemoveAll(a => a.Id == _selectedId);
        _selectedId = _editingId = null;
        SaveAnnotations();
        RenderAnnotations();
    }

    /// <summary>Give up editing, keeping whatever is valid. Called when the pencil is put down.</summary>
    public void EndAnnotationEditing()
    {
        _editingId = null;
        _annotations.RemoveAll(a => a.Validate() is not null);
        SaveAnnotations();
        RenderAnnotations();
    }

    /// <summary>Type a mark's label, in a field over the mark itself.</summary>
    private void BeginLabelEdit(string id)
    {
        var mark = _annotations.FirstOrDefault(a => a.Id == id);
        if (mark is null) return;
        if (SliceSurface().Project(mark.Anchor) is not { } at) return;

        var field = new TextBox { Text = mark.Text, Width = 220, PlaceholderText = "Label", AcceptsReturn = false };
        var flyout = new Flyout { Content = field, ShouldConstrainToRootBounds = false };

        void Commit()
        {
            var index = _annotations.FindIndex(a => a.Id == id);
            if (index >= 0) _annotations[index] = _annotations[index] with { Text = field.Text.Trim() };

            _editingId = null;
            _annotations.RemoveAll(a => a.Validate() is not null);
            SaveAnnotations();
            RenderAnnotations();
        }

        field.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) flyout.Hide();
        };
        flyout.Closed += (_, _) => Commit();

        _editingId = id;
        RenderAnnotations();

        flyout.ShowAt(SliceViewport, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(at.X, at.Y),
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
        });

        field.Focus(FocusState.Programmatic);
        field.SelectAll();
    }

    /// <summary>A double-press on a mark relabels it — but only when it is not the reset gesture.</summary>
    private bool TryRelabelAt(Windows.Foundation.Point at)
    {
        if (AnnotationTarget is null) return false;
        if (AnnotationGeometry.AnnotationAt(_annotations, SliceSurface(), at.X, at.Y) is not { } id) return false;

        _selectedId = id;
        BeginLabelEdit(id);
        return true;
    }
}
