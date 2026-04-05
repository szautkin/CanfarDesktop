using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.FitsViewer;

public sealed partial class FitsViewerPage : UserControl
{
    public FitsViewerViewModel ViewModel { get; }
    public event Action<double, double>? SearchAtPositionRequested;
    /// <summary>Raised when zoom changes via scroll wheel so the host can update its slider.</summary>
    public event Action<double>? ZoomChanged;

    private bool _headerVisible;
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private double _scrollStartH;
    private double _scrollStartV;
    private Windows.Foundation.Point? _crosshairScreenPos;

    /// <summary>Slider normalization range (per-image).</summary>
    public float SliderRangeMin { get; private set; }
    public float SliderRangeMax { get; private set; }

    public FitsViewerPage(FitsViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ImageCanvas.SizeChanged += (_, _) =>
        {
            ImageCanvas.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, ImageCanvas.ActualWidth, ImageCanvas.ActualHeight)
            };
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Re-bind image when tab becomes visible again (WinUI unloads hidden tabs)
        Loaded += OnLoaded;

        ZoomLabel.Text = "100%";
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.StatusMessage):
                    StatusLabel.Text = ViewModel.StatusMessage;
                    break;
                case nameof(ViewModel.CoordinateText):
                    CoordLabel.Text = ViewModel.CoordinateText;
                    break;
                case nameof(ViewModel.PixelText):
                    PixelLabel.Text = ViewModel.PixelText;
                    break;
                case nameof(ViewModel.RenderedImage):
                    FitsImage.Source = ViewModel.RenderedImage;
                    break;
            }
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Restore image source when tab becomes visible again after WinUI unload
        if (FitsImage.Source is null && ViewModel.RenderedImage is not null)
            FitsImage.Source = ViewModel.RenderedImage;
    }

    /// <summary>
    /// Called explicitly by FitsTabHost on tab close. Do NOT use Unloaded —
    /// WinUI fires Unloaded when switching tabs, which would destroy the image.
    /// </summary>
    public void CleanupForClose()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        FitsImage.Source = null;
    }

    // ── Public API for FitsTabHost ──────────────────────────────────────────

    public async Task OpenFileAsync(string filePath)
    {
        await ViewModel.OpenFileCommand.ExecuteAsync(filePath);
        UpdateHeaderList();
        ComputeSliderRange();
    }

    public void ToggleHeader()
    {
        _headerVisible = !_headerVisible;
        HeaderColumn.Width = _headerVisible ? new GridLength(320) : new GridLength(0);
    }

    public void ClearCrosshair()
    {
        _crosshairScreenPos = null;
        CrosshairH.Visibility = Visibility.Collapsed;
        CrosshairV.Visibility = Visibility.Collapsed;
        CrosshairLabel.Visibility = Visibility.Collapsed;
        ViewModel.CrosshairCoords = "";
        ViewModel.CrosshairRa = null;
        ViewModel.CrosshairDec = null;
    }

    public string? CopyCoords()
    {
        if (string.IsNullOrEmpty(ViewModel.CrosshairCoords)) return null;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(ViewModel.CrosshairCoords);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        ViewModel.StatusMessage = "Coordinates copied to clipboard";
        return ViewModel.CrosshairCoords;
    }

    public void TriggerSearchAtPosition()
    {
        if (ViewModel.CrosshairRa is null || ViewModel.CrosshairDec is null)
        {
            ViewModel.StatusMessage = "Right-click on the image to place crosshair first";
            return;
        }
        SearchAtPositionRequested?.Invoke(ViewModel.CrosshairRa.Value, ViewModel.CrosshairDec.Value);
    }

    public void ResetView()
    {
        ViewModel.ResetStretchCommand.Execute(null);
        ViewModel.IsNorthUp = false;
        ImageTransform.ScaleX = 1;
        ImageTransform.ScaleY = 1;
        ImageTransform.TranslateX = 0;
        ImageTransform.TranslateY = 0;
        ImageTransform.Rotation = 0;
        ImageTransform.ScaleX = 1; // remove any flip
        ZoomLabel.Text = "100%";
        ComputeSliderRange();
    }

    /// <summary>
    /// Toggle North-up orientation using WCS rotation angle.
    /// Rotates the image so celestial North points up and East points left.
    /// </summary>
    public void SetNorthUp(bool northUp)
    {
        ViewModel.IsNorthUp = northUp;

        if (!northUp)
        {
            var mag = Math.Abs(ImageTransform.ScaleX);
            ImageTransform.Rotation = 0;
            ImageTransform.ScaleX = mag;
            ViewModel.StatusMessage = "Original orientation";
            UpdateCrosshairCoords();
            return;
        }

        if (ViewModel.ImageData?.Wcs is not { IsValid: true } wcs)
        {
            ViewModel.StatusMessage = "No WCS — cannot determine North direction";
            ViewModel.IsNorthUp = false;
            return;
        }

        var angle = wcs.NorthAngle;
        var mag2 = Math.Abs(ImageTransform.ScaleX);

        ImageTransform.Rotation = -angle;
        ImageTransform.ScaleX = wcs.HasParityFlip ? -mag2 : mag2;

        // With RenderTransformOrigin="0.5,0.5" the rotation is around image center —
        // no translate compensation needed.

        ViewModel.StatusMessage = $"North Up (rotated {angle:F1}\u00b0" +
            (wcs.HasParityFlip ? ", mirrored" : "") +
            $", {wcs.PixelScaleArcsec:F2}\"/px)";
        UpdateCrosshairCoords();
    }

    /// <summary>
    /// Navigate crosshair to a world coordinate (RA, Dec in degrees).
    /// Converts to display pixel and places crosshair at the screen position.
    /// </summary>
    public void GoToWorldCoordinate(double ra, double dec)
    {
        var displayPixel = ViewModel.GoToCoordinate(ra, dec);
        if (displayPixel is null)
        {
            ViewModel.StatusMessage = "No WCS available for coordinate navigation";
            return;
        }

        var x = displayPixel.Value.X;
        var y = displayPixel.Value.Y;
        var w = ViewModel.ImageData!.Width;
        var h = ViewModel.ImageData!.Height;

        if (x < 0 || x >= w || y < 0 || y >= h)
        {
            ViewModel.StatusMessage = $"Coordinate outside image bounds (pixel {x:F0}, {y:F0} — image is {w}x{h})";
            return;
        }

        // Center the view on this coordinate
        CenterOnImagePixel(x, y);
    }

    private void CenterOnImagePixel(double imgPixelX, double imgPixelY)
    {
        var p = GetTransformParams();
        double localX = imgPixelX, localY = imgPixelY;
        if (ViewModel.ImageData is not null && p.imgW > 0)
        {
            localX = imgPixelX / ViewModel.ImageData.Width * p.imgW;
            localY = imgPixelY / ViewModel.ImageData.Height * p.imgH;
        }

        var (tx, ty) = ViewportMath.ComputeCenterTranslate(localX, localY,
            p.scaleX, p.scaleY, p.rotation, p.imgW, p.imgH, p.canvasW, p.canvasH);
        ImageTransform.TranslateX = tx;
        ImageTransform.TranslateY = ty;

        var centerX = p.canvasW / 2;
        var centerY = p.canvasH / 2;
        _crosshairScreenPos = new Windows.Foundation.Point(centerX, centerY);
        DrawCrosshairLines(_crosshairScreenPos.Value);
        UpdateCrosshairCoords();
    }

    /// <summary>Get current zoom magnitude (always positive).</summary>
    public double GetZoomMagnitude() => Math.Abs(ImageTransform.ScaleX);

    /// <summary>Set zoom level from the toolbar slider (preserves mirror sign).</summary>
    public void SetZoomLevel(double magnitude)
    {
        var sign = ImageTransform.ScaleX < 0 ? -1.0 : 1.0;
        var clamped = Math.Clamp(magnitude, 0.05, 20.0);
        ImageTransform.ScaleX = clamped * sign;
        ImageTransform.ScaleY = clamped;
        ZoomLabel.Text = $"{clamped * 100:F0}%";
        UpdateCrosshairCoords();
    }

    /// <summary>Compute slider range from current auto-cut values.</summary>
    public void ComputeSliderRange()
    {
        if (ViewModel.ImageData is null) return;
        var autoCutSpread = ViewModel.MaxCut - ViewModel.MinCut;
        var margin = Math.Max(autoCutSpread * 2, 1f);
        SliderRangeMin = ViewModel.MinCut - margin;
        SliderRangeMax = ViewModel.MaxCut + margin;
    }

    /// <summary>Convert a 0-100 slider value to an actual cut value.</summary>
    public float SliderToValue(double sliderValue)
    {
        var range = SliderRangeMax - SliderRangeMin;
        return SliderRangeMin + (float)(sliderValue / 100.0 * range);
    }

    /// <summary>Convert an actual cut value to a 0-100 slider value.</summary>
    public double ValueToSlider(float cutValue)
    {
        var range = SliderRangeMax - SliderRangeMin;
        return range > 0 ? (cutValue - SliderRangeMin) / range * 100 : 0;
    }

    // ── Header panel ─────────────────────────────────────────────────────────

    private void UpdateHeaderList()
    {
        if (ViewModel.CurrentHeader is null) return;
        HeaderList.ItemsSource = ViewModel.CurrentHeader.OrderedCards;
    }

    private void OnHeaderFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel.CurrentHeader is null) return;
        var filter = HeaderFilter.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            HeaderList.ItemsSource = ViewModel.CurrentHeader.OrderedCards;
        }
        else
        {
            HeaderList.ItemsSource = ViewModel.CurrentHeader.OrderedCards
                .Where(c => c.Keyword.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || c.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || c.Comment.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    // ── Mouse: zoom (wheel), pan (drag), coordinate readout ────────────────

    private void OnCanvasWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageCanvas);
        var delta = point.Properties.MouseWheelDelta;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl)
        {
            var factor = delta > 0 ? 1.15 : 1.0 / 1.15;
            var oldMag = Math.Abs(ImageTransform.ScaleX);
            var newMag = Math.Clamp(oldMag * factor, 0.05, 50.0);
            var signX = ImageTransform.ScaleX < 0 ? -1.0 : 1.0;

            var p = GetTransformParams();
            var (tx, ty) = ViewportMath.ComputeZoomTranslate(
                point.Position.X, point.Position.Y,
                p.scaleX, p.scaleY, newMag * signX, newMag,
                p.rotation, p.imgW, p.imgH, p.canvasW, p.canvasH,
                p.translateX, p.translateY);

            ImageTransform.ScaleX = newMag * signX;
            ImageTransform.ScaleY = newMag;
            ImageTransform.TranslateX = tx;
            ImageTransform.TranslateY = ty;

            ZoomLabel.Text = $"{newMag * 100:F0}%";
            ZoomChanged?.Invoke(newMag);
            UpdateCrosshairCoords();
        }
        else if (shift)
        {
            ImageTransform.TranslateX += delta;
            UpdateCrosshairCoords();
        }
        else
        {
            ImageTransform.TranslateY += delta;
            UpdateCrosshairCoords();
        }

        e.Handled = true;
    }

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageCanvas);

        if (point.Properties.IsRightButtonPressed)
        {
            PlaceCrosshair(point.Position);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _isDragging = true;
            _dragStart = point.Position;
            _scrollStartH = ImageTransform.TranslateX;
            _scrollStartV = ImageTransform.TranslateY;
            ImageCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void PlaceCrosshair(Windows.Foundation.Point screenPos)
    {
        var scale = Math.Abs(ImageTransform.ScaleX);

        if (scale > 1.05) // zoomed in: center the view on the clicked point
        {
            var imgPx = e_PointToImage(screenPos);
            CenterOnImagePixel(imgPx.X, imgPx.Y);
        }
        else
        {
            _crosshairScreenPos = screenPos;
            DrawCrosshairLines(screenPos);
            UpdateCrosshairCoords();
        }
    }

    private void DrawCrosshairLines(Windows.Foundation.Point pos)
    {
        var canvasW = ImageCanvas.ActualWidth;
        var canvasH = ImageCanvas.ActualHeight;
        CrosshairH.X1 = 0; CrosshairH.Y1 = pos.Y;
        CrosshairH.X2 = canvasW; CrosshairH.Y2 = pos.Y;
        CrosshairV.X1 = pos.X; CrosshairV.Y1 = 0;
        CrosshairV.X2 = pos.X; CrosshairV.Y2 = canvasH;
        CrosshairH.Visibility = Visibility.Visible;
        CrosshairV.Visibility = Visibility.Visible;
    }

    private void UpdateCrosshairCoords()
    {
        if (_crosshairScreenPos is null || ViewModel.ImageData is null) return;

        var screenPos = _crosshairScreenPos.Value;
        var imgPx = e_PointToImage(screenPos);
        var ix = (int)imgPx.X;
        var iy = (int)imgPx.Y;
        var w = ViewModel.ImageData.Width;
        var h = ViewModel.ImageData.Height;
        var lines = new List<string>();

        if (ix >= 0 && ix < w && iy >= 0 && iy < h)
        {
            var fitsY = h - 1 - iy;
            var pixelIdx = fitsY * w + ix;
            var value = ViewModel.ImageData.Pixels[pixelIdx];
            lines.Add($"Pixel ({ix}, {iy}) = {value:G6}");

            if (ViewModel.ImageData.Wcs is { IsValid: true } wcs)
            {
                var (ra, dec) = wcs.PixelToWorld(ix + 1, fitsY + 1);
                var raStr = WcsInfo.FormatRa(ra);
                var decStr = WcsInfo.FormatDec(dec);
                lines.Add($"RA  {raStr}");
                lines.Add($"Dec {decStr}");
                ViewModel.CrosshairRa = ra;
                ViewModel.CrosshairDec = dec;
                ViewModel.CrosshairCoords = $"RA {raStr}  Dec {decStr}";
            }
        }

        CrosshairText.Text = string.Join("\n", lines);
        CrosshairLabel.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var labelX = screenPos.X + 12;
        var labelY = screenPos.Y - 50;
        var canvasW = ImageCanvas.ActualWidth;
        if (labelX + 200 > canvasW) labelX = screenPos.X - 200;
        if (labelY < 0) labelY = screenPos.Y + 12;
        Canvas.SetLeft(CrosshairLabel, labelX);
        Canvas.SetTop(CrosshairLabel, labelY);
    }

    /// <summary>Helper to get current transform params for ViewportMath calls.</summary>
    private (double scaleX, double scaleY, double rotation, double imgW, double imgH,
             double canvasW, double canvasH, double translateX, double translateY) GetTransformParams()
    {
        var scaleX = ImageTransform.ScaleX;
        var scaleY = ImageTransform.ScaleY;
        if (Math.Abs(scaleX) < 0.001) scaleX = 1;
        if (Math.Abs(scaleY) < 0.001) scaleY = 1;
        return (scaleX, scaleY, ImageTransform.Rotation,
                FitsImage.ActualWidth, FitsImage.ActualHeight,
                ImageCanvas.ActualWidth, ImageCanvas.ActualHeight,
                ImageTransform.TranslateX, ImageTransform.TranslateY);
    }

    private Windows.Foundation.Point ImageToScreen(Windows.Foundation.Point imgPx)
    {
        var p = GetTransformParams();
        double localX = imgPx.X, localY = imgPx.Y;
        if (ViewModel.ImageData is not null && p.imgW > 0)
        {
            localX = imgPx.X / ViewModel.ImageData.Width * p.imgW;
            localY = imgPx.Y / ViewModel.ImageData.Height * p.imgH;
        }
        var (sx, sy) = ViewportMath.LocalToScreen(localX, localY,
            p.scaleX, p.scaleY, p.rotation, p.imgW, p.imgH,
            p.canvasW, p.canvasH, p.translateX, p.translateY);
        return new Windows.Foundation.Point(sx, sy);
    }

    private Windows.Foundation.Point e_PointToImage(Windows.Foundation.Point screenPos)
    {
        var p = GetTransformParams();
        var (lx, ly) = ViewportMath.ScreenToLocal(screenPos.X, screenPos.Y,
            p.scaleX, p.scaleY, p.rotation, p.imgW, p.imgH,
            p.canvasW, p.canvasH, p.translateX, p.translateY);
        if (ViewModel.ImageData is not null && p.imgW > 0)
        {
            lx = lx / p.imgW * ViewModel.ImageData.Width;
            ly = ly / p.imgH * ViewModel.ImageData.Height;
        }
        return new Windows.Foundation.Point(lx, ly);
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetCurrentPoint(ImageCanvas).Position;
            ImageTransform.TranslateX = _scrollStartH + (pos.X - _dragStart.X);
            ImageTransform.TranslateY = _scrollStartV + (pos.Y - _dragStart.Y);
            UpdateCrosshairCoords();
            e.Handled = true;
            return;
        }

        if (ViewModel.RenderedImage is null || ViewModel.ImageData is null) return;
        var screenPos = e.GetCurrentPoint(ImageCanvas).Position;
        var pixelPos = e_PointToImage(screenPos);
        ViewModel.UpdatePixelInfo(pixelPos.X, pixelPos.Y);
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ImageCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }
}
