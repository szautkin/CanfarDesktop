namespace CanfarDesktop.ViewModels;

using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

/// <summary>
/// ViewModel for the FITS viewer. Manages file loading, stretch/colormap state,
/// and renders to a WriteableBitmap for display.
/// </summary>
public partial class FitsViewerViewModel : ObservableObject
{
    private FitsImageData? _imageData;
    private List<FitsHdu>? _hdus;
    private bool _disposed;
    private CancellationTokenSource? _renderCts;

    [ObservableProperty] private string _title = "FITS Viewer";
    [ObservableProperty] private string _statusMessage = "No file loaded";
    [ObservableProperty] private string _coordinateText = "";
    [ObservableProperty] private WorldCoordinate? _crosshairPosition;

    [ObservableProperty] private string _pixelText = "";
    [ObservableProperty] private WriteableBitmap? _renderedImage;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentHeader))]
    private int _selectedHduIndex;
    [ObservableProperty] private float _minCut;
    [ObservableProperty] private float _maxCut;
    [ObservableProperty] private ImageStretcher.StretchMode _stretch = ImageStretcher.StretchMode.Linear;
    [ObservableProperty] private ColormapProvider.ColormapName _colormap = ColormapProvider.ColormapName.Grayscale;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private bool _isNorthUp;

    public List<FitsHdu>? Hdus => _hdus;
    public FitsImageData? ImageData => _imageData;
    public FitsHeader? CurrentHeader => _hdus is not null && SelectedHduIndex < _hdus.Count
        ? _hdus[SelectedHduIndex].Header : null;

    public string? FilePath { get; private set; }

    [RelayCommand]
    public async Task OpenFileAsync(string filePath)
    {
        IsLoading = true;
        StatusMessage = $"Loading {Path.GetFileName(filePath)}...";

        try
        {
            FilePath = filePath;
            Title = Path.GetFileName(filePath);

            _hdus = await Task.Run(() =>
            {
                using var stream = File.OpenRead(filePath);
                return FitsParser.Parse(stream);
            });

            // Find first image HDU
            var imageHdu = _hdus.FirstOrDefault(h => h.HasImage);
            if (imageHdu is null)
            {
                StatusMessage = "No image data found in FITS file";
                IsLoading = false;
                return;
            }

            SelectedHduIndex = imageHdu.Index;
            _imageData = imageHdu.ImageData;

            // Auto-cut
            var (autoMin, autoMax) = FitsRenderer.AutoCut(_imageData!);
            MinCut = autoMin;
            MaxCut = autoMax;

            StatusMessage = $"{_imageData!.Width} x {_imageData.Height} | {_hdus.Count} HDU(s)";

            await RenderAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RenderAsync()
    {
        if (_imageData is null || _disposed) return;

        // Cancel any in-flight render (e.g., from rapid slider drag)
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = _renderCts = new CancellationTokenSource();

        var image = _imageData;
        var stretch = Stretch;
        var colormapName = Colormap;
        var minCut = MinCut;
        var maxCut = MaxCut;

        var colormap = ColormapProvider.GetColormap(colormapName);

        byte[] bgra;
        try
        {
            bgra = await Task.Run(() =>
                FitsRenderer.Render(image, stretch, colormap, minCut, maxCut, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) { return; }

        if (cts.Token.IsCancellationRequested || _disposed) return;

        var bitmap = new WriteableBitmap(image.Width, image.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(bgra, cts.Token);
        }
        bitmap.Invalidate();

        RenderedImage = bitmap;
    }

    partial void OnStretchChanged(ImageStretcher.StretchMode value) => _ = RenderAsync();
    partial void OnColormapChanged(ColormapProvider.ColormapName value) => _ = RenderAsync();
    partial void OnMinCutChanged(float value) => _ = RenderAsync();
    partial void OnMaxCutChanged(float value) => _ = RenderAsync();

    /// <summary>
    /// Update coordinate/pixel readout for a given pixel position.
    /// </summary>
    public void UpdatePixelInfo(double px, double py)
    {
        if (_imageData is null) return;

        var ix = (int)px;
        var iy = (int)py;

        if (ix < 0 || ix >= _imageData.Width || iy < 0 || iy >= _imageData.Height)
        {
            CoordinateText = "";
            PixelText = "";
            return;
        }

        // FITS Y is flipped: display row 0 = FITS row (height-1)
        var fitsY = _imageData.Height - 1 - iy;
        var pixelIdx = fitsY * _imageData.Width + ix;
        var value = _imageData.Pixels[pixelIdx];

        PixelText = $"({ix}, {iy}) = {value:G6}";

        if (_imageData.Wcs is { IsValid: true } wcs)
        {
            var (ra, dec) = wcs.PixelToWorld(ix + 1, fitsY + 1); // FITS pixels are 1-based
            CoordinateText = $"RA {WcsInfo.FormatRa(ra)}  Dec {WcsInfo.FormatDec(dec)}";
        }
        else
        {
            CoordinateText = "";
        }
    }

    [RelayCommand]
    public void ResetStretch()
    {
        if (_imageData is null) return;
        var (autoMin, autoMax) = FitsRenderer.AutoCut(_imageData);
        MinCut = autoMin;
        MaxCut = autoMax;
        Stretch = ImageStretcher.StretchMode.Linear;
    }

    [RelayCommand]
    public void SelectHdu(int index)
    {
        if (_hdus is null || index < 0 || index >= _hdus.Count) return;
        var hdu = _hdus[index];
        if (!hdu.HasImage || hdu.ImageData is null) return;

        SelectedHduIndex = index;
        _imageData = hdu.ImageData;

        var (autoMin, autoMax) = FitsRenderer.AutoCut(_imageData);
        MinCut = autoMin;
        MaxCut = autoMax;

        _ = RenderAsync();
    }

    /// <summary>
    /// Convert world RA/Dec to display pixel coordinates (0-based, Y-flipped).
    /// Returns null if no image/WCS or singular matrix.
    /// </summary>
    public (double X, double Y)? GoToCoordinate(double ra, double dec)
    {
        if (_imageData?.Wcs is not { IsValid: true } wcs) return null;
        var pixel = wcs.WorldToPixel(ra, dec);
        if (pixel is null) return null;
        // 1-based FITS pixel → 0-based display pixel, flip Y
        var displayX = pixel.Value.Px - 1;
        var displayY = _imageData.Height - 1 - (pixel.Value.Py - 1);
        return (displayX, displayY);
    }

    /// <summary>
    /// Release heavy resources (bitmaps, pixel arrays) for tab close.
    /// </summary>
    public void Cleanup()
    {
        _disposed = true;
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        RenderedImage = null;
        _imageData = null;
        _hdus = null;
    }
}
