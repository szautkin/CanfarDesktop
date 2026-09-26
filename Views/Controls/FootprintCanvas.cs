using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;
using WinPoint = Windows.Foundation.Point;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// A file's footprint as a sky chart — north up, east left, true shape at any Dec — with a cutout
/// region over it that can be dragged, resized by its handle, or drawn anew.
///
/// <para>One control for both uses: read-only, it is the observation view's footprint sketch (which
/// used to scale RA and Dec separately and so drew every field the wrong shape); editable, it is the
/// cutout editor's drawing. The region is the editor's — this reports changes through
/// <see cref="RegionChanged"/> and is told the result, so the fields and the drawing never disagree.</para>
///
/// <para>Scaled to the footprint (and the search's target, when it is shown), never to the region, so
/// the view does not lurch while a region is dragged; a region reaching past the edge is clipped.</para>
/// </summary>
public sealed class FootprintCanvas : UserControl
{
    private enum Drag { None, Move, Resize, Draw }

    /// <summary>Within this many pixels of the handle, a press resizes rather than moves.</summary>
    private const double HandleReach = 10;

    /// <summary>A press that moves less than this is a click: it recentres the region where it lands.</summary>
    private const double ClickSlop = 3;

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private SkyCanvasProjection? _projection;
    private IReadOnlyList<SkyPoint> _footprint = [];
    private SkyPoint? _target;
    private SkyRegion? _region;

    private Drag _drag;
    private WinPoint _pressAt;
    private SkyPoint _pressSky;
    private SkyRegion? _pressRegion;

    public FootprintCanvas()
    {
        Content = new Border
        {
            Child = _canvas,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = Res("CardStrokeColorDefaultBrush"),
            Background = Res("CardBackgroundFillColorSecondaryBrush"),
        };

        _canvas.SizeChanged += (_, e) =>
        {
            _canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            Refit();
        };
        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += OnReleased;
        _canvas.PointerCaptureLost += (_, _) => _drag = Drag.None;
    }

    /// <summary>Whether the region can be dragged and drawn; off, this is a picture.</summary>
    public bool IsEditable { get; set; }

    /// <summary>The shape a fresh drag draws.</summary>
    public SkyShape DrawShape { get; set; } = SkyShape.Circle;

    /// <summary>Raised as the person drags, with the region as it now stands.</summary>
    public event Action<SkyRegion>? RegionChanged;

    public IReadOnlyList<SkyPoint> Footprint
    {
        get => _footprint;
        set { _footprint = value; Refit(); }
    }

    /// <summary>Where the search looked, marked with a cross.</summary>
    public SkyPoint? Target
    {
        get => _target;
        set { _target = value; Refit(); }
    }

    /// <summary>The region shown. Setting it redraws; only a drag raises <see cref="RegionChanged"/>.</summary>
    public SkyRegion? Region
    {
        get => _region;
        set { _region = value; Redraw(); }
    }

    private void Refit()
    {
        var (w, h) = (_canvas.ActualWidth, _canvas.ActualHeight);
        if (w <= 0 || h <= 0 || _footprint.Count == 0)
        {
            _projection = null;
            Redraw();
            return;
        }

        var fit = _footprint.ToList();
        if (_target is { } t) fit.Add(t);
        _projection = new SkyCanvasProjection(fit, w, h, padding: 0.12);
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        if (_projection is not { } p) return;

        if (Outline(p, _footprint) is { } footprint)
        {
            footprint.Stroke = Res("AccentFillColorDefaultBrush");
            footprint.StrokeThickness = 1.5;
            footprint.Fill = Res("SubtleFillColorSecondaryBrush");
            _canvas.Children.Add(footprint);
        }

        AddCompass();

        if (_target is { } target && p.ToCanvas(target) is { } tc)
        {
            var brush = Res("TextFillColorSecondaryBrush");
            _canvas.Children.Add(new Line { X1 = tc.X - 6, Y1 = tc.Y, X2 = tc.X + 6, Y2 = tc.Y, Stroke = brush, StrokeThickness = 1.5 });
            _canvas.Children.Add(new Line { X1 = tc.X, Y1 = tc.Y - 6, X2 = tc.X, Y2 = tc.Y + 6, Stroke = brush, StrokeThickness = 1.5 });
        }

        if (_region is { } region && region.Problem() is null && Outline(p, region.Outline()) is { } shape)
        {
            shape.Stroke = Res("SystemFillColorCautionBrush");
            shape.StrokeThickness = 2;
            shape.Fill = Res("SubtleFillColorTertiaryBrush");
            _canvas.Children.Add(shape);

            if (IsEditable && Handle(region) is { } handle && p.ToCanvas(handle) is { } hc)
            {
                var dot = new Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = Res("SystemFillColorCautionBrush"),
                    Stroke = Res("TextOnAccentFillColorPrimaryBrush"),
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(dot, hc.X - 5);
                Canvas.SetTop(dot, hc.Y - 5);
                _canvas.Children.Add(dot);
            }
        }
    }

    /// <summary>"N ↑  E ←" in a corner: the chart's orientation is the one thing a sky picture must say.</summary>
    private void AddCompass()
    {
        var label = new TextBlock
        {
            Text = "N ↑  E ←",
            FontSize = 10,
            Foreground = Res("TextFillColorTertiaryBrush"),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, 6);
        Canvas.SetTop(label, 4);
        _canvas.Children.Add(label);
    }

