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
    private bool _headerVisible;
    private bool _suppressSliderChange;
    private float _sliderRangeMin;
    private float _sliderRangeMax;
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private double _scrollStartH;
    private double _scrollStartV;

    public FitsViewerPage(FitsViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

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

        ImageScroller.ViewChanged += (_, _) =>
        {
            ZoomLabel.Text = $"{ImageScroller.ZoomFactor * 100:F0}%";
        };
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

    private void OnResetStretch(object s, RoutedEventArgs e) => ViewModel.ResetStretchCommand.Execute(null);

    private void OnToggleHeader(object s, RoutedEventArgs e)
    {
        _headerVisible = !_headerVisible;
        HeaderColumn.Width = _headerVisible ? new GridLength(320) : new GridLength(0);
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

    // ── Mouse: drag-to-pan, scroll, coordinate tracking ────────────────────

    private void OnCanvasWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(ImageScroller).Properties;
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (shift)
        {
            // Shift+scroll → horizontal pan
            var delta = props.MouseWheelDelta;
            ImageScroller.ScrollToHorizontalOffset(ImageScroller.HorizontalOffset - delta);
            e.Handled = true;
        }
    }

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageScroller);

        // Left button or middle button → start drag-to-pan
        if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _isDragging = true;
            _dragStart = point.Position;
            _scrollStartH = ImageScroller.HorizontalOffset;
            _scrollStartV = ImageScroller.VerticalOffset;
            ImageScroller.CapturePointer(e.Pointer);
        }
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetCurrentPoint(ImageScroller).Position;
            var dx = _dragStart.X - pos.X;
            var dy = _dragStart.Y - pos.Y;

            ImageScroller.ScrollToHorizontalOffset(_scrollStartH + dx);
            ImageScroller.ScrollToVerticalOffset(_scrollStartV + dy);
            return;
        }

        // Coordinate readout
        if (ViewModel.RenderedImage is null) return;
        var imgPos = e.GetCurrentPoint(FitsImage).Position;
        ViewModel.UpdatePixelInfo(imgPos.X, imgPos.Y);
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ImageScroller.ReleasePointerCapture(e.Pointer);
        }
    }
}
