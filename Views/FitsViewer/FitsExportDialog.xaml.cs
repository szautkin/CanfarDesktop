using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Models.Fits;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The export modal: a live preview of the plate on the left, the choices that change it on the right,
/// and a Save that writes the same figure at full size.
///
/// The preview is the plate itself, scaled down inside a Viewbox rather than rendered small — so what
/// is on screen is the figure, not an approximation of it. The SAVED figure is rendered separately at
/// its natural size, because scaling a small plate up would soften every glyph on it.
/// </summary>
public sealed partial class FitsExportDialog : ContentDialog
{
    private FitsViewerPage? _page;
    private FitsRegion _region;
    private string _baseName = "figure";

    public FitsExportDialog() => InitializeComponent();

    public void Initialize(FitsViewerPage page, FitsRegion region, string baseName)
    {
        _page = page;
        _region = region;
        _baseName = string.IsNullOrWhiteSpace(baseName) ? "figure" : baseName;

        ThemeCombo.SelectedIndex = 0;
        FontCombo.SelectedIndex = 0;
        ScaleCombo.SelectedIndex = 1;   // 2x: the size most figures actually want

        RegionSummary.Text = $"{region.Width:0} × {region.Height:0} px of the image";

        _ = RefreshPreviewAsync();
    }

    private FitsExportPlate.PlateStyle CurrentStyle() => new()
    {
        Dark = ThemeCombo.SelectedIndex == 0,
        Font = FontCombo.SelectedIndex switch { 1 => "mono", 2 => "serif", _ => "sans" },
        TextColor = "auto",
        TextScale = 1.0,
        Annotate = AnnotateToggle.IsOn,
        ShowMarks = MarksToggle.IsOn,
        Transparent = TransparentToggle.IsOn,
    };

    private int CurrentScale()
        => ScaleCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var scale)
            ? Math.Clamp(scale, 1, 4)
            : 2;

    private void OnStyleChanged(object sender, RoutedEventArgs e) => _ = RefreshPreviewAsync();

    /// <summary>
    /// Rebuild the preview plate.
    ///
    /// Always at 1× whatever the export scale is: the preview's job is to show the LAYOUT, and a 4×
    /// plate shown at a quarter size is the same picture rendered four times as slowly.
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        if (_page is null) return;

        var built = await _page.BuildPreviewPlateAsync(_region, CurrentStyle());
        PreviewBox.Child = built;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_page is null) return;

        SaveButton.IsEnabled = false;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = _baseName + "-figure" };
            picker.FileTypeChoices.Add("PNG image", [".png"]);
            picker.FileTypeChoices.Add("PDF document", [".pdf"]);

            // A picker needs an owning window, and a dialog has none of its own.
            var hwnd = ActiveWindows.Count > 0 ? WindowNative.GetWindowHandle(ActiveWindows[0]) : nint.Zero;
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var format = System.IO.Path.GetExtension(file.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "pdf" : "png";

            var error = await _page.ExportRegionToPathAsync(_region, file.Path, format, CurrentScale(), CurrentStyle());

            ResultBar.Severity = error is null ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            ResultBar.Message = error ?? $"Saved {System.IO.Path.GetFileName(file.Path)}";
            ResultBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ResultBar.Severity = InfoBarSeverity.Error;
            ResultBar.Message = ex.Message;
            ResultBar.IsOpen = true;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
