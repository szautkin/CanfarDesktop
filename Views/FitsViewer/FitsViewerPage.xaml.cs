using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.ViewModels;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop.Views.FitsViewer;

public sealed partial class FitsViewerPage : UserControl
{
    public FitsViewerViewModel ViewModel { get; }
    public event Action<double, double>? SearchAtPositionRequested;
    private bool _headerVisible;
    private bool _suppressSliderChange;
    private float _sliderRangeMin;
    private float _sliderRangeMax;
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private double _scrollStartH;
    private double _scrollStartV;
    private Windows.Foundation.Point? _crosshairScreenPos; // fixed screen position

    public FitsViewerPage(FitsViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Clip the image canvas so zoomed image doesn't overflow into toolbar/statusbar
        ImageCanvas.SizeChanged += (_, _) =>
        {
            ImageCanvas.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, ImageCanvas.ActualWidth, ImageCanvas.ActualHeight)
            };
        };

        ViewModel.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
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
                case nameof(ViewModel.IsLoading):
                    LoadingRing.IsActive = ViewModel.IsLoading;
                    break;
                case nameof(ViewModel.RenderedImage):
                    FitsImage.Source = ViewModel.RenderedImage;
                    break;
                case nameof(ViewModel.MinCut) or nameof(ViewModel.MaxCut):
                    UpdateSliders();
                    break;
            }
        });

        ZoomLabel.Text = "100%";
    }

    public async Task OpenFileAsync(string filePath)
    {
        await ViewModel.OpenFileCommand.ExecuteAsync(filePath);
        UpdateHeaderList();
        UpdateSliderRange();
    }

    // ── Toolbar ──────────────────────────────────────────────────────────────

    private async void OnOpenFile(object s, RoutedEventArgs e)
    {
        var hwnd = ActiveWindows.Count > 0
            ? WindowNative.GetWindowHandle(ActiveWindows[0])
            : nint.Zero;
        if (hwnd == nint.Zero) return;

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".fits");
        picker.FileTypeFilter.Add(".fit");
        picker.FileTypeFilter.Add(".fts");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await OpenFileAsync(file.Path);
    }

    private void OnStretchChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StretchCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (Enum.TryParse<ImageStretcher.StretchMode>(tag, out var mode))
                ViewModel.Stretch = mode;
        }
    }

    private void OnColormapChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColormapCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (Enum.TryParse<ColormapProvider.ColormapName>(tag, out var name))
                ViewModel.Colormap = name;
        }
    }

    private void OnMinCutChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSliderChange || ViewModel.ImageData is null) return;
        var range = _sliderRangeMax - _sliderRangeMin;
        ViewModel.MinCut = _sliderRangeMin + (float)(e.NewValue / 100.0 * range);
    }

    private void OnMaxCutChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSliderChange || ViewModel.ImageData is null) return;
        var range = _sliderRangeMax - _sliderRangeMin;
        ViewModel.MaxCut = _sliderRangeMin + (float)(e.NewValue / 100.0 * range);
    }

    private void OnResetStretch(object s, RoutedEventArgs e)
    {
        ViewModel.ResetStretchCommand.Execute(null);
        // Reset zoom + pan
        ImageTransform.ScaleX = 1;
        ImageTransform.ScaleY = 1;
        ImageTransform.TranslateX = 0;
        ImageTransform.TranslateY = 0;
        ZoomLabel.Text = "100%";
    }

    private void OnToggleHeader(object s, RoutedEventArgs e)
    {
        _headerVisible = !_headerVisible;
        HeaderColumn.Width = _headerVisible ? new GridLength(320) : new GridLength(0);
    }

    // ── Crosshair toolbar ────────────────────────────────────────────────────

    private void OnCopyCoords(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.CrosshairCoords)) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(ViewModel.CrosshairCoords);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        ViewModel.StatusMessage = "Coordinates copied to clipboard";
    }

    private void OnSearchAtPosition(object s, RoutedEventArgs e)
    {
        if (ViewModel.CrosshairRa is null || ViewModel.CrosshairDec is null)
        {
            ViewModel.StatusMessage = "Right-click on the image to place crosshair first";
            return;
        }
        SearchAtPositionRequested?.Invoke(ViewModel.CrosshairRa.Value, ViewModel.CrosshairDec.Value);
    }

    private void OnClearCrosshair(object s, RoutedEventArgs e)
    {
        _crosshairScreenPos = null;
        CrosshairH.Visibility = Visibility.Collapsed;
        CrosshairV.Visibility = Visibility.Collapsed;
        CrosshairLabel.Visibility = Visibility.Collapsed;
        ViewModel.CrosshairCoords = "";
        ViewModel.CrosshairRa = null;
        ViewModel.CrosshairDec = null;
    }

    // ── Sliders ──────────────────────────────────────────────────────────────

    private void UpdateSliderRange()
    {
        if (ViewModel.ImageData is null) return;

        // Set slider range to ±2x the auto-cut spread, centered on the data
        var autoCutSpread = ViewModel.MaxCut - ViewModel.MinCut;
        var margin = Math.Max(autoCutSpread * 2, 1f);
        _sliderRangeMin = ViewModel.MinCut - margin;
        _sliderRangeMax = ViewModel.MaxCut + margin;

        _suppressSliderChange = true;
        UpdateSliders();
        _suppressSliderChange = false;
    }

    private void UpdateSliders()
    {
        if (ViewModel.ImageData is null) return;
        _suppressSliderChange = true;
        var range = _sliderRangeMax - _sliderRangeMin;
        if (range > 0)
        {
            MinCutSlider.Value = (ViewModel.MinCut - _sliderRangeMin) / range * 100;
            MaxCutSlider.Value = (ViewModel.MaxCut - _sliderRangeMin) / range * 100;
        }
        _suppressSliderChange = false;
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
            // Ctrl+scroll → zoom toward cursor
            var factor = delta > 0 ? 1.15 : 1.0 / 1.15;
            var oldScale = ImageTransform.ScaleX;
            var newScale = Math.Clamp(oldScale * factor, 0.05, 50.0);

            var cursorX = point.Position.X;
            var cursorY = point.Position.Y;
            ImageTransform.TranslateX = cursorX - (cursorX - ImageTransform.TranslateX) * (newScale / oldScale);
            ImageTransform.TranslateY = cursorY - (cursorY - ImageTransform.TranslateY) * (newScale / oldScale);
            ImageTransform.ScaleX = newScale;
            ImageTransform.ScaleY = newScale;

            ZoomLabel.Text = $"{newScale * 100:F0}%";
            UpdateCrosshairCoords();
        }
        else if (shift)
        {
            // Shift+scroll → horizontal pan
            ImageTransform.TranslateX += delta;
            UpdateCrosshairCoords();
        }
        else
        {
            // Scroll → vertical pan
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
            // Right-click → place crosshair
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
        _crosshairScreenPos = screenPos;
        DrawCrosshairLines(screenPos);
        UpdateCrosshairCoords();
    }

    /// <summary>Draw crosshair lines at a fixed screen position.</summary>
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

    /// <summary>
    /// Update the crosshair coordinate label based on whatever image pixel
    /// is currently under the fixed screen position. Call after pan/zoom.
    /// </summary>
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
                var raStr = Models.Fits.WcsInfo.FormatRa(ra);
                var decStr = Models.Fits.WcsInfo.FormatDec(dec);
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

    /// <summary>Convert image pixel coords back to screen coords (inverse of e_PointToImage).</summary>
    private Windows.Foundation.Point ImageToScreen(Windows.Foundation.Point imgPx)
    {
        var scale = ImageTransform.ScaleX;
        if (scale <= 0) scale = 1;

        var imgW = FitsImage.ActualWidth;
        var imgH = FitsImage.ActualHeight;
        var canvasW = ImageCanvas.ActualWidth;
        var canvasH = ImageCanvas.ActualHeight;
        var imgOffsetX = (canvasW - imgW) / 2;
        var imgOffsetY = (canvasH - imgH) / 2;

        // Map from pixel coords to display image coords
        double displayX = imgPx.X, displayY = imgPx.Y;
        if (ViewModel.ImageData is not null && imgW > 0)
        {
            displayX = imgPx.X / ViewModel.ImageData.Width * imgW;
            displayY = imgPx.Y / ViewModel.ImageData.Height * imgH;
        }

        var sx = displayX * scale + imgOffsetX + ImageTransform.TranslateX;
        var sy = displayY * scale + imgOffsetY + ImageTransform.TranslateY;
        return new Windows.Foundation.Point(sx, sy);
    }

    /// <summary>Convert screen position to image pixel coordinates.</summary>
    private Windows.Foundation.Point e_PointToImage(Windows.Foundation.Point screenPos)
    {
        // Inverse the CompositeTransform: (screen - translate) / scale
        // Then account for Image centering within the canvas
        var scale = ImageTransform.ScaleX;
        if (scale <= 0) scale = 1;

        // The Image is centered — find its offset
        var imgW = FitsImage.ActualWidth;
        var imgH = FitsImage.ActualHeight;
        var canvasW = ImageCanvas.ActualWidth;
        var canvasH = ImageCanvas.ActualHeight;
        var imgOffsetX = (canvasW - imgW) / 2;
        var imgOffsetY = (canvasH - imgH) / 2;

        var px = (screenPos.X - ImageTransform.TranslateX - imgOffsetX) / scale;
        var py = (screenPos.Y - ImageTransform.TranslateY - imgOffsetY) / scale;

        // Map from display image size to actual pixel size
        if (ViewModel.ImageData is not null && imgW > 0)
        {
            px = px / imgW * ViewModel.ImageData.Width;
            py = py / imgH * ViewModel.ImageData.Height;
        }

        return new Windows.Foundation.Point(px, py);
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetCurrentPoint(ImageCanvas).Position;
            ImageTransform.TranslateX = _scrollStartH + (pos.X - _dragStart.X);
            ImageTransform.TranslateY = _scrollStartV + (pos.Y - _dragStart.Y);
            UpdateCrosshairCoords(); // crosshair tracks the image
            e.Handled = true;
            return;
        }

        // Coordinate readout
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