    private static Polygon? Outline(SkyCanvasProjection p, IReadOnlyList<SkyPoint> sky)
    {
        if (sky.Count < 3) return null;
        var points = new PointCollection();
        foreach (var v in sky)
        {
            if (p.ToCanvas(v) is not { } c) return null;
            points.Add(new WinPoint(c.X, c.Y));
        }
        return new Polygon { Points = points, IsHitTestVisible = false };
    }

    /// <summary>
    /// Where the resize handle sits: a circle's west edge, a box's south-west corner — both at the
    /// lower right of a sky chart, where a hand reaches for one.
    /// </summary>
    private static SkyPoint? Handle(SkyRegion region) => region.Shape switch
    {
        SkyShape.Circle => SkyGeometry.Unproject(region.Centre, -region.Radius, 0),
        SkyShape.Box => region.Outline()[1],
        _ => null,
    };

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEditable || _projection is not { } p) return;

        _pressAt = e.GetCurrentPoint(_canvas).Position;
        _pressSky = p.ToSky(_pressAt.X, _pressAt.Y);
        _pressRegion = _region;

        _drag = _region is { } r && r.Problem() is null && Near(p, Handle(r), _pressAt) ? Drag.Resize
              : _region is { } inside && inside.Problem() is null && SkyGeometry.Contains(inside.Outline(), _pressSky) ? Drag.Move
              : Drag.Draw;

        _canvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Drag.None || _projection is not { } p) return;

        var at = e.GetCurrentPoint(_canvas).Position;
        if (Math.Abs(at.X - _pressAt.X) < ClickSlop && Math.Abs(at.Y - _pressAt.Y) < ClickSlop) return;

        var sky = p.ToSky(at.X, at.Y);
        var next = _drag switch
        {
            Drag.Move when _pressRegion is { } from => Moved(from, _pressSky, sky),
            Drag.Resize when _pressRegion is { } from => Resized(from, sky),
            _ => Drawn(_pressSky, sky),
        };
        if (next is null) return;

        _region = next;
        Redraw();
        RegionChanged?.Invoke(next);
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Drag.None) return;

        // A click rather than a drag: bring the region to where it landed, its size kept.
        var at = e.GetCurrentPoint(_canvas).Position;
        if (_drag == Drag.Draw && _pressRegion is { } region && region.Problem() is null
            && Math.Abs(at.X - _pressAt.X) < ClickSlop && Math.Abs(at.Y - _pressAt.Y) < ClickSlop
            && Moved(region, region.Centre, _pressSky) is { } recentred)
        {
            _region = recentred;
            Redraw();
            RegionChanged?.Invoke(recentred);
        }

        _drag = Drag.None;
        _canvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private static bool Near(SkyCanvasProjection p, SkyPoint? sky, WinPoint at)
        => sky is { } s && p.ToCanvas(s) is { } c
           && Math.Abs(c.X - at.X) <= HandleReach && Math.Abs(c.Y - at.Y) <= HandleReach;

    /// <summary>The same shape shifted by the drag, through the tangent plane about its own centre.</summary>
    private static SkyRegion? Moved(SkyRegion from, SkyPoint start, SkyPoint now)
    {
        var centre = from.Centre;
        if (SkyGeometry.Project(centre, start) is not { } a || SkyGeometry.Project(centre, now) is not { } b) return null;
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);

        return from.Shape switch
        {
            SkyShape.Polygon => SkyRegion.Polygon(from.Vertices
                .Select(v => SkyGeometry.Project(centre, v) is { } q ? SkyGeometry.Unproject(centre, q.X + dx, q.Y + dy) : v)),
            _ => ShiftCentre(from, SkyGeometry.Unproject(centre, dx, dy)),
        };
    }

    private static SkyRegion ShiftCentre(SkyRegion from, SkyPoint to) => from.Shape == SkyShape.Circle
        ? SkyRegion.Circle(to.Ra, to.Dec, from.Radius)
        : SkyRegion.Box(to.Ra, to.Dec, from.Width, from.Height);

    private static SkyRegion? Resized(SkyRegion from, SkyPoint handle)
    {
        var centre = from.Centre;
        switch (from.Shape)
        {
            case SkyShape.Circle:
                return SkyRegion.Circle(centre.Ra, centre.Dec, Math.Max(SkyGeometry.Distance(centre, handle), 1.0 / 3600));
            case SkyShape.Box when SkyGeometry.Project(centre, handle) is { } q:
                return SkyRegion.Box(centre.Ra, centre.Dec, Math.Max(2 * Math.Abs(q.X), 1.0 / 3600), Math.Max(2 * Math.Abs(q.Y), 1.0 / 3600));
            default:
                return null;
        }
    }

    /// <summary>A fresh region from the press to here: a circle about the press, or a box corner to corner.</summary>
    private SkyRegion? Drawn(SkyPoint start, SkyPoint now)
    {
        if (DrawShape == SkyShape.Circle)
            return SkyRegion.Circle(start.Ra, start.Dec, Math.Max(SkyGeometry.Distance(start, now), 1.0 / 3600));

        if (SkyGeometry.Project(start, now) is not { } q) return null;
        var middle = SkyGeometry.Unproject(start, q.X / 2, q.Y / 2);
        return SkyRegion.Box(middle.Ra, middle.Dec, Math.Max(Math.Abs(q.X), 1.0 / 3600), Math.Max(Math.Abs(q.Y), 1.0 / 3600));
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
