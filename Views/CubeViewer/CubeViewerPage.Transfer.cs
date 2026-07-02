using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Opacity transfer-function editor for volume mode: an interactive control-point curve
/// (drag / click-to-add / right-click-to-remove) feeding the renderer's alpha ramp via
/// <see cref="CubeVolumeRenderer.SetTransferFunction"/>. The Windows analogue of the macOS
/// <c>TransferFunctionEditor</c>; the edit rules live in the testable
/// <see cref="TransferFunctionModel"/>.
/// </summary>
public sealed partial class CubeViewerPage
{
    private readonly TransferFunctionModel _transfer = TransferFunctionModel.CreateDefault();
    private int _tfDrag = -1; // index of the point being dragged, or -1

    private const double TfHitRadiusPx = 9;

    private void OnTransferCanvasSizeChanged(object sender, SizeChangedEventArgs e) => DrawTransferEditor();

    private void OnTransferReset(object sender, RoutedEventArgs e)
    {
        _transfer.Reset();
        ApplyTransferFunction();
        DrawTransferEditor();
    }

    /// <summary>Canvas position → normalized (value, alpha) space (alpha increases upward).</summary>
    private (float X, float Y)? TfNorm(Point p)
    {
        double w = TransferCanvas.ActualWidth, h = TransferCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return null;
        return ((float)Math.Clamp(p.X / w, 0, 1), (float)Math.Clamp(1 - p.Y / h, 0, 1));
    }

    /// <summary>The pixel hit radius expressed as per-axis normalized radii (the canvas isn't square).</summary>
    private (float Rx, float Ry) TfHitRadii()
        => ((float)(TfHitRadiusPx / Math.Max(1, TransferCanvas.ActualWidth)),
            (float)(TfHitRadiusPx / Math.Max(1, TransferCanvas.ActualHeight)));

    private void OnTransferPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(TransferCanvas);
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (TfNorm(pt.Position) is not { } n) return;
        var (rx, ry) = TfHitRadii();
        // Grab the point under the cursor, or add a new one there and drag it immediately.
        if (_transfer.HitTest(n.X, n.Y, rx, ry) is { } hit)
        {
            _tfDrag = hit;
        }
        else
        {
            _tfDrag = _transfer.Add(n.X, n.Y);
            ApplyTransferFunction();
        }
        TransferCanvas.CapturePointer(e.Pointer);
        DrawTransferEditor();
        e.Handled = true;
    }

    private void OnTransferPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_tfDrag < 0) return;
        if (TfNorm(e.GetCurrentPoint(TransferCanvas).Position) is not { } n) return;
        _transfer.Drag(_tfDrag, n.X, n.Y);
        // Rebuilding the 256-entry ramp texture per move is tiny; the volume picks it up next frame.
        ApplyTransferFunction();
        DrawTransferEditor();
        e.Handled = true;
    }

    private void OnTransferPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_tfDrag < 0) return;
        _tfDrag = -1;
        TransferCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnTransferRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (TfNorm(e.GetPosition(TransferCanvas)) is not { } n) return;
        var (rx, ry) = TfHitRadii();
        if (_transfer.HitTest(n.X, n.Y, rx, ry) is { } hit && _transfer.Remove(hit))
        {
            ApplyTransferFunction();
            DrawTransferEditor();
            e.Handled = true;
        }
    }

    private void ApplyTransferFunction() => _renderer.SetTransferFunction(_transfer.Points);

    /// <summary>Redraw the curve: filled area under it, the line, and one handle per point.</summary>
    private void DrawTransferEditor()
    {
        TransferCanvas.Children.Clear();
        double w = TransferCanvas.ActualWidth, h = TransferCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var order = Enumerable.Range(0, _transfer.Points.Count)
            .OrderBy(i => _transfer.Points[i].X).ToArray();
        if (order.Length == 0) return;

        Point View(int i) => new(_transfer.Points[i].X * w, h * (1 - _transfer.Points[i].Y));

        // Filled area under the curve (slice-plane cyan, translucent).
        var area = new Polygon { Fill = ArgbBrush(0x30, 0x57, 0xC7, 0xFF) };
        var areaPts = new PointCollection { new Point(View(order[0]).X, h) };
        foreach (var i in order) areaPts.Add(View(i));
        areaPts.Add(new Point(View(order[^1]).X, h));
        area.Points = areaPts;
        TransferCanvas.Children.Add(area);

        // Curve line.
        var line = new Polyline { Stroke = ArgbBrush(0xFF, 0x57, 0xC7, 0xFF), StrokeThickness = 1.5 };
        var linePts = new PointCollection();
        foreach (var i in order) linePts.Add(View(i));
        line.Points = linePts;
        TransferCanvas.Children.Add(line);

        // Handles (endpoints outlined — they're pinned in value and can't be removed).
        for (int i = 0; i < _transfer.Points.Count; i++)
        {
            var p = View(i);
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = ArgbBrush(0xFF, 0x57, 0xC7, 0xFF),
                Stroke = _transfer.IsEndpoint(i) ? ArgbBrush(0xFF, 0xF0, 0xF0, 0xF0) : null,
                StrokeThickness = _transfer.IsEndpoint(i) ? 1.5 : 0,
            };
            Canvas.SetLeft(dot, p.X - 5);
            Canvas.SetTop(dot, p.Y - 5);
            TransferCanvas.Children.Add(dot);
        }
    }
}
