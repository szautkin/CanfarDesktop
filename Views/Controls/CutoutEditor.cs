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
/// Choose a cutout of one file: who cuts it, when it can be cut more than one way; its region by numbers
/// or by drawing; its band when the file has one — checked as it is typed, with an idea of its size.
///
/// <para>Edits one <see cref="CutoutSpec"/>, the file as the chosen way (<see cref="ICutoutSource"/>)
/// describes it deciding which fields there are. The fields and the drawing are two views of that one
/// spec — editing either updates the other — and the way's <see cref="ICutoutSource.Check"/> judges it,
/// the same judgement an agent's request meets. Changing the way keeps the region. It does not make the
/// cutout: it says <see cref="DownloadRequested"/>, and whoever placed it hands the cutout to the app.</para>
///
/// <para>Every control is named, so an agent can point at it.</para>
/// </summary>
public sealed class CutoutEditor : UserControl
{
    private static readonly (string Label, double Degrees)[] AngleUnits = [("″", 1.0 / 3600), ("′", 1.0 / 60), ("°", 1.0)];
    private static readonly (string Label, double Metres)[] WaveUnits = [("nm", 1e-9), ("µm", 1e-6), ("mm", 1e-3)];

    private readonly IReadOnlyList<ICutoutSource> _ways;
    private readonly CutoutHints? _hints;
    private ICutoutSource _source;
    private CutoutSpec _spec;
    private bool _syncing;

    /// <summary>
    /// The fields' text as this editor last wrote it. TextBox raises TextChanged after the fact, not
    /// while the text is being set, so a flag alone cannot tell the editor's own writes from typing:
    /// text still equal to what was written is not an edit.
    /// </summary>
    private string _written = string.Empty;

    private readonly RadioButtons _way = new() { Name = "CutoutWayChoice" };
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _note = Caption("TextFillColorSecondaryBrush");
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
    private readonly StackPanel? _band;
    private readonly StackPanel _extensions = new() { Name = "CutoutExtensions", Spacing = 2 };
    private readonly FootprintCanvas _sky = new() { Name = "CutoutSky", IsEditable = true, Height = 220, MinWidth = 240 };
    private readonly TextBlock _errors = Caption("SystemFillColorCriticalBrush");
    private readonly TextBlock _warnings = Caption("SystemFillColorCautionBrush");
    private readonly TextBlock _estimate = Caption("TextFillColorSecondaryBrush");
    private readonly Button _download = new() { Name = "CutoutDownloadButton", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

    /// <summary>The person asked for this cutout, made this way.</summary>
    public event Action<ICutoutSource, CutoutSpec>? DownloadRequested;

    /// <summary>The person closed the editor.</summary>
    public event Action? CloseRequested;

    /// <param name="ways">The ways this one file can be cut, the one to start on first (<see cref="CutoutSources.Preferred"/>).</param>
    /// <param name="hints">The last search, for the cutout each way suggests.</param>
    public CutoutEditor(IReadOnlyList<ICutoutSource> ways, CutoutHints? hints, SkyPoint? target)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ways.Count);
        _ways = ways;
        _hints = hints;
        _source = ways[0];
        _spec = _source.Suggest(hints);
        var banded = ways.FirstOrDefault(w => w.File.Supports("BAND"))?.File;

        _shape.Header = Loc.T("Cutout_Shape");
        _shape.Items.Add(Loc.T("Cutout_ShapeCircle"));
        _shape.Items.Add(Loc.T("Cutout_ShapeBox"));
        _shape.SelectedIndex = _spec.Region?.Shape == SkyShape.Box ? 1 : 0;

        foreach (var (label, _) in AngleUnits) _sizeUnit.Items.Add(label);
        _sizeUnit.SelectedIndex = 1; // arcminutes: what a cutout is usually measured in
        AutomationProperties.SetName(_sizeUnit, Loc.T("Cutout_Unit"));

        _circleSize.Children.Add(_radius);
        _boxSize.Children.Add(_width);
        _boxSize.Children.Add(_height);

