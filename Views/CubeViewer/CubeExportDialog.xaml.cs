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

    public CubeExportDialog()
    {
        InitializeComponent();
        Opened += FitToWindow;
    }

    /// <summary>
    /// Take the size the window can actually give, rather than the size this dialog would prefer.
    ///
    /// A ContentDialog clips content it cannot fit rather than shrinking it, so a fixed size is a
    /// promise the window may not be able to keep — and what gets clipped is the bottom, which is
    /// where the buttons are. Done on Opened, because that is when the root is measurable.
    /// </summary>
    private void FitToWindow(object sender, ContentDialogOpenedEventArgs args)
    {
        if (XamlRoot is null) return;

        DialogRoot.Width = Helpers.DialogSize.Fit(XamlRoot.Size.Width, PreferredWidth, DialogRoot.MinWidth);
        DialogRoot.Height = Helpers.DialogSize.Fit(XamlRoot.Size.Height, PreferredHeight, DialogRoot.MinHeight);
    }

    /// <summary>What this dialog asks for when there is room. Matches the size set in its markup.</summary>
    private const double PreferredWidth = 1000, PreferredHeight = 600;


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
        // Localized initial label (code owns this text at runtime, so no x:Uid on it).
        ScaleLabel.Text = Helpers.Loc.F("Cube_ExpTextScale", ScaleSlider.Value);
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
        ShowMarks = MarksToggle.IsOn,
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
        if (ScaleLabel is not null) ScaleLabel.Text = Helpers.Loc.F("Cube_ExpTextScale", ScaleSlider.Value);
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
            StatusLabel.Text = Helpers.Loc.T("Cube_ExpRendering");

            // Rasterize a FRESH plate at full natural size — NOT the preview plate, which is scaled
            // down inside the Viewbox (that would soften the text). This keeps the export font crisp.
            raster = new CubeExportPlate();
            raster.Populate(_frame, _frameW, _frameH, _data, CurrentStyle());
            RasterHost.Children.Add(raster);
            Canvas.SetLeft(raster, -100000);
            raster.UpdateLayout();

            // Rendered in pieces when the figure is past what one rasterisation can produce, which
            // is how 4x becomes an actual 4x rather than the 2.8x the limit used to quietly impose.
            var rendered = await Views.Controls.PlateRasterizer.RenderAsync(
                raster, RasterHost, scale, expectOpaque: !TransparentToggle.IsOn);
            if (rendered is not { } figure)
            {
                StatusLabel.Text = scale >= 4 ? Helpers.Loc.T("Cube_ExpTooLarge") : Helpers.Loc.T("Cube_ExpRenderFailed");
                return;
            }

            int rw = figure.Width, rh = figure.Height;
            byte[] buf = figure.Pixels;

            var hwnd = WindowHelper.ActiveWindows.Count > 0
                ? WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]) : nint.Zero;
            if (hwnd == nint.Zero) { StatusLabel.Text = Helpers.Loc.T("Cube_ExpNoWindow"); return; }

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedFileName = _baseName;
            picker.FileTypeChoices.Add(
                pdf ? Helpers.Loc.T("Cube_ExpPdfType") : Helpers.Loc.T("Cube_ExpPngType"),
                new List<string> { pdf ? ".pdf" : ".png" });
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
            // Say what was really produced when the limit got in the way, rather than letting the
            // 4x button quietly hand back something closer to 3x.
            // Say what was really produced when the figure had to be reduced to something the
            // machine could assemble, rather than letting the 4x button quietly hand back less.
            StatusLabel.Text = figure.Scale < scale - 1e-9
                ? Helpers.Loc.F("Cube_SavedLimited", file.Name, $"{figure.Scale:0.#}", scale, rw, rh)
                : Helpers.Loc.F("Cube_Saved", file.Name);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = Helpers.Loc.F("Cube_ExpFailed", ex.Message);
        }
        finally
        {
            if (raster is not null) RasterHost.Children.Remove(raster);
        }
    }
}
