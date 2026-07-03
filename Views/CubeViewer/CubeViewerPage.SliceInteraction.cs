using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Slice-view navigation + hover readout: wheel-zoom toward the cursor, drag-pan,
/// double-tap reset, and a floating cursor chip (voxel value + sky coordinates +
/// spectral value) with a persistent coordinate bar. The Windows analogue of the
/// macOS <c>CubeSliceView</c> gestures + cursorChip/coordinateBar.
/// </summary>
public sealed partial class CubeViewerPage
{
    // Zoom/pan state for the slice viewport. The transform is view = center + (fit − center)·zoom
    // + pan (a CompositeTransform scaling around the viewport center, then translating), matching
    // the macOS scaleEffect + offset order — MapToPixel inverts the same equation.
    private double _sliceZoom = 1;
    private double _slicePanX, _slicePanY;
    private bool _slicePressed, _slicePanning;
    private Point _slicePressOrigin, _sliceLastDrag;

    private const double MaxSliceZoom = 20;      // macOS MagnificationGesture cap
    private const double SlicePanThresholdPx = 6; // below this a press+release is a probe click
    private static string CoordBarHint => Helpers.Loc.T("Cube_CoordBarHint");

    // ── Viewport sizing ────────────────────────────────────────────────────────

    private void OnSliceViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The zoomed slice must not spill over the surrounding panels, and the transform's
        // scale center must track the viewport center (the pan math assumes it).
        SliceViewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        SliceTransform.CenterX = e.NewSize.Width / 2;
        SliceTransform.CenterY = e.NewSize.Height / 2;
    }

    // ── Zoom / pan / probe gestures ────────────────────────────────────────────

    private void OnSlicePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_volume is null) return;
        var pt = e.GetCurrentPoint(SliceViewport);
        if (!pt.Properties.IsLeftButtonPressed) return;
        _slicePressed = true;
        _slicePanning = false;
        _slicePressOrigin = _sliceLastDrag = pt.Position;
        SliceViewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSlicePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_volume is null) return;
        var pos = e.GetCurrentPoint(SliceViewport).Position;
        if (_slicePressed)
        {
            // Beyond a small threshold the press becomes a pan; a clean click still probes on release.
            if (!_slicePanning
                && (Math.Abs(pos.X - _slicePressOrigin.X) > SlicePanThresholdPx
                    || Math.Abs(pos.Y - _slicePressOrigin.Y) > SlicePanThresholdPx))
                _slicePanning = true;
            if (_slicePanning)
            {
                _slicePanX += pos.X - _sliceLastDrag.X;
                _slicePanY += pos.Y - _sliceLastDrag.Y;
                _sliceLastDrag = pos;
                ApplySliceTransform();
                return; // don't chase the readout chip mid-pan
            }
            _sliceLastDrag = pos;
        }
        UpdateCursorReadout(pos);
    }

    private void OnSlicePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_slicePressed) return;
        _slicePressed = false;
        SliceViewport.ReleasePointerCapture(e.Pointer);
        if (!_slicePanning) ProbeSpectrumAt(e.GetCurrentPoint(SliceViewport).Position);
        _slicePanning = false;
        e.Handled = true;
    }

    private void OnSlicePointerExited(object sender, PointerRoutedEventArgs e) => ClearCursorReadout();

    private void OnSlicePointerWheel(object sender, PointerRoutedEventArgs e)
    {
        if (_volume is null) return;
        var pt = e.GetCurrentPoint(SliceViewport);
        int delta = pt.Properties.MouseWheelDelta;
        // Same exp() feel as the volume orbit zoom (±0.12 per 120-tick notch).
        ZoomSliceToward(pt.Position, Math.Exp(delta / 120.0 * 0.12));
        e.Handled = true;
    }

    private void OnSliceDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ResetSliceView();

    /// <summary>Zoom by <paramref name="factor"/>, keeping the content under <paramref name="cursor"/> fixed.</summary>
    private void ZoomSliceToward(Point cursor, double factor)
    {
        double cx = SliceViewport.ActualWidth / 2, cy = SliceViewport.ActualHeight / 2;
        double newZoom = Math.Clamp(_sliceZoom * factor, 1.0, MaxSliceZoom);
        if (newZoom == _sliceZoom) return;
        // Solve pan' so the fit-space point currently under the cursor stays put.
        double fx = cx + (cursor.X - _slicePanX - cx) / _sliceZoom;
        double fy = cy + (cursor.Y - _slicePanY - cy) / _sliceZoom;
        _sliceZoom = newZoom;
        _slicePanX = cursor.X - cx - (fx - cx) * newZoom;
        _slicePanY = cursor.Y - cy - (fy - cy) * newZoom;
        ApplySliceTransform();
        UpdateCursorReadout(cursor);
    }

    private void ApplySliceTransform()
    {
        SliceTransform.ScaleX = _sliceZoom;
        SliceTransform.ScaleY = _sliceZoom;
        SliceTransform.TranslateX = _slicePanX;
        SliceTransform.TranslateY = _slicePanY;
    }

    /// <summary>Reset zoom/pan to the fit view (double-tap, and on every new cube).</summary>
    private void ResetSliceView()
    {
        _sliceZoom = 1;
        _slicePanX = _slicePanY = 0;
        _slicePressed = _slicePanning = false;
        ApplySliceTransform();
    }

    // ── Hover readout (cursor chip + coordinate bar) ───────────────────────────

    /// <summary>Update the floating chip + coordinate bar for a viewport pointer position.</summary>
    private void UpdateCursorReadout(Point pos)
    {
        if (_volume is null || ViewModel.ViewMode != CubeViewMode.Slice) return;
        var px = MapToPixel(pos);
        if (px is null) { ClearCursorReadout(); return; }
        var (x, y) = px.Value;

        // Voxel value: sampled from the (down-sampled) in-RAM volume — the same source the
        // spectrum probe uses — then mapped back to physical units via the normalization cut.
        int vx = MapDispToVolume(x, _sliceDispNx, _volume.Nx);
        int vy = MapDispToVolume(y, _sliceDispNy, _volume.Ny);
        float norm = (float)_volume.Data[((long)ViewModel.Channel * _volume.Ny + vy) * _volume.Nx + vx];
        string value;
        if (float.IsNaN(norm) || float.IsInfinity(norm))
        {
            value = "—";
        }
        else
        {
            double lo = _meta?.NormLo ?? 0, hi = _meta?.NormHi ?? 1;
            double phys = lo + norm * (hi - lo);
            string unit = string.IsNullOrEmpty(_meta?.Bunit) ? "" : " " + _meta!.Bunit;
            value = phys.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + unit;
        }

        // Sky coordinates at the NATIVE pixel matching the displayed one (the WCS is native-res).
        (string Lon, string Lat)? sky = null;
        if (_meta is { } m && m.Wcs.HasSpatial)
            sky = m.Wcs.SkyTextAt(
                MapDispToVolume(x, _sliceDispNx, m.Wcs.Nx),
                MapDispToVolume(y, _sliceDispNy, m.Wcs.Ny));

        // Spectral value for the current channel (same formatting as the channel label).
        string spec = "";
        if (_meta is not null && _meta.Wcs.HasSpectral)
        {
            string su = _meta.Wcs.SpecUnitDisplay();
            spec = _meta.Wcs.SpecText(ViewModel.Channel) + (string.IsNullOrEmpty(su) ? "" : " " + su);
        }

        // Chip lines (empty lines collapse).
        SetChipLine(CursorChipLon, sky is { } s1 ? $"{_meta!.Wcs.LonName} {s1.Lon}" : $"X {x}");
        SetChipLine(CursorChipLat, sky is { } s2 ? $"{_meta!.Wcs.LatName} {s2.Lat}" : $"Y {y}");
        SetChipLine(CursorChipSpec, spec);
        SetChipLine(CursorChipValue, value);
        PositionCursorChip(pos);
        CursorChip.Visibility = Visibility.Visible;

        // Coordinate bar: sky · value · spectral, on one line.
        var parts = new List<string>(3)
        {
            sky is { } s ? $"{_meta!.Wcs.LonName} {s.Lon}   {_meta!.Wcs.LatName} {s.Lat}" : $"px ({x}, {y})",
            value,
        };
        if (!string.IsNullOrEmpty(spec)) parts.Add(spec);
        SliceCoordText.Text = string.Join(" · ", parts);
    }

    private static void SetChipLine(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Place the chip near the pointer, clamped inside the viewport (macOS cursorChip offsets).</summary>
    private void PositionCursorChip(Point pos)
    {
        CursorChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cw = CursorChip.DesiredSize.Width, ch = CursorChip.DesiredSize.Height;
        double vw = SliceViewport.ActualWidth, vh = SliceViewport.ActualHeight;
        double cx = Math.Clamp(pos.X + 16, 0, Math.Max(0, vw - cw));
        double cy = Math.Clamp(pos.Y - ch - 12, 0, Math.Max(0, vh - ch));
        Canvas.SetLeft(CursorChip, cx);
        Canvas.SetTop(CursorChip, cy);
    }

    /// <summary>Hide the chip and reset the coordinate bar to its hint (pointer left / new cube).</summary>
    private void ClearCursorReadout()
    {
        CursorChip.Visibility = Visibility.Collapsed;
        SliceCoordText.Text = CoordBarHint;
    }
}
