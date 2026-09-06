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
        var fit = Math.Min(_viewportWidth / _displayNx, _viewportHeight / _displayNy);
        var originX = (_viewportWidth - _displayNx * fit) / 2;
        var originY = (_viewportHeight - _displayNy * fit) / 2;

        var fx = originX + dx * fit;
        var fy = originY + dy * fit;

        var centreX = _viewportWidth / 2;
        var centreY = _viewportHeight / 2;

        return (centreX + (fx - centreX) * _zoom + _panX,
                centreY + (fy - centreY) * _zoom + _panY);
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
