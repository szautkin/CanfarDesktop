using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// The volume view, as the annotation renderer sees it.
///
/// A cube mark is a point in the box, so it projects with the same camera the wireframe uses — and a
/// mark behind the camera is not drawn rather than drawn at the edge, which is what the wireframe does
/// with an edge whose endpoint is behind.
///
/// A circle here is a sphere: it is drawn as an ellipse of the projected radius, which is what a sphere
/// looks like from off-axis, rather than a ring that stays circular and stops belonging to the volume.
/// </summary>
public sealed class CubeVolumeAnnotationSurface : IAnnotationSurface
{
    private readonly CubeProjector? _projector;
    private readonly int _nx, _ny, _nz;

    public CubeVolumeAnnotationSurface(CubeProjector? projector, int nx, int ny, int nz)
    {
        _projector = projector;
        _nx = nx;
        _ny = ny;
        _nz = nz;
    }

    public double InkScale { get; init; } = 1.0;

    public (double X, double Y)? Project(AnnotationAnchor anchor)
    {
        // Only cube marks belong in a cube. An image-pixel or sky anchor is another viewer's — skipped,
        // never clamped, because a clamped mark points at the wrong thing.
        if (_projector is null || anchor.Space != AnchorSpace.Data || !anchor.IsValid) return null;

        var at = _projector.ProjectVoxel(anchor.X, anchor.Y, anchor.Z, _nx, _ny, _nz);
        return at.Visible ? (at.X, at.Y) : null;
    }

    /// <summary>
    /// The inverse of <see cref="Project"/> for one channel: which voxel a press is over.
    ///
    /// A press on a perspective view names a ray, so the channel supplies the depth — the ray is met
    /// with the plane the view already draws as the slice-plane marker. Null when the press is not over
    /// the box at all.
    ///
    /// Here rather than in the page, because this class already holds the projector and the cube's
    /// dimensions; a caller that assembled them again would be a second place to get them wrong.
    /// </summary>
    public AnnotationAnchor? VoxelAt(double screenX, double screenY, int channel)
    {
        if (_projector is null) return null;
        if (_projector.VoxelOnChannelPlane(screenX, screenY, channel, _nx, _ny, _nz) is not { } voxel) return null;

        var anchor = AnnotationAnchor.Data(voxel.X, voxel.Y, channel);
        return anchor.IsValid ? anchor : null;
    }

    /// <summary>
    /// Measured, not derived: project the anchor and a voxel away from it, and take the distance.
    ///
    /// It has to be measured here more than anywhere. The view is perspective, so a voxel near the
    /// camera covers more screen than the same voxel at the back of the box — a single scale for the
    /// whole cube would draw the far marks too big and the near ones too small, at every camera angle
    /// except straight on.
    /// </summary>
    public double UnitsToPixels(AnnotationAnchor anchor)
    {
        if (Project(anchor) is not { } here) return 1.0;

        var stepped = AnnotationAnchor.Data(anchor.X + 1, anchor.Y, anchor.Z);
        if (Project(stepped) is not { } there) return 1.0;

        var span = Math.Sqrt(Math.Pow(there.X - here.X, 2) + Math.Pow(there.Y - here.Y, 2));
        return double.IsFinite(span) && span > 0 ? span : 1.0;
    }
}

/// <summary>
/// The slice view, as the annotation renderer sees it: a flat image with a fit, a zoom and a pan.
///
/// <b>A mark belongs to its channel.</b> One anchored on channel 17 is not on the surface showing
/// channel 18 — <see cref="Project"/> answers null for it, and the layer skips what it cannot place.
/// Drawing every mark on every slice would make a cube with marks on forty channels unreadable, and
/// would say something false: the mark is about that channel.
/// </summary>
public sealed class CubeSliceAnnotationSurface : IAnnotationSurface
{
    private readonly int _channel;
    private readonly int _volumeNx, _volumeNy;
    private readonly int _displayNx, _displayNy;
    private readonly double _viewportWidth, _viewportHeight;
    private readonly double _zoom, _panX, _panY;

    public CubeSliceAnnotationSurface(
        int channel,
        int volumeNx, int volumeNy,
        int displayNx, int displayNy,
        double viewportWidth, double viewportHeight,
        double zoom, double panX, double panY)
    {
        _channel = channel;
        _volumeNx = volumeNx;
        _volumeNy = volumeNy;
        _displayNx = displayNx > 0 ? displayNx : volumeNx;
        _displayNy = displayNy > 0 ? displayNy : volumeNy;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _zoom = zoom;
        _panX = panX;
        _panY = panY;
    }

    public double InkScale { get; init; } = 1.0;

    /// <summary>
    /// The aspect-fit factor: viewport pixels per DISPLAY pixel before the zoom.
    ///
    /// One definition, used by the forward mapping and by its inverse, so the two cannot drift apart.
    /// Zero when anything needed is not a usable measurement.
    /// </summary>
    private double Fit
        => _viewportWidth > 0 && _viewportHeight > 0 && _displayNx > 0 && _displayNy > 0
            ? Math.Min(_viewportWidth / _displayNx, _viewportHeight / _displayNy)
            : 0;

