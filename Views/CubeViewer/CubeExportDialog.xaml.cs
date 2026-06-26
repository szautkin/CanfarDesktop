using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// The figure-export modal: a live WYSIWYG preview of the <see cref="CubeExportPlate"/> on the left,
/// style controls (theme / font / text color / scale / annotations / transparency) on the right, and
/// PNG/PDF export at 2×/4×. The plate is rasterized at export time — same control, scaled. The cube
/// frame is captured once (transparent) so every style change is an instant re-layout, no GPU work.
/// </summary>
public sealed partial class CubeExportDialog : ContentDialog
{
    private WriteableBitmap _frame = null!;
    private int _frameW, _frameH;
    private CubeExportPlate.PlateData _data;
    private string _baseName = "cube";
    private bool _ready;
    private CubeExportPlate? _plate;

    public CubeExportDialog() => InitializeComponent();

    /// <summary>Provide the captured (transparent) volume snapshot + plate content, and show the live preview.</summary>
    public void Initialize(WriteableBitmap frame, int frameW, int frameH, CubeExportPlate.PlateData data, string baseName)
    {
        _frame = frame;
        _frameW = frameW;
        _frameH = frameH;
        _data = data;
        _baseName = string.IsNullOrWhiteSpace(baseName) ? "cube" : baseName;

        ThemeCombo.SelectedIndex = 0;     // cockpit dark
        FontCombo.SelectedIndex = 0;      // sans
        TextColorCombo.SelectedIndex = 0; // auto
        ResCombo.SelectedIndex = 0;       // 2×
        _ready = true;
        Rebuild();
    }

    private CubeExportPlate.PlateStyle CurrentStyle() => new()
    {
        Dark = ThemeCombo.SelectedIndex == 0,
        Font = FontCombo.SelectedIndex switch { 1 => "mono", 2 => "serif", _ => "sans" },
        TextColor = TextColorCombo.SelectedIndex switch { 1 => "white", 2 => "black", 3 => "cyan", 4 => "amber", _ => "auto" },
        TextScale = ScaleSlider.Value,
        Annotate = AnnotateToggle.IsOn,
        Transparent = TransparentToggle.IsOn,
    };

    private void Rebuild()
    {
        if (!_ready) return;
        _plate ??= new CubeExportPlate();
        if (PreviewBox.Child != _plate) PreviewBox.Child = _plate;
        _plate.Populate(_frame, _frameW, _frameH, _data, CurrentStyle());
    }

    private void OnStyleChanged(object sender, object e) => Rebuild();

    private void OnScaleChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ScaleLabel is not null) ScaleLabel.Text = $"Text scale {ScaleSlider.Value:0.00}×";
        Rebuild();
    }

    private async void OnExportPng(object sender, RoutedEventArgs e) => await ExportAsync(pdf: false);
    private async void OnExportPdf(object sender, RoutedEventArgs e) => await ExportAsync(pdf: true);

    private async Task ExportAsync(bool pdf)
    {
        int scale = ResCombo.SelectedIndex == 1 ? 4 : 2;
        CubeExportPlate? raster = null;
        try
        {
            StatusLabel.Text = "Rendering…";

            // Rasterize a FRESH plate at full natural size — NOT the preview plate, which is scaled
            // down inside the Viewbox (that would soften the text). This keeps the export font crisp.
            raster = new CubeExportPlate();
            raster.Populate(_frame, _frameW, _frameH, _data, CurrentStyle());
            RasterHost.Children.Add(raster);
            Canvas.SetLeft(raster, -100000);
            raster.UpdateLayout();

            int reqW = (int)Math.Ceiling(raster.ActualWidth * scale);
            int reqH = (int)Math.Ceiling(raster.ActualHeight * scale);
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(raster, reqW, reqH);
            int rw = rtb.PixelWidth, rh = rtb.PixelHeight;
            byte[] buf = (await rtb.GetPixelsAsync()).ToArray();
            if (rw <= 0 || rh <= 0 || buf.Length < (long)rw * rh * 4)
            {
                StatusLabel.Text = scale >= 4 ? "Too large — try 2×." : "Render failed.";
                return;
            }

            var hwnd = WindowHelper.ActiveWindows.Count > 0
                ? WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]) : nint.Zero;
            if (hwnd == nint.Zero) { StatusLabel.Text = "No window handle."; return; }

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedFileName = _baseName;
            picker.FileTypeChoices.Add(pdf ? "PDF document" : "PNG image", new List<string> { pdf ? ".pdf" : ".png" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) { StatusLabel.Text = string.Empty; return; }

            if (pdf)
            {
                // PDF has no alpha here, so flatten a transparent figure onto white paper.
                var rgb = TransparentToggle.IsOn
                    ? PdfImageWriter.BgraToRgbOverWhite(buf, rw, rh)
                    : PdfImageWriter.BgraToRgb(buf, rw, rh);
                using var fs = await file.OpenStreamForWriteAsync();
                fs.SetLength(0);
                PdfImageWriter.Write(fs, rgb, rw, rh);
            }
            else
            {
                using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)rw, (uint)rh, 96, 96, buf);
                await encoder.FlushAsync();
            }
            StatusLabel.Text = "Saved " + file.Name;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Failed: " + ex.Message;
        }
        finally
        {
            if (raster is not null) RasterHost.Children.Remove(raster);
        }
    }
}
