using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// A publication figure "plate" for FITS export: header (object / instrument / what region this is |
/// filename / date), the framed picture with the MARKS drawn live over it, and a footer (labelled
/// colorbar with the cut levels, then where on the sky the picture is).
///
/// Deliberately the same shape as <see cref="CubeViewer.CubeExportPlate"/>: a figure from the cube
/// viewer and one from the FITS viewer end up in the same paper, and two house styles in one figure set
/// is the kind of thing a referee notices before the science.
///
/// The marks are NOT baked into the bitmap. They are drawn by the same
/// <see cref="Controls.AnnotationLayer"/> the viewer uses, over a
/// <see cref="FitsExportSurface"/> carrying the export's ink scale — so they re-theme with the figure
/// and they are drawn at the size the figure needs rather than the size the screen had.
/// </summary>
public sealed partial class FitsExportPlate : UserControl
{
    public FitsExportPlate() => InitializeComponent();

    /// <summary>Content for the plate: the text, the colorbar, and the marks with the geometry to place them.</summary>
    public struct PlateData
    {
        public string Title;            // object name (or filename)
        public string Subtitle;         // instrument / telescope
        public string RegionText;       // "REGION 512×512 px · 8.5′ × 8.5′" or "FULL FRAME"
        public string FileName;
        public string DateText;
        public string CbMin;            // low cut, formatted
        public string CbMax;            // high cut (+ unit)
        public string CbStretch;        // "LINEAR · VIRIDIS"
        public byte[] ColorbarLut;      // 256×4 RGBA

        /// <summary>Footer facts: centre, field of view, pixel scale, filter, exposure.</summary>
        public List<(string Key, string Value)> Facts;

        /// <summary>The marks, and the region + WCS that place them on the frame.</summary>
        public IReadOnlyList<Annotation> Marks;
        public FitsRegion Region;
        public WcsInfo? Wcs;
        public int ImageHeight;
    }

    /// <summary>User-tunable figure style. Mirrors the cube plate's, so the two dialogs offer the same choices.</summary>
    public struct PlateStyle
    {
        public bool Dark;
        public string Font;        // "sans" | "mono" | "serif"
        public string TextColor;   // "auto" | "white" | "black" | "cyan" | "amber"
        public double TextScale;   // 0.75 .. 1.5
        public bool Annotate;      // header + footer visible
        public bool ShowMarks;     // the marks themselves
        public bool Transparent;

        public static PlateStyle Default => new()
        {
            Dark = true, Font = "sans", TextColor = "auto", TextScale = 1.0,
            Annotate = true, ShowMarks = true, Transparent = false,
        };
    }