    /// <summary>Where the fitted plane starts inside the viewport — the letterbox margin.</summary>
    private double OriginX(double fit) => (_viewportWidth - _displayNx * fit) / 2;
    private double OriginY(double fit) => (_viewportHeight - _displayNy * fit) / 2;

    /// <summary>How many viewport pixels one DISPLAY pixel of the slice spans, after the fit and the zoom.</summary>
    private double DisplayScale
    {
        get
        {
            if (_viewportWidth <= 0 || _viewportHeight <= 0 || _displayNx <= 0 || _displayNy <= 0) return 0;
            return Math.Min(_viewportWidth / _displayNx, _viewportHeight / _displayNy) * _zoom;
        }
    }

    /// <summary>
    /// The slice is rendered from a DOWN-SAMPLED volume, so a voxel and a displayed pixel are not the
    /// same thing. Anchors are in the cube's own voxels — that is what an agent is given and what
    /// survives a re-render at another resolution — so they are converted here rather than stored
    /// pre-scaled.
    /// </summary>
    private (double X, double Y) VoxelToDisplay(double x, double y)
        => (_volumeNx > 0 ? x * _displayNx / _volumeNx : x,
            _volumeNy > 0 ? y * _displayNy / _volumeNy : y);

    public (double X, double Y)? Project(AnnotationAnchor anchor)
    {
        if (anchor.Space != AnchorSpace.Data || !anchor.IsValid) return null;

        // A mark belongs to its channel. Rounded rather than truncated: a mark placed at 16.5 belongs to
        // 17 as much as to 16, and the scrubber only ever sits on whole channels. AwayFromZero, because
        // the default is banker's rounding — which would send 16.5 down and 17.5 up, and "it depends
        // whether the channel is even" is not an explanation anyone should have to hear.
        if ((int)Math.Round(anchor.Z, MidpointRounding.AwayFromZero) != _channel) return null;

        var scale = DisplayScale;
        if (scale <= 0) return null;

        var (dx, dy) = VoxelToDisplay(anchor.X, anchor.Y);

        // Undo of MapToPixel: fit-space, then the zoom about the viewport centre, then the pan.
        var fit = Fit;
        var fx = OriginX(fit) + dx * fit;
        var fy = OriginY(fit) + dy * fit;

        var centreX = _viewportWidth / 2;
        var centreY = _viewportHeight / 2;

        return (centreX + (fx - centreX) * _zoom + _panX,
                centreY + (fy - centreY) * _zoom + _panY);
    }

    /// <summary>
    /// The voxel a point on the viewport is over — the exact inverse of <see cref="Project"/>, on the
    /// channel this surface is showing.
    ///
    /// <para>CONTINUOUS, and that is the point. A voxel index is a whole number, but a mark's position
    /// is not: the anchor holds doubles precisely so a mark can sit between voxels. The viewer used to
    /// answer this by reusing the pixel readout's mapping, which floors to a whole display pixel and
    /// then floors again to a whole voxel — so a drag could only ever put a mark on an integer voxel.
    /// On a cube down-sampled to a couple of voxels across, that is a handful of places a mark is
    /// allowed to be, and dragging it looked broken: nothing for a long sweep of the pointer, then a
    /// jump of a whole voxel. Zooming in made the dead zone wider and zooming out made the jumps
    /// wilder, which is exactly how it was reported.</para>
    ///
    /// <para>Here, beside the forward mapping, because the two have to cancel. Kept apart they drifted
    /// — the readout's version quantises deliberately, and borrowing it for a drag inherited a
    /// rounding that placement must not have. The readout still has its own: a pixel probe genuinely
    /// wants a whole pixel.</para>
    /// </summary>
    public AnnotationAnchor? VoxelAt(double screenX, double screenY)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY)) return null;

        var fit = Fit;
        if (fit <= 0 || _zoom <= 0) return null;

        // Undo Project, in the order it applied things: the zoom about the viewport centre and the
        // pan, then the aspect-fit origin, then the display-pixel-to-voxel scaling.
        var centreX = _viewportWidth / 2;
        var centreY = _viewportHeight / 2;

        var fx = centreX + (screenX - _panX - centreX) / _zoom;
        var fy = centreY + (screenY - _panY - centreY) / _zoom;

        var dx = (fx - OriginX(fit)) / fit;
        var dy = (fy - OriginY(fit)) / fit;

        var vx = _displayNx > 0 ? dx * _volumeNx / _displayNx : dx;
        var vy = _displayNy > 0 ? dy * _volumeNy / _displayNy : dy;

        // Outside the plane is the letterbox margin, where there is no data under the pointer.
        if (vx < 0 || vy < 0 || vx > _volumeNx || vy > _volumeNy) return null;

        var anchor = AnnotationAnchor.Data(vx, vy, _channel);
        return anchor.IsValid ? anchor : null;
    }

    /// <summary>
    /// One voxel, in viewport pixels. Derived rather than measured here: the mapping is a plain
    /// similarity — a fit, a zoom and a pan — so the two agree exactly, and deriving it stays right at
    /// the edge of the frame where a stepped point would fall outside the slice.
    /// </summary>
    public double UnitsToPixels(AnnotationAnchor anchor)
    {
        var scale = DisplayScale * (_volumeNx > 0 ? (double)_displayNx / _volumeNx : 1);
        return double.IsFinite(scale) && scale > 0 ? scale : 1.0;
    }
}
