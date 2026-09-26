using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Cutouts;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Choose a cutout of one file: its region by numbers or by drawing, its band when the file has one,
/// checked as it is typed, with an idea of its size.
///
/// <para>Edits one <see cref="CutoutSpec"/>, the file as its <see cref="ICutoutSource"/> describes it
/// deciding which fields there are. The fields and the drawing are two views of that one spec — editing
/// either updates the other — and the source's <see cref="ICutoutSource.Check"/> judges it, the same
/// judgement an agent's request meets. It does not download: it says <see cref="DownloadRequested"/>,
/// and whoever placed it hands the cutout to the app.</para>
///
/// <para>Every control is named, so an agent can point at it.</para>
/// </summary>
public sealed class CutoutEditor : UserControl
{
    private static readonly (string Label, double Degrees)[] AngleUnits = [("″", 1.0 / 3600), ("′", 1.0 / 60), ("°", 1.0)];
    private static readonly (string Label, double Metres)[] WaveUnits = [("nm", 1e-9), ("µm", 1e-6), ("mm", 1e-3)];

    private readonly ICutoutSource _source;
    private readonly CutoutSpec _suggested;
    private CutoutSpec _spec;
    private bool _syncing;

    /// <summary>
    /// The fields' text as this editor last wrote it. TextBox raises TextChanged after the fact, not
    /// while the text is being set, so a flag alone cannot tell the editor's own writes from typing:
    /// text still equal to what was written is not an edit.
    /// </summary>
    private string _written = string.Empty;