    /// <summary>
    /// Lay out and theme the plate for a frame of <paramref name="frameW"/>×<paramref name="frameH"/> px.
    ///
    /// <paramref name="inkScale"/> is what the marks are drawn at: the export's scale factor, so a 4×
    /// figure gets 4× strokes and labels rather than screen-sized ones on a quadrupled plate.
    /// </summary>
    public void Populate(WriteableBitmap frame, int frameW, int frameH, PlateData d, PlateStyle s, double inkScale)
    {
        Color bg = s.Dark ? C(0xFF, 0x0D, 0x0D, 0x0D) : C(0xFF, 0xFF, 0xFF, 0xFF);
        Color themeText = s.Dark ? C(0xFF, 0xFF, 0xFF, 0xFF) : C(0xFF, 0x14, 0x14, 0x14);
        Color themeDim = s.Dark ? C(0xFF, 0x9E, 0x9E, 0x9E) : C(0xFF, 0x6B, 0x6B, 0x6B);
        Color line = s.Dark ? C(0xFF, 0x4D, 0x4D, 0x4D) : C(0xFF, 0xC7, 0xC7, 0xC7);

        Color main = ResolveTextColor(s.TextColor, themeText);
        Color dim = s.TextColor == "auto" ? themeDim : C(0xA6, main.R, main.G, main.B);

        double sc = Math.Clamp(s.TextScale, 0.5, 2.0);
        double titleF = frameW * 0.020 * sc;
        double smallF = frameW * 0.0095 * sc;
        var fam = ResolveFont(s.Font);
        var mono = new FontFamily("Consolas");

        double pad = Math.Max(18, frameW * 0.018);
        RootBorder.Background = s.Transparent ? new SolidColorBrush(C(0, 0, 0, 0)) : B(bg);
        Width = frameW + 2 * pad;

        // ── Header ──
        SetText(HdrTitle, d.Title, fam, titleF, main, Microsoft.UI.Text.FontWeights.SemiBold);
        SetText(HdrInstrument, d.Subtitle, fam, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(HdrRegion, d.RegionText, mono, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(HdrBrand, "◈ VERBINAL", fam, smallF, dim, Microsoft.UI.Text.FontWeights.Medium);
        SetText(HdrFile, d.FileName, mono, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(HdrDate, d.DateText, mono, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        HdrInstrument.Visibility = string.IsNullOrEmpty(d.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        HdrRegion.Visibility = string.IsNullOrEmpty(d.RegionText) ? Visibility.Collapsed : Visibility.Visible;

        DividerTop.Fill = DividerBot.Fill = B(line);
        DividerTop.Height = DividerBot.Height = Math.Max(1, frameW * 0.0006);

        // ── Frame + the marks over it ──
        FrameBorder.BorderBrush = B(line);
        FrameImage.Source = frame;
        FrameImage.Width = frameW;
        FrameImage.Height = frameH;
        BuildMarkOverlay(frameW, frameH, d, s, inkScale);

        // ── Footer ──
        ColorbarRect.Width = Math.Max(150, frameW * 0.14);
        ColorbarRect.Height = Math.Max(10, frameW * 0.0085);
        ColorbarRect.Stroke = B(line);
        ColorbarRect.StrokeThickness = 1;
        ColorbarRect.Fill = GradientFromLut(d.ColorbarLut);
        SetText(CbMin, d.CbMin, mono, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(CbMax, d.CbMax, mono, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(CbStretch, d.CbStretch, mono, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);

        MetaPanel.Children.Clear();
        foreach (var (key, value) in d.Facts ?? [])
            AddFact(key, value, fam, smallF, main, dim);

        // ── Annotations toggle ──
        var ann = s.Annotate ? Visibility.Visible : Visibility.Collapsed;
        HeaderGrid.Visibility = ann;
        DividerTop.Visibility = ann;
        DividerBot.Visibility = ann;
        FooterPanel.Visibility = ann;
        RootBorder.Padding = s.Annotate ? new Thickness(pad) : new Thickness(Math.Max(2, frameW * 0.004));
    }

    /// <summary>
    /// The marks, drawn by the viewer's own renderer over a surface that maps the region onto the frame.
    ///
    /// The same code as the screen, which is the point: a figure that drew its marks separately would
    /// eventually disagree with the viewer about where one was, and the figure is the artefact that
    /// outlives the session.
    /// </summary>
    private void BuildMarkOverlay(int frameW, int frameH, PlateData d, PlateStyle s, double inkScale)
    {
        MarkOverlay.Width = frameW;
        MarkOverlay.Height = frameH;
        MarkOverlay.EditingId = MarkOverlay.SelectedId = null;   // a figure has no selection

        if (!s.ShowMarks || d.Marks is not { Count: > 0 })
        {
            MarkOverlay.Children.Clear();
            return;
        }

        var surface = new FitsExportSurface(d.Region, frameW, frameH, d.Wcs, d.ImageHeight)
        {
            InkScale = inkScale,
        };

        MarkOverlay.Render(d.Marks, surface, frameW);
    }

    private void AddFact(string key, string value, FontFamily fam, double smallF, Color main, Color dim)
    {
        if (string.IsNullOrEmpty(value)) return;

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(new TextBlock { Text = key, FontFamily = fam, FontSize = smallF * 0.86, Foreground = B(dim) });
        stack.Children.Add(new TextBlock { Text = value, FontFamily = new FontFamily("Consolas"), FontSize = smallF, Foreground = B(main) });
        MetaPanel.Children.Add(stack);
    }

    private static void SetText(TextBlock tb, string text, FontFamily fam, double size, Color color, Windows.UI.Text.FontWeight weight)
    {
        tb.Text = text ?? string.Empty;
        tb.FontFamily = fam;
        tb.FontSize = size;
        tb.Foreground = B(color);
        tb.FontWeight = weight;
    }

    private static FontFamily ResolveFont(string font) => font switch
    {
        "mono" => new FontFamily("Consolas"),
        "serif" => new FontFamily("Cambria"),
        _ => new FontFamily("Segoe UI"),
    };

    private static Color ResolveTextColor(string textColor, Color themeText) => textColor switch
    {
        "white" => C(0xFF, 0xFF, 0xFF, 0xFF),
        "black" => C(0xFF, 0x14, 0x14, 0x14),
        "cyan" => C(0xFF, 0x40, 0xB3, 0xF2),
        "amber" => C(0xFF, 0xF2, 0x99, 0x26),
        _ => themeText,
    };

    /// <summary>The colorbar, as a gradient sampled from the same LUT the picture was rendered with.</summary>
    private static LinearGradientBrush GradientFromLut(byte[] lut)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        if (lut is null || lut.Length < 256 * 4) return brush;

        const int stops = 17;
        for (var s = 0; s < stops; s++)
        {
            var index = s * 255 / (stops - 1);
            var o = index * 4;
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
