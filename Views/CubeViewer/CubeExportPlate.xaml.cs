using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// A publication figure "plate" for cube export: header (title / instrument / channel | filename /
/// date), the framed render (a transparent volume snapshot) with a LIVE box + WCS caption overlay,
/// and a footer (labeled colorbar + a metadata grid). Fully themed via <see cref="PlateStyle"/> so a
/// modal can preview it live and rasterize the same control to PNG/PDF. Windows analogue of the
/// macOS CubeExportPlate.
/// </summary>
public sealed partial class CubeExportPlate : UserControl
{
    public CubeExportPlate() => InitializeComponent();

    /// <summary>Content for the plate (text + colorbar + the camera/metadata for the live overlay).</summary>
    public struct PlateData
    {
        public string Title;            // object name (or filename)
        public string Subtitle;         // instrument
        public string ChannelText;      // "CH n/N · spectral value"
        public string FileName;
        public string DateText;
        public string Dims;             // "nx × ny × nz"
        public string NanText;          // "x.x%"
        public string ModeText;         // "Resident"
        public List<(string Name, string Range)> AxisRanges; // RA/DEC/SPECTRAL
        public string CbMin;            // colorbar min label
        public string CbMax;            // colorbar max label (+ unit)
        public string CbStretch;        // "ASINH · INFERNO"
        public byte[] ColorbarLut;      // 256×4 RGBA

        // Live box + caption overlay (export camera, already pulled back).
        public float Az, El, Dist, SpectralScale;
        public int VolNx, VolNy;
        public CubeMetadata? Meta;
        public bool CaptionsOn;
    }

    /// <summary>User-tunable figure style (theme / font / text color / scale / annotations / transparency).</summary>
    public struct PlateStyle
    {
        public bool Dark;          // cockpit dark vs journal light
        public string Font;        // "sans" | "mono" | "serif"
        public string TextColor;   // "auto" | "white" | "black" | "cyan" | "amber"
        public double TextScale;   // 0.75 .. 1.5
        public bool Annotate;      // header + footer visible
        public bool Transparent;   // no background fill

        public static PlateStyle Default => new()
        {
            Dark = true, Font = "sans", TextColor = "auto", TextScale = 1.0, Annotate = true, Transparent = false,
        };
    }

    /// <summary>Lay out + theme the plate for a frame of <paramref name="frameW"/>×<paramref name="frameH"/> px.</summary>
    public void Populate(WriteableBitmap frame, int frameW, int frameH, PlateData d, PlateStyle s)
    {
        // Theme palette (macOS journal/cockpit).
        Color bg = s.Dark ? C(0xFF, 0x0D, 0x0D, 0x0D) : C(0xFF, 0xFF, 0xFF, 0xFF);
        Color themeText = s.Dark ? C(0xFF, 0xFF, 0xFF, 0xFF) : C(0xFF, 0x14, 0x14, 0x14);
        Color themeDim = s.Dark ? C(0xFF, 0x9E, 0x9E, 0x9E) : C(0xFF, 0x6B, 0x6B, 0x6B);
        Color line = s.Dark ? C(0xFF, 0x4D, 0x4D, 0x4D) : C(0xFF, 0xC7, 0xC7, 0xC7);

        Color main = ResolveTextColor(s.TextColor, themeText);
        Color dim = s.TextColor == "auto" ? themeDim : C(0xA6, main.R, main.G, main.B); // 65% of main

        double sc = Math.Clamp(s.TextScale, 0.5, 2.0);
        double titleF = frameW * 0.020 * sc;
        double smallF = frameW * 0.0095 * sc;
        var fam = ResolveFont(s.Font);

        double pad = Math.Max(18, frameW * 0.018);
        RootBorder.Padding = new Thickness(pad);
        RootBorder.Background = s.Transparent ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) : B(bg);
        Width = frameW + 2 * pad;