        var fields = new StackPanel { Spacing = 10, MinWidth = 280 };
        if (ways.Count > 1) fields.Children.Add(WayChoice());
        fields.Children.Add(_shape);
        fields.Children.Add(Row(_ra, _dec));
        fields.Children.Add(Row(_circleSize, _boxSize, _sizeUnit));

        if (banded is not null)
        {
            _bandMin = Field("CutoutBandMinBox", "Cutout_BandFrom");
            _bandMax = Field("CutoutBandMaxBox", "Cutout_BandTo");
            _bandUnit = new ComboBox { Name = "CutoutBandUnit", VerticalAlignment = VerticalAlignment.Bottom };
            foreach (var (label, _) in WaveUnits) _bandUnit.Items.Add(label);
            _bandUnit.SelectedIndex = BandUnitFor(banded.BandMax ?? banded.BandMin);
            AutomationProperties.SetName(_bandUnit, Loc.T("Cutout_Unit"));

            _band = new StackPanel { Spacing = 2 };
            _band.Children.Add(new TextBlock { Text = Loc.T("Cutout_Band"), Style = Sty("BodyStrongTextBlockStyle") });
            _band.Children.Add(Row(_bandMin, _bandMax, _bandUnit));
            _band.Children.Add(Caption("TextFillColorTertiaryBrush", Loc.T("Cutout_BandHint")));
            fields.Children.Add(_band);
        }
        fields.Children.Add(_extensions);

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

        _download.Click += (_, _) => DownloadRequested?.Invoke(_source, _spec);
        var reset = new Button { Name = "CutoutResetButton", Content = Loc.T("Cutout_Reset") };
        reset.Click += (_, _) => Load(_source.Suggest(_hints));
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
        _title.Style = Sty("SubtitleTextBlockStyle");
        header.Children.Add(_title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(header);
        stack.Children.Add(_note);
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

        UseWay(_source);
    }

    /// <summary>The cutout as it stands.</summary>
    public CutoutSpec Spec => _spec;

    /// <summary>The way it will be cut.</summary>
    public ICutoutSource Source => _source;

    /// <summary>
    /// Show this cutout — the suggestion, or one an agent proposes — cut the way it says when this file
    /// can be cut that way.
    /// </summary>
    public void Load(CutoutSpec spec)
    {
        var index = _ways.ToList().FindIndex(w => w.Method == spec.CutBy && w.Unavailable is null);
        if (index >= 0 && _ways[index] != _source)
        {
            _source = _ways[index];
            _syncing = true;
            try { _way.SelectedIndex = index; }
            finally { _syncing = false; }
            UseWay(_source, spec);
            return;
        }
        Apply(spec, fromDrawing: false);
    }

    /// <summary>
    /// What the editor says for each way of cutting: the choice, the note under the title, the button, and
    /// the estimate's wording. The one place a new way's words go.
    /// </summary>
    private static (string Choice, string Note, string Action, string EstimateKey) Words(CutoutMethod method) => method switch
    {
        CutoutMethod.Local => (Loc.T("Cutout_CutByLocal"), Loc.T("Cutout_EditorNoteLocal"), Loc.T("Cutout_CutLocal"), "Cutout_EstimateLocal"),
        _ => (Loc.T("Cutout_CutBySoda"), Loc.T("Cutout_EditorNote"), Loc.T("Cutout_Download"), "Cutout_Estimate"),
    };