    private readonly RadioButtons _shape = new() { Name = "CutoutShapeChoice", MaxColumns = 2 };
    private readonly TextBox _ra = Field("CutoutRaBox", "Cutout_Ra");
    private readonly TextBox _dec = Field("CutoutDecBox", "Cutout_Dec");
    private readonly TextBox _radius = Field("CutoutRadiusBox", "Cutout_Radius");
    private readonly TextBox _width = Field("CutoutWidthBox", "Cutout_Width");
    private readonly TextBox _height = Field("CutoutHeightBox", "Cutout_Height");
    private readonly ComboBox _sizeUnit = new() { Name = "CutoutSizeUnit", VerticalAlignment = VerticalAlignment.Bottom };
    private readonly StackPanel _circleSize = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _boxSize = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBox? _bandMin;
    private readonly TextBox? _bandMax;
    private readonly ComboBox? _bandUnit;
    private readonly FootprintCanvas _sky = new() { Name = "CutoutSky", IsEditable = true, Height = 220, MinWidth = 240 };
    private readonly TextBlock _errors = Caption("SystemFillColorCriticalBrush");
    private readonly TextBlock _warnings = Caption("SystemFillColorCautionBrush");
    private readonly TextBlock _estimate = Caption("TextFillColorSecondaryBrush");
    private readonly Button _download = new() { Name = "CutoutDownloadButton", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

    /// <summary>The person asked for this cutout.</summary>
    public event Action<CutoutSpec>? DownloadRequested;

    /// <summary>The person closed the editor.</summary>
    public event Action? CloseRequested;

    public CutoutEditor(ICutoutSource source, CutoutSpec suggested, SkyPoint? target)
    {
        _source = source;
        _suggested = source.Bind(suggested);
        _spec = _suggested;
        var file = source.File;

        _shape.Header = Loc.T("Cutout_Shape");
        _shape.Items.Add(Loc.T("Cutout_ShapeCircle"));
        _shape.Items.Add(Loc.T("Cutout_ShapeBox"));
        _shape.SelectedIndex = suggested.Region?.Shape == SkyShape.Box ? 1 : 0;

        foreach (var (label, _) in AngleUnits) _sizeUnit.Items.Add(label);
        _sizeUnit.SelectedIndex = 1; // arcminutes: what a cutout is usually measured in
        AutomationProperties.SetName(_sizeUnit, Loc.T("Cutout_Unit"));

        _circleSize.Children.Add(_radius);
        _boxSize.Children.Add(_width);
        _boxSize.Children.Add(_height);

        var fields = new StackPanel { Spacing = 10, MinWidth = 280 };
        fields.Children.Add(_shape);
        fields.Children.Add(Row(_ra, _dec));
        fields.Children.Add(Row(_circleSize, _boxSize, _sizeUnit));

        if (file.Supports("BAND"))
        {
            _bandMin = Field("CutoutBandMinBox", "Cutout_BandFrom");
            _bandMax = Field("CutoutBandMaxBox", "Cutout_BandTo");
            _bandUnit = new ComboBox { Name = "CutoutBandUnit", VerticalAlignment = VerticalAlignment.Bottom };
            foreach (var (label, _) in WaveUnits) _bandUnit.Items.Add(label);
            _bandUnit.SelectedIndex = BandUnitFor(file.BandMax ?? file.BandMin);
            AutomationProperties.SetName(_bandUnit, Loc.T("Cutout_Unit"));

            var band = new StackPanel { Spacing = 2 };
            band.Children.Add(new TextBlock { Text = Loc.T("Cutout_Band"), Style = Sty("BodyStrongTextBlockStyle") });
            band.Children.Add(Row(_bandMin, _bandMax, _bandUnit));
            band.Children.Add(Caption("TextFillColorTertiaryBrush", Loc.T("Cutout_BandHint")));
            fields.Children.Add(band);
        }

        _sky.Footprint = file.Footprint?.Outline() ?? [];
        _sky.Target = target;
        _sky.DrawShape = _shape.SelectedIndex == 1 ? SkyShape.Box : SkyShape.Circle;
        AutomationProperties.SetName(_sky, Loc.T("Cutout_SkyName"));
        var skyColumn = new StackPanel { Spacing = 4 };
        skyColumn.Children.Add(_sky);
        skyColumn.Children.Add(Caption("TextFillColorTertiaryBrush", Loc.T("Cutout_SkyHint")));

        var body = new Grid { ColumnSpacing = 16 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(skyColumn, 1);
        body.Children.Add(fields);
        body.Children.Add(skyColumn);

        _download.Content = Loc.T("Cutout_Download");
        _download.Click += (_, _) => DownloadRequested?.Invoke(_spec);
        var reset = new Button { Name = "CutoutResetButton", Content = Loc.T("Cutout_Reset") };
        reset.Click += (_, _) => Load(_suggested);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_download);
        buttons.Children.Add(reset);

        var close = new Button
        {
            Name = "CutoutCloseButton",
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetName(close, Loc.T("Cutout_Close"));
        ToolTipService.SetToolTip(close, Loc.T("Cutout_Close"));
        close.Click += (_, _) => CloseRequested?.Invoke();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = Loc.F("Cutout_EditorTitle", file.FileName),
            Style = Sty("SubtitleTextBlockStyle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(header);
        stack.Children.Add(Caption("TextFillColorSecondaryBrush", Loc.T("Cutout_EditorNote")));
        stack.Children.Add(body);
        stack.Children.Add(_errors);
        stack.Children.Add(_warnings);
        stack.Children.Add(_estimate);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Child = stack,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Res("AccentFillColorDefaultBrush"),
            Background = Res("CardBackgroundFillColorDefaultBrush"),
        };

        foreach (var box in new[] { _ra, _dec, _radius, _width, _height, _bandMin, _bandMax })
            if (box is not null) box.TextChanged += (_, _) => FromFields();
        _sizeUnit.SelectionChanged += (_, _) => { if (!_syncing) WriteFields(_spec); };
        if (_bandUnit is not null) _bandUnit.SelectionChanged += (_, _) => { if (!_syncing) WriteFields(_spec); };
        _shape.SelectionChanged += (_, _) => OnShapeChanged();
        _sky.RegionChanged += region => Apply(_spec with { Region = region }, fromDrawing: true);

        Load(suggested);
    }

    /// <summary>The cutout as it stands.</summary>
    public CutoutSpec Spec => _spec;

    /// <summary>Show this cutout — the suggestion, or one an agent proposes.</summary>
    public void Load(CutoutSpec spec) => Apply(spec, fromDrawing: false);

    private void Apply(CutoutSpec spec, bool fromDrawing)
    {
        _spec = _source.Bind(spec);
        if (!fromDrawing) _sky.Region = _spec.Region;
        WriteFields(_spec);
        Evaluate(fieldError: null);
    }

    private void OnShapeChanged()
    {
        if (_syncing) return;
        var box = _shape.SelectedIndex == 1;
        _sky.DrawShape = box ? SkyShape.Box : SkyShape.Circle;

        // The same patch of sky in the other shape: a circle's box is its diameter square, a box's circle reaches its far side.
        if (_spec.Region is { } r)
        {
            var c = r.Centre;
            var next = box
                ? SkyRegion.Box(c.Ra, c.Dec, 2 * r.Reach, 2 * r.Reach)
                : SkyRegion.Circle(c.Ra, c.Dec, r.Shape == SkyShape.Box ? Math.Max(r.Width, r.Height) / 2 : r.Reach);
            Load(_spec with { Region = next });
        }
    }

    private void WriteFields(CutoutSpec spec)
    {
        _syncing = true;
        try
        {
            var box = spec.Region?.Shape == SkyShape.Box;
            _shape.SelectedIndex = box ? 1 : 0;
            _circleSize.Visibility = box ? Visibility.Collapsed : Visibility.Visible;
            _boxSize.Visibility = box ? Visibility.Visible : Visibility.Collapsed;

            if (spec.Region is { } r)
            {
                var c = r.Centre;
                _ra.Text = c.Ra.ToString("0.00000", CultureInfo.InvariantCulture);
                _dec.Text = c.Dec.ToString("+0.00000;-0.00000", CultureInfo.InvariantCulture);
                var unit = AngleUnits[Math.Max(0, _sizeUnit.SelectedIndex)].Degrees;
                _radius.Text = Size(r.Shape == SkyShape.Circle ? r.Radius : r.Reach, unit);
                _width.Text = Size(r.Width, unit);
                _height.Text = Size(r.Height, unit);
            }

            if (_bandMin is not null && _bandMax is not null && _bandUnit is not null)
            {
                var metres = WaveUnits[Math.Max(0, _bandUnit.SelectedIndex)].Metres;
                _bandMin.Text = spec.BandMin is { } lo ? Size(lo, metres) : string.Empty;
                _bandMax.Text = spec.BandMax is { } hi ? Size(hi, metres) : string.Empty;
            }
        }
        finally
        {
            _written = FieldText();
            _syncing = false;
        }
    }

    private string FieldText()
        => string.Join('\u001F', _ra.Text, _dec.Text, _radius.Text, _width.Text, _height.Text,
            _bandMin?.Text ?? "", _bandMax?.Text ?? "");

    /// <summary>The fields as typed, into the spec — or, when one cannot be read, which one and why.</summary>
    private void FromFields()
    {
        if (_syncing || FieldText() == _written) return;

        if (!Sexagesimal.TryParseAngle(_ra.Text, isRa: true, out var ra))
        { Evaluate(Loc.F("Cutout_BadCoordinate", _ra.Text)); return; }
        if (!Sexagesimal.TryParseAngle(_dec.Text, isRa: false, out var dec))
        { Evaluate(Loc.F("Cutout_BadCoordinate", _dec.Text)); return; }

        var unit = AngleUnits[Math.Max(0, _sizeUnit.SelectedIndex)].Degrees;
        SkyRegion region;
        if (_shape.SelectedIndex == 1)
        {
            if (!NumberInput.TryParseUser(_width.Text, out var w)) { Evaluate(Loc.F("Cutout_BadNumber", _width.Text)); return; }
            if (!NumberInput.TryParseUser(_height.Text, out var h)) { Evaluate(Loc.F("Cutout_BadNumber", _height.Text)); return; }
            region = SkyRegion.Box(ra, dec, w * unit, h * unit);
        }
        else
        {
            if (!NumberInput.TryParseUser(_radius.Text, out var r)) { Evaluate(Loc.F("Cutout_BadNumber", _radius.Text)); return; }
            region = SkyRegion.Circle(ra, dec, r * unit);
        }

        double? bandMin = null, bandMax = null;
        if (_bandMin is not null && _bandMax is not null && _bandUnit is not null)
        {
            var metres = WaveUnits[Math.Max(0, _bandUnit.SelectedIndex)].Metres;
            if (!TryOptional(_bandMin.Text, metres, out bandMin)) { Evaluate(Loc.F("Cutout_BadNumber", _bandMin.Text)); return; }
            if (!TryOptional(_bandMax.Text, metres, out bandMax)) { Evaluate(Loc.F("Cutout_BadNumber", _bandMax.Text)); return; }
        }

        _spec = _spec with { Region = region, BandMin = bandMin, BandMax = bandMax };
        _written = FieldText();
        _sky.Region = region;
        Evaluate(fieldError: null);
    }

    private void Evaluate(string? fieldError)
    {
        var check = _source.Check(_spec);
        IReadOnlyList<string> errors = fieldError is null ? check.Errors : [fieldError];

        _errors.Text = string.Join("\n", errors);
        _errors.Visibility = errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _warnings.Text = fieldError is null ? string.Join("\n", check.Warnings) : string.Empty;
        _warnings.Visibility = _warnings.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var estimate = fieldError is null ? _source.EstimateBytes(_spec) : null;
        _estimate.Text = estimate is { } bytes && _source.WholeFileBytes is { } whole
            ? Loc.F("Cutout_Estimate", Caom2Format.Bytes(bytes), Caom2Format.Bytes(whole))
            : string.Empty;
        _estimate.Visibility = _estimate.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _download.IsEnabled = errors.Count == 0;
    }

    private static bool TryOptional(string text, double unit, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!NumberInput.TryParseUser(text, out var v)) return false;
        value = v * unit;
        return true;
    }

    /// <summary>The unit a file's wavelengths read best in: nm for optical, µm for the infrared and submillimetre, mm beyond.</summary>
    private static int BandUnitFor(double? metres) => metres switch
    {
        < 1e-6 => 0,
        < 1e-3 => 1,
        _ => 2,
    };

    private static string Size(double value, double unit) => (value / unit).ToString("0.####", CultureInfo.InvariantCulture);

    private static TextBox Field(string name, string headerKey) => new()
    {
        Name = name,
        Header = Loc.T(headerKey),
        MinWidth = 120,
    };

    private static StackPanel Row(params FrameworkElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    private static TextBlock Caption(string brushKey, string text = "") => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = Sty("CaptionTextBlockStyle"),
        Foreground = Res(brushKey),
        Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
    };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    private static Style Sty(string key) => (Style)Application.Current.Resources[key];
}
