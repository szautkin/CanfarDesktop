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

    /// <summary>
    /// How far through the file the parser is, 0 to 1 — or null while that cannot be known.
    ///
    /// Null is a real answer and not a zero: a stream that cannot report its length should leave the
    /// bar indeterminate rather than sit at the left pretending nothing has happened.
    /// </summary>
    [ObservableProperty] private double? _loadFraction;
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

    /// <summary>Null after a successful load; the parse error message when the last open failed.
    /// Lets a caller (e.g. the MCP open_fits_file action) report a real success/failure, not optimism.</summary>
    public string? LoadError { get; private set; }

    [RelayCommand]
    public async Task OpenFileAsync(string filePath)
    {
        IsLoading = true;
        LoadFraction = null;
        StatusMessage = $"Loading {Path.GetFileName(filePath)}...";
        LoadError = null;

        try
        {
            FilePath = filePath;
            Title = Path.GetFileName(filePath);

            // Reported from the parse thread, so it is marshalled back before anything is bound to.
            // A mosaic takes tens of seconds and every extension of it is visible progress.
            var name = Path.GetFileName(filePath);
            var progress = new Progress<Helpers.FitsParseProgress>(p =>
            {
                LoadFraction = Helpers.FitsLoadProgress.Fraction(p);
                StatusMessage = Helpers.FitsLoadProgress.Describe(name, p);
            });

            _hdus = await Task.Run(() =>
            {
                // Unwrap a tar/gzip container (CADC ships multi-product downloads as tar bundles)
                // so the parser sees a single FITS member, not the archive header.
                using var stream = FitsContainer.OpenFits(filePath);
                return FitsParser.Parse(stream, progress);
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
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
            LoadFraction = null;
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

        // Display row 0 is the LAST row of the array: the renderer flips Y to draw.
        var (_, arrayY) = PixelConvention.DisplayToArray(ix, iy, _imageData.Height);
        var pixelIdx = (int)arrayY * _imageData.Width + ix;
        var value = _imageData.Pixels[pixelIdx];

        PixelText = $"({ix}, {iy}) = {value:G6}";

        if (_imageData.Wcs is { IsValid: true } wcs)
        {
            var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, _imageData.Height, ix, iy);
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
