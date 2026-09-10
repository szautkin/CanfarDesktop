using Microsoft.UI.Xaml;
using Windows.Foundation;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// This cube, as <see cref="MarkEditor"/> sees it.
///
/// Everything a person does to a mark lives in that class, shared with the FITS viewer. What is here is
/// only what is genuinely different about a cube: two views of one set of marks, and a press that names
/// a RAY rather than a point.
///
/// A mark lives on a channel — the slice showing that channel draws it, the others do not, and the
/// volume draws all of them because that is what the volume is for. Neither view stores anything: both
/// ask a surface where a voxel lands, and the surface is the only thing that differs between them.
/// </summary>
public sealed partial class CubeViewerPage : IMarkCanvas
{
    private IAnnotationStore? _annotationStore;
    private MarkEditor? _marks;
    private Controls.MarkPanelBinding? _panelBinding;

    /// <summary>The marks in this cube. Built on first use, once the store has been attached.</summary>
    private MarkEditor Marks => _marks ??= new MarkEditor(
        this,
        _annotationStore,
        new SettingsMarkStylePreference(() => App.Services.GetService(typeof(ISettingsService)) as ISettingsService));

    public void AttachAnnotationStore(IAnnotationStore store) => _annotationStore = store;

    /// <summary>The pencil. While armed, a press draws rather than panning or orbiting.</summary>
    public bool DrawingArmed
    {
        get => Marks.DrawArmed;
        set => Marks.SetDrawArmed(value);
    }

    /// <summary>What a newly drawn mark will be.</summary>
    public AnnotationKind DrawingKind => Marks.Kind;

    // ── IMarkCanvas: the two things a cube does differently ────────────────────────────────────

    /// <summary>The cube whose marks these are — the answer <c>annotate_cube</c> needs.</summary>
    public string? Target => string.IsNullOrWhiteSpace(_cubePath) ? null : _cubePath;

    /// <summary>
    /// The surface of whichever view is on screen.
    ///
    /// Every gesture goes through this rather than naming a view. The two surfaces already answer the
    /// same three questions — where a voxel lands, how big a unit is, how much bigger than the screen
    /// this rendering is — so the code that moves and resizes marks never needs to know which it has.
    /// </summary>
    public IAnnotationSurface Surface
        => ViewModel.ViewMode == CubeViewMode.Slice ? SliceSurface() : VolumeSurface();

    public IMarkLabelField Label => MarkEditorField;

    /// <summary>
    /// A press is measured against whichever view is showing; the field hangs off the PAGE, because the
    /// slice is Collapsed while the volume shows and a field parented there could never appear over it.
    /// One translation, so the same call works in both.
    /// </summary>
    public (double X, double Y) ToLabelHost(double x, double y)
    {
        var view = ViewModel.ViewMode == CubeViewMode.Slice ? (FrameworkElement)SliceViewport : RenderPanel;
        var onPage = view.TransformToVisual(PageRoot).TransformPoint(new Point(x, y));
        return (onPage.X, onPage.Y);
    }

    /// <summary>Run something after the current input event has finished being handled.</summary>
    public void Later(Action what) => DispatcherQueue.TryEnqueue(() => what());

    /// <summary>The volume view's surface for the current camera.</summary>
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
    /// The voxel a press is over, in whichever view is on screen.
    ///
    /// The two views answer this differently and there is no way around that: a press on the slice names
    /// one voxel outright, while a press on the volume names a RAY and the channel has to supply the
    /// depth — met with the plane the volume already draws as the slice-plane marker.
    ///
    /// A mark being MOVED keeps its own channel rather than jumping to whichever one the scrubber shows,
    /// so dragging a mark sideways does not quietly move it through the cube.
    /// </summary>
    public AnnotationAnchor? AnchorFor(double x, double y, Annotation? moving)
    {
        var voxel = ViewModel.ViewMode == CubeViewMode.Slice
            ? VoxelOnSlice(new Point(x, y))
            : VolumeSurface().VoxelAt(x, y, ViewModel.Channel);

        if (voxel is null) return null;
        if (moving is null) return voxel;

        var kept = AnnotationAnchor.Data(voxel.X, voxel.Y, moving.Anchor.Z);
        return kept.IsValid ? kept : null;
    }

    /// <summary>
    /// The voxel a press on the slice is over, on the channel currently shown. Null in the letterbox
    /// margin, where there is no data under the pointer.
    /// </summary>
    private AnnotationAnchor? VoxelOnSlice(Point at)
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

    /// <summary>
    /// Both views are told, every time. They are one set of marks seen two ways, and drawing only the
    /// one on screen would leave the other stale the moment the mode changed.
    /// </summary>
    public void Draw(IReadOnlyList<Annotation> marks, string? selectedId, string? editingId)
    {
        SliceAnnotationCanvas.EditingId = VolumeAnnotationCanvas.EditingId = editingId;
        SliceAnnotationCanvas.SelectedId = VolumeAnnotationCanvas.SelectedId = selectedId;

        if (ViewModel.ViewMode == CubeViewMode.Slice)
            SliceAnnotationCanvas.Render(marks, SliceSurface(), SliceViewport.ActualWidth);
        else
            VolumeAnnotationCanvas.Render(marks, VolumeSurface(), RenderPanel.ActualWidth);
    }

    /// <summary>
    /// Go to a mark's CHANNEL, since that is what decides whether the slice draws it at all.
    ///
    /// Panning to centre it as well would fight whatever the person had lined up; the channel is the one
    /// part they cannot recover by looking.
    /// </summary>
    public void Reveal(Annotation mark)
    {
        if (mark.Anchor.Space != AnchorSpace.Data) return;

        // Away-from-zero, matching the slice surface's own channel test: banker's rounding sends channel
        // 16.5 to 16 there and 16 here, or the mark is shown on a slice that does not draw it.
        var channel = (int)Math.Round(mark.Anchor.Z, MidpointRounding.AwayFromZero);
        ViewModel.Channel = Math.Clamp(channel, 0, Math.Max(0, (_volume?.Nz ?? 1) - 1));
    }

    // ── What the page calls ────────────────────────────────────────────────────────────────────

    /// <summary>The cube the marks on screen belong to.</summary>
    public string? AnnotationTarget => Target;

    private void RenderAnnotations() => Marks.Render();

    /// <summary>Re-read and redraw after an agent has changed something.</summary>
    public bool RefreshAnnotations(string target, string? selectId) => Marks.Refresh(target, selectId);

    private bool TryBeginAnnotationGesture(Point at) => Marks.TryBegin(at.X, at.Y);

    private bool ContinueAnnotationGesture(Point at) => Marks.Continue(at.X, at.Y);

    private bool EndAnnotationGesture() => Marks.End();

    /// <summary>Remove the mark that is picked out, if there is one.</summary>
    public void DeleteSelectedMark() => Marks.DeleteSelected();

    /// <summary>Give up naming, keeping whatever is valid. Called when the pencil is put down.</summary>
    public void EndAnnotationEditing() => Marks.EndEditing();

    /// <summary>A double-press on a mark relabels it — but only when it is not the reset gesture.</summary>
    private bool TryRelabelAt(Point at) => Marks.TryRelabelAt(at.X, at.Y);
}
