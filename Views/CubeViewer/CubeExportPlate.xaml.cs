using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// A publication figure "plate" for cube export: a header (brand · title | filename · date), the
/// framed render (volume + box + captions, already composited), and a legend footer (object /
/// instrument / facts, per-axis WCS ranges, and a labeled colorbar). Rasterized off-screen via
/// <c>RenderTargetBitmap</c> to PNG/PDF. The Windows analogue of the macOS CubeExportPlate /
/// v-cube composePlate.
/// </summary>
public sealed partial class CubeExportPlate : UserControl
{
    public CubeExportPlate() => InitializeComponent();

    /// <summary>All text + colorbar content for the plate.</summary>
    public struct PlateData
    {
        public string Title;            // object name (or filename)
        public string Subtitle;         // instrument
        public string FileName;
        public string DateText;
        public string Facts;            // "nx×ny×nz · NaN x% · Resident"
        public List<(string Name, string Range)> AxisRanges; // RA/DEC/SPECTRAL
        public string CbMin;            // min value label
        public string CbMax;            // max value + unit label
        public string CbStretch;        // "ASINH · INFERNO"
        public byte[] ColorbarLut;      // 256×4 RGBA
    }

    /// <summary>Lay out the plate for a frame of <paramref name="frameW"/>×<paramref name="frameH"/> pixels.</summary>
    public void Populate(WriteableBitmap frame, int frameW, int frameH, PlateData d, bool dark)
    {
        // Palettes ported from v-cube (cockpit dark / journal light).
        Color bg = dark ? C(0xFF, 0x04, 0x07, 0x0C) : C(0xFF, 0xFF, 0xFF, 0xFF);
        Color text = dark ? C(0xFF, 0xD7, 0xF0, 0xFF) : C(0xFF, 0x14, 0x18, 0x1C);
        Color dim = dark ? C(0xFF, 0x6B, 0x8A, 0x9C) : C(0xFF, 0x5A, 0x64, 0x6C);
        Color accent = dark ? C(0xFF, 0x56, 0xC8, 0xFF) : C(0xFF, 0x14, 0x18, 0x1C); // light: accent = text
        Color line = dark ? C(0x47, 0x56, 0xC8, 0xFF) : C(0x73, 0x14, 0x18, 0x1C);   // rgba(.,.,.,.28/.45)

        double pad = Math.Max(18, frameW * 0.018);
        double titleF = Math.Max(15, frameW * 0.013);
        double bodyF = Math.Max(11, frameW * 0.0078);
        double capF = Math.Max(10, frameW * 0.0066);
        var mono = new FontFamily("Consolas");

        RootBorder.Background = B(bg);
        RootBorder.Padding = new Thickness(pad);
        Width = frameW + 2 * pad;

        // Header brand (the cube object/title lives in the legend footer below).
        BrandTitle.Inlines.Clear();
        BrandTitle.Inlines.Add(new Run { Text = "◈ CANFAR CUBE", Foreground = B(accent), FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        BrandTitle.FontSize = titleF;

        FileDate.Text = string.IsNullOrEmpty(d.FileName) ? d.DateText : $"{d.FileName} · {d.DateText}";
        FileDate.Foreground = B(dim);
        FileDate.FontSize = capF;
        FileDate.FontFamily = mono;

        Divider.Fill = B(line);
        Divider.Height = Math.Max(1, frameW * 0.0007);

        // Frame.
        FrameBorder.BorderBrush = B(line);
        FrameImage.Source = frame;
        FrameImage.Width = frameW;
        FrameImage.Height = frameH;

        // Legend.
        LegendTitle.Text = d.Title;
        LegendTitle.Foreground = B(text);
        LegendTitle.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        LegendTitle.FontSize = bodyF * 1.15;

        LegendSubtitle.Text = d.Subtitle;
        LegendSubtitle.Foreground = B(dim);
        LegendSubtitle.FontSize = capF;
        LegendSubtitle.Visibility = string.IsNullOrEmpty(d.Subtitle) ? Visibility.Collapsed : Visibility.Visible;

        LegendFacts.Text = d.Facts;
        LegendFacts.Foreground = B(dim);
        LegendFacts.FontSize = capF;
        LegendFacts.FontFamily = mono;

        // Axis ranges (name accent, value dim).
        AxisRangesPanel.Children.Clear();
        foreach (var (name, range) in d.AxisRanges ?? new())
        {
            var tb = new TextBlock { FontSize = capF, FontFamily = mono };
            tb.Inlines.Add(new Run { Text = name + "  ", Foreground = B(accent) });
            tb.Inlines.Add(new Run { Text = range, Foreground = B(dim) });
            AxisRangesPanel.Children.Add(tb);
        }

        // Colorbar.
        ColorbarPanel.Width = Math.Max(160, frameW * 0.18);
        CbStretch.Text = d.CbStretch;
        CbStretch.Foreground = B(dim);
        CbStretch.FontSize = capF * 0.92;
        CbStretch.FontFamily = mono;

        ColorbarRect.Height = Math.Max(8, frameW * 0.004);
        ColorbarRect.Stroke = B(line);
        ColorbarRect.StrokeThickness = 1;
        ColorbarRect.Fill = GradientFromLut(d.ColorbarLut);

        CbMin.Text = d.CbMin;
        CbMax.Text = d.CbMax;
        CbMin.Foreground = CbMax.Foreground = B(dim);
        CbMin.FontSize = CbMax.FontSize = capF;
        CbMin.FontFamily = CbMax.FontFamily = mono;
    }

    private static LinearGradientBrush GradientFromLut(byte[] lut)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        if (lut is null || lut.Length < 256 * 4) return brush;
        const int stops = 17;
        for (int s = 0; s < stops; s++)
        {
            int idx = s * 255 / (stops - 1), o = idx * 4;
            brush.GradientStops.Add(new GradientStop
            {
                Color = C(255, lut[o], lut[o + 1], lut[o + 2]),
                Offset = s / (double)(stops - 1),
            });
        }
        return brush;
    }

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private static SolidColorBrush B(Color c) => new(c);
}