    /// <summary>
    /// "Cut by": each way this file can be cut, one that cannot greyed — and why, underneath, since a
    /// disabled choice shows no tooltip of its own.
    /// </summary>
    private FrameworkElement WayChoice()
    {
        _way.Header = Loc.T("Cutout_CutBy");
        var reasons = new List<string>();
        foreach (var way in _ways)
        {
            var words = Words(way.Method);
            _way.Items.Add(new RadioButton { Content = words.Choice, IsEnabled = way.Unavailable is null });
            if (way.Unavailable is { } why) reasons.Add(Loc.F("Cutout_WayUnavailable", words.Choice, why));
        }
        _way.SelectedIndex = 0;
        _way.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _way.SelectedIndex >= 0 && _ways[_way.SelectedIndex] != _source)
                UseWay(_ways[_way.SelectedIndex]);
        };

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(_way);
        if (reasons.Count > 0) panel.Children.Add(Caption("TextFillColorTertiaryBrush", string.Join("\n", reasons)));
        return panel;
    }

    /// <summary>
    /// Cut this way: its file's name, footprint and fields, its words — the region kept as it is, a band
    /// dropped when this way cannot cut by one.
    /// </summary>
    private void UseWay(ICutoutSource way, CutoutSpec? spec = null)
    {
        _source = way;
        var file = way.File;
        var words = Words(way.Method);
        _title.Text = Loc.F("Cutout_EditorTitle", file.FileName);
        _note.Text = words.Note;
        _note.Visibility = Visibility.Visible;
        _download.Content = words.Action;
        if (_band is not null) _band.Visibility = file.Supports("BAND") ? Visibility.Visible : Visibility.Collapsed;
        _sky.Parts = file.Parts.Select(p => p.Outline()).ToList();
        _sky.Footprint = file.Footprint?.Outline() ?? [];

        var next = spec ?? _spec;
        if (!file.Supports("BAND")) next = next with { BandMin = null, BandMax = null };
        if (file.Extensions.Count == 0) next = next with { Extensions = [] };
        BuildExtensions(file);
        Apply(next, fromDrawing: false);
    }

    /// <summary>
    /// "Images": a box for each of a multi-extension file's images, ticked to keep it — all of them at
    /// first, which keeps every image the region falls on. Nothing when there is no choice to make.
    /// </summary>
    private void BuildExtensions(ICutoutFile file)
    {
        _extensions.Children.Clear();
        _extensions.Visibility = file.Extensions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (file.Extensions.Count <= 1) return;

        _extensions.Children.Add(new TextBlock { Text = Loc.T("Cutout_Extensions"), Style = Sty("BodyStrongTextBlockStyle") });
        foreach (var name in file.Extensions)
        {
            var box = new CheckBox { Tag = name, Content = $"[{name}]", MinWidth = 0 };
            AutomationProperties.SetName(box, name);
            box.Checked += (_, _) => FromExtensions();
            box.Unchecked += (_, _) => FromExtensions();
            _extensions.Children.Add(box);
        }
        _extensions.Children.Add(Caption("TextFillColorTertiaryBrush", Loc.T("Cutout_ExtensionsHint")));
    }

    private IEnumerable<CheckBox> ExtensionBoxes => _extensions.Children.OfType<CheckBox>();

    /// <summary>The boxes as ticked, into the spec: none left out is every image, as if nothing were chosen.</summary>
    private void FromExtensions()
    {
        if (_syncing) return;
        var chosen = ExtensionBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        Apply(_spec with { Extensions = chosen.Count == _source.File.Extensions.Count ? [] : chosen }, fromDrawing: false);
    }

    /// <summary>The boxes as the spec has them, each saying when the region is not on its image.</summary>
    private void ShowExtensions()
    {
        var file = _source.File;
        var outline = _spec.Region is { } region && region.Problem() is null ? region.Outline() : null;
        _syncing = true;
        try
        {
            foreach (var box in ExtensionBoxes)
            {
                var name = (string)box.Tag;
                box.IsChecked = _spec.Extensions.Count == 0 || _spec.Extensions.Contains(name);
                var index = file.Extensions.ToList().IndexOf(name);
                var missed = outline is not null && index >= 0 && index < file.Parts.Count
                             && SkyGeometry.Overlap(outline, file.Parts[index].Outline()) == SkyOverlap.Outside;
                box.Content = missed ? Loc.F("Cutout_ExtensionMissed", name) : $"[{name}]";
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Apply(CutoutSpec spec, bool fromDrawing)
    {
        _spec = _source.Bind(spec);
        if (!fromDrawing) _sky.Region = _spec.Region;
        WriteFields(_spec);
        ShowExtensions();
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
        ShowExtensions();
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
            ? Loc.F(Words(_source.Method).EstimateKey, Caom2Format.Bytes(bytes), Caom2Format.Bytes(whole))
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