        // ── Header ──
        SetText(HdrTitle, d.Title, fam, titleF, main, Microsoft.UI.Text.FontWeights.SemiBold);
        SetText(HdrInstrument, d.Subtitle, fam, smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(HdrChannel, d.ChannelText, new FontFamily("Consolas"), smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(HdrBrand, "◈ VERBINAL", fam, smallF, dim, Microsoft.UI.Text.FontWeights.Medium);
        SetText(HdrFile, d.FileName, new FontFamily("Consolas"), smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(HdrDate, d.DateText, new FontFamily("Consolas"), smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        HdrInstrument.Visibility = string.IsNullOrEmpty(d.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        HdrChannel.Visibility = string.IsNullOrEmpty(d.ChannelText) ? Visibility.Collapsed : Visibility.Visible;

        DividerTop.Fill = B(line);
        DividerBot.Fill = B(line);
        DividerTop.Height = DividerBot.Height = Math.Max(1, frameW * 0.0006);

        // ── Frame + live overlay ──
        FrameBorder.BorderBrush = B(line);
        FrameImage.Source = frame;
        FrameImage.Width = frameW;
        FrameImage.Height = frameH;
        BuildCaptionOverlay(frameW, frameH, d, s.Dark);

        // ── Footer: colorbar + metadata grid ──
        ColorbarRect.Width = Math.Max(150, frameW * 0.14);
        ColorbarRect.Height = Math.Max(10, frameW * 0.0085);
        ColorbarRect.Stroke = B(line);
        ColorbarRect.StrokeThickness = 1;
        ColorbarRect.Fill = GradientFromLut(d.ColorbarLut);
        SetText(CbMin, d.CbMin, new FontFamily("Consolas"), smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(CbMax, d.CbMax, new FontFamily("Consolas"), smallF, dim, Microsoft.UI.Text.FontWeights.Normal);
        SetText(CbStretch, d.CbStretch, new FontFamily("Consolas"), smallF, dim, Microsoft.UI.Text.FontWeights.Normal);

        BuildMetaGrid(d, fam, smallF, main, dim);

        // ── Annotations toggle ──
        var ann = s.Annotate ? Visibility.Visible : Visibility.Collapsed;
        HeaderGrid.Visibility = ann;
        DividerTop.Visibility = ann;
        DividerBot.Visibility = ann;
        FooterPanel.Visibility = ann;
        RootBorder.Padding = s.Annotate ? new Thickness(pad) : new Thickness(Math.Max(2, frameW * 0.004));
    }

    private void BuildCaptionOverlay(int frameW, int frameH, PlateData d, bool dark)
    {
        CaptionOverlay.Children.Clear();
        CaptionOverlay.Width = frameW;
        CaptionOverlay.Height = frameH;
        if (!d.CaptionsOn) return;

        var frame = new CubeAxesOverlay.Frame();
        CubeAxesOverlay.Build(frame, d.Az, d.El, d.Dist, d.SpectralScale, d.VolNx, d.VolNy, d.Meta, frameW, frameH);

        // Captions/edges re-theme so they stay legible where they fall on the plate margins:
        // light cyan/white + black halo on dark; darker blue/ink + white halo on light.
        Color axisColor = dark ? C(0xFF, 0x73, 0xD9, 0xFF) : C(0xFF, 0x12, 0x5C, 0x99);
        Color valueColor = dark ? C(0xF5, 0xFF, 0xFF, 0xFF) : C(0xFF, 0x1A, 0x1A, 0x1A);
        Color haloColor = dark ? C(0xFF, 0x00, 0x00, 0x00) : C(0xC0, 0xFF, 0xFF, 0xFF);
        var edgeBrush = B(dark ? C(0x66, 0x9F, 0xC4, 0xE8) : C(0x80, 0x2E, 0x5E, 0x8C));

        double edgeT = Math.Max(1, frameW / 1600.0);
        foreach (var (a, b) in frame.Edges)
            if (a.Visible && b.Visible)
                CaptionOverlay.Children.Add(new Line { Stroke = edgeBrush, StrokeThickness = edgeT, X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y });

        double capF = Math.Max(11, frameW * 0.011);
        var mono = new FontFamily("Consolas");
        foreach (var cap in frame.Captions)
        {
            if (!cap.At.Visible || string.IsNullOrEmpty(cap.Text)) continue;
            var weight = cap.IsAxisName ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            var color = cap.IsAxisName ? axisColor : valueColor;
            var mainTb = new TextBlock { Text = cap.Text, FontFamily = mono, FontSize = capF, FontWeight = weight, Foreground = B(color) };
            var shadow = new TextBlock { Text = cap.Text, FontFamily = mono, FontSize = capF, FontWeight = weight, Foreground = B(haloColor) };
            mainTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = mainTb.DesiredSize.Width, th = mainTb.DesiredSize.Height;
            double x = Math.Clamp(cap.At.X - tw / 2, 4, Math.Max(4, frameW - tw - 4));
            double y = Math.Clamp(cap.At.Y - th / 2, 4, Math.Max(4, frameH - th - 4));
            Canvas.SetLeft(shadow, x + 1); Canvas.SetTop(shadow, y + 1);
            Canvas.SetLeft(mainTb, x); Canvas.SetTop(mainTb, y);
            CaptionOverlay.Children.Add(shadow);
            CaptionOverlay.Children.Add(mainTb);
        }
    }

    private void BuildMetaGrid(PlateData d, FontFamily fam, double smallF, Color main, Color dim)
    {
        MetaPanel.Children.Clear();
        AddMetaColumn(Helpers.Loc.T("Cube_PlateDims"), d.Dims, fam, smallF, main, dim);
        foreach (var (name, range) in d.AxisRanges ?? new())
            AddMetaColumn(name, range, fam, smallF, main, dim);
        AddMetaColumn("NaN", d.NanText, fam, smallF, main, dim); // NaN is a technical term — not localized
        AddMetaColumn(Helpers.Loc.T("Cube_PlateMode"), d.ModeText, fam, smallF, main, dim);
    }

    private void AddMetaColumn(string key, string value, FontFamily fam, double smallF, Color main, Color dim)
    {
        if (string.IsNullOrEmpty(value)) return;
        var sp = new StackPanel { Spacing = 1 };
        sp.Children.Add(new TextBlock { Text = key, FontFamily = fam, FontSize = smallF * 0.86, Foreground = B(dim) });
        sp.Children.Add(new TextBlock { Text = value, FontFamily = new FontFamily("Consolas"), FontSize = smallF, Foreground = B(main) });
        MetaPanel.Children.Add(sp);
    }

    private static void SetText(TextBlock tb, string text, FontFamily fam, double size, Color color, Windows.UI.Text.FontWeight weight)
    {
        tb.Text = text;
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

    private static LinearGradientBrush GradientFromLut(byte[] lut)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        if (lut is null || lut.Length < 256 * 4) return brush;
        const int stops = 17;
        for (int s = 0; s < stops; s++)
        {
            int idx = s * 255 / (stops - 1), o = idx * 4;
            brush.GradientStops.Add(new GradientStop { Color = C(255, lut[o], lut[o + 1], lut[o + 2]), Offset = s / (double)(stops - 1) });
        }
        return brush;
    }

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private static SolidColorBrush B(Color c) => new(c);
}
