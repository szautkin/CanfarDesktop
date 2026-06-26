using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Services;
using Windows.Storage.Pickers;

namespace CanfarDesktop.Views;

/// <summary>
/// Full-width CAOM2 observation detail viewer: persistent header + 5-tab Pivot
/// (Overview / Coverage / Files / Provenance / Raw) with loading / auth-required /
/// not-found / error states. Replaces the old flat ShowRowDetail dialog.
/// </summary>
public sealed partial class ObservationDetailPage : UserControl
{
    private readonly ICAOM2Service _caom2;
    private readonly DataLinkService _dataLink;

    private string _publisherID = string.Empty;
    private string _collection = string.Empty;
    private string _observationID = string.Empty;

    /// <summary>Raised when the user presses "Sign in" on the auth-required state.</summary>
    public event Action? SignInRequested;

    public ObservationDetailPage(ICAOM2Service caom2, DataLinkService dataLink)
    {
        InitializeComponent();
        _caom2 = caom2;
        _dataLink = dataLink;
    }

    /// <summary>Load (or reload) the detail view for a search-result publisher ID.</summary>
    public async Task LoadAsync(string publisherID)
    {
        _publisherID = publisherID;
        (_collection, _observationID) = SplitUri(Caom2Uri.ToObservationUri(publisherID));
        HeaderObsId.Text = string.IsNullOrEmpty(_observationID) ? "Observation" : _observationID;
        HeaderCollection.Text = _collection;
        HeaderChips.Children.Clear();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        SetState(loading: true);
        var result = await _caom2.GetByPublisherIdAsync(_publisherID);
        switch (result.Status)
        {
            case Caom2Status.Success when result.Observation is not null:
                Populate(result.Observation);
                SetState(success: true);
                break;
            case Caom2Status.AuthRequired:
                SetState(auth: true);
                break;
            case Caom2Status.NotFound:
                NotFoundText.Text = $"The observation '{_observationID}' could not be located in collection '{_collection}'. It may have been removed or the ID is incorrect.";
                SetState(notFound: true);
                break;
            default:
                ErrorBar.Message = result.Message ?? "The metadata service could not be reached.";
                SetState(error: true);
                break;
        }
    }

    private void SetState(bool loading = false, bool success = false, bool auth = false,
                          bool notFound = false, bool error = false)
    {
        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        AuthPanel.Visibility = auth ? Visibility.Visible : Visibility.Collapsed;
        NotFoundPanel.Visibility = notFound ? Visibility.Visible : Visibility.Collapsed;
        DetailPivot.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        ErrorBar.IsOpen = error;
    }

    private static (string Collection, string ObservationID) SplitUri(string? caomUri)
    {
        if (string.IsNullOrEmpty(caomUri) || !caomUri.StartsWith("caom:", StringComparison.OrdinalIgnoreCase))
            return (string.Empty, string.Empty);
        var parts = caomUri["caom:".Length..].Split('/', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (string.Empty, string.Empty);
    }

    #region Populate

    private void Populate(CAOM2Observation obs)
    {
        HeaderObsId.Text = obs.ObservationID;
        HeaderCollection.Text = obs.Collection;

        HeaderChips.Children.Clear();
        if (!string.IsNullOrWhiteSpace(obs.ObservationType))
            HeaderChips.Children.Add(Chip(obs.ObservationType!, "SubtleFillColorSecondaryBrush", "TextFillColorPrimaryBrush"));
        if (!string.IsNullOrWhiteSpace(obs.Intent))
        {
            var science = obs.Intent!.Equals("science", StringComparison.OrdinalIgnoreCase);
            HeaderChips.Children.Add(Chip(obs.Intent!,
                science ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush",
                science ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush"));
        }

        BuildOverview(obs);
        BuildCoverage(obs);
        BuildFiles(obs);
        BuildProvenance(obs);
        BuildRaw(obs);
    }

    private void BuildOverview(CAOM2Observation obs)
    {
        OverviewPanel.Children.Clear();

        OverviewPanel.Children.Add(Card("Observation Identity",
            ("Algorithm", Caom2Format.Text(obs.Algorithm)),
            ("Sequence No.", Caom2Format.Text(obs.SequenceNumber)),
            ("Meta Release", Caom2Format.Date(obs.MetaRelease)),
            ("Type", Caom2Format.Text(obs.ObservationType)),
            ("Intent", Caom2Format.Text(obs.Intent))));

        Border? targetCard = obs.Target is { } t ? Card("Target",
            ("Name", Caom2Format.Text(t.Name)),
            ("Type", Caom2Format.Text(t.Type)),
            ("Standard", Caom2Format.Bool(t.Standard)),
            ("Redshift", Caom2Format.Number(t.Redshift)),
            ("Moving", Caom2Format.Bool(t.Moving)),
            ("Keywords", JoinKeywords(t.Keywords))) : null;

        Border? proposalCard = obs.Proposal is { } p ? Card("Proposal",
            ("ID", Caom2Format.Text(p.Id)),
            ("PI", Caom2Format.Text(p.Pi)),
            ("Project", Caom2Format.Text(p.Project)),
            ("Title", Caom2Format.Text(p.Title)),
            ("Keywords", JoinKeywords(p.Keywords))) : null;

        OverviewPanel.Children.Add(TwoColumn(targetCard, proposalCard));

        Border? scopeCard = (obs.Telescope is not null || obs.Instrument is not null) ? Card("Telescope + Instrument",
            ("Telescope", Caom2Format.Text(obs.Telescope?.Name)),
            ("Instrument", Caom2Format.Text(obs.Instrument?.Name)),
            ("Location", obs.Telescope?.GeoLocation is { } g
                ? $"({Caom2Format.Number(g.X)}, {Caom2Format.Number(g.Y)}, {Caom2Format.Number(g.Z)}) m"
                : "—")) : null;

        Border? envCard = obs.Environment is { } e ? Card("Environment",
            ("Seeing", Caom2Format.Number(e.Seeing)),
            ("Humidity", Caom2Format.Number(e.Humidity)),
            ("Elevation", Caom2Format.Degrees(e.Elevation)),
            ("Ambient Temp", Caom2Format.Number(e.AmbientTemp)),
            ("Photometric", Caom2Format.Bool(e.Photometric))) : null;

        OverviewPanel.Children.Add(TwoColumn(scopeCard, envCard));
    }

    private void BuildCoverage(CAOM2Observation obs)
    {
        CoveragePanel.Children.Clear();
        ForEachPlane(obs, CoveragePanel, (plane, into) =>
        {
            if (plane.Position is { } pos)
            {
                var spatial = Card("Spatial",
                    ("Footprint", pos.Polygon.Count >= 3 ? "" : "—"),
                    ("Dimensions", pos.DimensionPixels is { } d ? $"{d.NAxis1} × {d.NAxis2} px" : "—"),
                    ("Resolution", pos.ResolutionArcsec is { } r ? $"{Caom2Format.Number(r)}″" : "—"),
                    ("Sample Size", pos.SampleSizeArcsec is { } s ? $"{Caom2Format.Number(s)}″" : "—"));
                if (pos.Polygon.Count >= 3 && BuildFootprint(pos.Polygon) is { } fp && spatial.Child is StackPanel sp)
                    sp.Children.Insert(1, fp);
                into.Children.Add(spatial);
            }
            if (plane.Energy is { } en)
                into.Children.Add(Card("Spectral",
                    ("Bandpass", Caom2Format.Text(en.BandpassName)),
                    ("Band", Caom2Format.Text(en.EmBand)),
                    ("Wavelength", Caom2Format.WavelengthRange(en.LowerMetres, en.UpperMetres)),
                    ("Resolving Power", Caom2Format.Number(en.ResolvingPower)),
                    ("Rest Wavelength", Caom2Format.Wavelength(en.RestWavMetres))));
            if (plane.Time is { } tm)
                into.Children.Add(Card("Temporal",
                    ("Start", Caom2Format.MjdToDate(tm.LowerMJD)),
                    ("End", Caom2Format.MjdToDate(tm.UpperMJD)),
                    ("Exposure", Caom2Format.Seconds(tm.ExposureSeconds))));
            if (plane.Polarization is { States.Count: > 0 } pol)
                into.Children.Add(Card("Polarization", ("States", string.Join(", ", pol.States))));
        });
    }

    private void BuildFiles(CAOM2Observation obs)
    {
        FilesPanel.Children.Clear();
        ForEachPlane(obs, FilesPanel, (plane, into) =>
        {
            if (plane.Artifacts.Count == 0)
            {
                into.Children.Add(new TextBlock
                {
                    Text = "No files listed for this plane.",
                    Foreground = Res("TextFillColorTertiaryBrush"),
                    Style = Sty("CaptionTextBlockStyle"),
                });
                return;
            }
            foreach (var art in plane.Artifacts)
                into.Children.Add(BuildArtifactRow(art));
        });

        var link = new HyperlinkButton { Content = "View all files on CADC", Margin = new Thickness(0, 4, 0, 0) };
        link.Click += OnViewOnCadc;
        FilesPanel.Children.Add(link);
    }

    private void BuildProvenance(CAOM2Observation obs)
    {
        ProvenancePanel.Children.Clear();
        var any = false;
        ForEachPlane(obs, ProvenancePanel, (plane, into) =>
        {
            if (plane.Provenance is not { } pv)
            {
                into.Children.Add(new TextBlock
                {
                    Text = "No provenance recorded for this plane.",
                    Foreground = Res("TextFillColorTertiaryBrush"),
                    Style = Sty("CaptionTextBlockStyle"),
                });
                return;
            }
            any = true;
            into.Children.Add(Card("Pipeline",
                ("Name", Caom2Format.Text(pv.Name)),
                ("Version", Caom2Format.Text(pv.Version)),
                ("Project", Caom2Format.Text(pv.Project)),
                ("Producer", Caom2Format.Text(pv.Producer)),
                ("Run ID", Caom2Format.Text(pv.RunID)),
                ("Reference", Caom2Format.Text(pv.Reference)),
                ("Last Executed", Caom2Format.Date(pv.LastExecuted))));

            if (pv.Inputs.Count > 0)
            {
                var inputsStack = new StackPanel { Spacing = 4 };
                inputsStack.Children.Add(Heading("Inputs"));
                foreach (var input in pv.Inputs)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    row.Children.Add(new FontIcon { Glyph = "", FontSize = 12, Foreground = Res("TextFillColorSecondaryBrush") });
                    row.Children.Add(new TextBlock { Text = input, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, Style = Sty("CaptionTextBlockStyle") });
                    AutomationProperties.SetName(row, $"Input: {input}");
                    inputsStack.Children.Add(row);
                }
                into.Children.Add(CardFrom(inputsStack));
            }
        });
        _ = any;
    }

    private void BuildRaw(CAOM2Observation obs)
    {
        RawPanel.Children.Clear();
        void Row(string k, string v) { if (v != "—") RawPanel.Children.Add(InfoRow(k, v)); }

        Row("collection", obs.Collection);
        Row("observationID", obs.ObservationID);
        Row("type", Caom2Format.Text(obs.ObservationType));
        Row("intent", Caom2Format.Text(obs.Intent));
        Row("sequenceNumber", Caom2Format.Text(obs.SequenceNumber));
        Row("algorithm", Caom2Format.Text(obs.Algorithm));
        Row("metaRelease", Caom2Format.Date(obs.MetaRelease));
        Row("target.name", Caom2Format.Text(obs.Target?.Name));
        Row("proposal.id", Caom2Format.Text(obs.Proposal?.Id));
        Row("proposal.pi", Caom2Format.Text(obs.Proposal?.Pi));
        Row("telescope.name", Caom2Format.Text(obs.Telescope?.Name));
        Row("instrument.name", Caom2Format.Text(obs.Instrument?.Name));

        for (var i = 0; i < obs.Planes.Count; i++)
        {
            var p = obs.Planes[i];
            var prefix = $"plane[{i}].";
            Row(prefix + "productID", Caom2Format.Text(p.ProductID));
            Row(prefix + "dataProductType", Caom2Format.Text(p.DataProductType));
            Row(prefix + "calibrationLevel", p.CalibrationLevel?.ToString() ?? "—");
            Row(prefix + "quality", Caom2Format.Text(p.Quality));
            Row(prefix + "energy.bandpass", Caom2Format.Text(p.Energy?.BandpassName));
            Row(prefix + "energy.lower", Caom2Format.Wavelength(p.Energy?.LowerMetres));
            Row(prefix + "energy.upper", Caom2Format.Wavelength(p.Energy?.UpperMetres));
            Row(prefix + "time.exposure", Caom2Format.Seconds(p.Time?.ExposureSeconds));
            Row(prefix + "artifacts", p.Artifacts.Count.ToString());
        }
    }

    #endregion

    #region Builders

    private void ForEachPlane(CAOM2Observation obs, Panel target, Action<Caom2Plane, StackPanel> build)
    {
        var multi = obs.Planes.Count > 1;
        for (var i = 0; i < obs.Planes.Count; i++)
        {
            var plane = obs.Planes[i];
            var content = new StackPanel { Spacing = 12 };
            build(plane, content);

            if (!multi)
            {
                target.Children.Add(content);
                continue;
            }

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            header.Children.Add(new TextBlock
            {
                Text = $"{plane.ProductID}  —  {Caom2Format.Text(plane.DataProductType)}  —  Cal. Level {plane.CalibrationLevel?.ToString() ?? "?"}",
                Style = Sty("BodyStrongTextBlockStyle"),
            });
            if (string.Equals(plane.Quality, "junk", StringComparison.OrdinalIgnoreCase))
                header.Children.Add(Chip("Junk", "SystemFillColorCriticalBrush", "TextOnAccentFillColorPrimaryBrush"));

            target.Children.Add(new Expander
            {
                Header = header,
                Content = content,
                IsExpanded = i == 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            });
        }
    }

    private Border BuildArtifactRow(Caom2Artifact art)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var (bg, fg) = ArtifactBadge(art.ProductType);
        var badge = Chip(string.IsNullOrWhiteSpace(art.ProductType) ? "file" : art.ProductType!, bg, fg);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var name = new TextBlock
        {
            Text = Caom2Format.ArtifactFileName(art.Uri),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            IsTabStop = false,
            IsTextSelectionEnabled = true,
        };
        ToolTipService.SetToolTip(name, art.Uri);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var meta = new TextBlock
        {
            Text = $"{(string.IsNullOrWhiteSpace(art.ContentType) ? "" : art.ContentType + "  ")}{Caom2Format.Bytes(art.ContentLength)}",
            Foreground = Res("TextFillColorSecondaryBrush"),
            Style = Sty("CaptionTextBlockStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(meta, 2);
        grid.Children.Add(meta);

        var dl = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
        ToolTipService.SetToolTip(dl, "Download this file");
        AutomationProperties.SetName(dl, $"Download {Caom2Format.ArtifactFileName(art.Uri)}");
        dl.Click += (_, _) => _ = OnDownloadArtifactAsync(art);
        Grid.SetColumn(dl, 3);
        grid.Children.Add(dl);

        return new Border
        {
            Background = Res("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Res("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = grid,
        };
    }

    private FrameworkElement? BuildFootprint(IReadOnlyList<Caom2SkyVertex> poly)
    {
        if (poly.Count < 3) return null;
        double minRa = poly.Min(p => p.Ra), maxRa = poly.Max(p => p.Ra);
        double minDec = poly.Min(p => p.Dec), maxDec = poly.Max(p => p.Dec);
        var rangeRa = Math.Max(maxRa - minRa, 1e-9);
        var rangeDec = Math.Max(maxDec - minDec, 1e-9);
        const double w = 200, h = 120, pad = 0.1;

        var points = new Microsoft.UI.Xaml.Media.PointCollection();
        foreach (var v in poly)
        {
            var nx = (maxRa - v.Ra) / rangeRa;   // mirror RA (increases left on screen)
            var ny = (maxDec - v.Dec) / rangeDec; // Dec increases upward
            var x = pad * w + nx * (1 - 2 * pad) * w;
            var y = pad * h + ny * (1 - 2 * pad) * h;
            points.Add(new Windows.Foundation.Point(x, y));
        }
        points.Add(points[0]); // close the loop

        var poly2 = new Polyline
        {
            Points = points,
            Stroke = Res("AccentFillColorDefaultBrush"),
            StrokeThickness = 1.5,
            Fill = Res("SubtleFillColorSecondaryBrush"),
        };
        var canvas = new Canvas { Width = w, Height = h, IsTabStop = false };
        canvas.Children.Add(poly2);

        var border = new Border
        {
            Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxWidth = w, MaxHeight = h, HorizontalAlignment = HorizontalAlignment.Left },
            Margin = new Thickness(0, 0, 0, 4),
        };
        AutomationProperties.SetName(border,
            $"Spatial footprint: {poly.Count}-vertex polygon, RA {minRa:F2}° to {maxRa:F2}°, Dec {minDec:F2}° to {maxDec:F2}°");
        return border;
    }

    private Border Card(string title, params (string Label, string Value)[] rows)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Heading(title));
        foreach (var (label, value) in rows)
            if (!string.IsNullOrWhiteSpace(value) && value != "—")
                stack.Children.Add(InfoRow(label, value));
        return CardFrom(stack);
    }

    private Border CardFrom(UIElement content) => new()
    {
        Background = Res("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Res("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16),
        Child = content,
    };

    private TextBlock Heading(string text)
    {
        var tb = new TextBlock { Text = text, Style = Sty("BodyStrongTextBlockStyle") };
        AutomationProperties.SetHeadingLevel(tb, AutomationHeadingLevel.Level3);
        return tb;
    }

    private Grid InfoRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var l = new TextBlock
        {
            Text = label,
            Style = Sty("CaptionTextBlockStyle"),
            Foreground = Res("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.Wrap,
        };
        var v = new TextBlock
        {
            Text = value,
            Style = Sty("BodyTextBlockStyle"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(v, 1);
        grid.Children.Add(l);
        grid.Children.Add(v);
        return grid;
    }

    private Grid TwoColumn(Border? left, Border? right)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (left is not null) { Grid.SetColumn(left, 0); grid.Children.Add(left); }
        if (right is not null) { Grid.SetColumn(right, 1); grid.Children.Add(right); }
        // If only one card, let it span both columns.
        if (left is not null && right is null) Grid.SetColumnSpan(left, 2);
        if (left is null && right is not null) { Grid.SetColumn(right, 0); Grid.SetColumnSpan(right, 2); }
        return grid;
    }

    private Border Chip(string text, string bgKey, string fgKey)
    {
        var border = new Border
        {
            Background = Res(bgKey),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.Child = new TextBlock { Text = text, Style = Sty("CaptionTextBlockStyle"), Foreground = Res(fgKey) };
        AutomationProperties.SetName(border, text);
        return border;
    }

    private static (string Bg, string Fg) ArtifactBadge(string? productType) => (productType?.ToLowerInvariant()) switch
    {
        "science" => ("SystemFillColorSuccessBrush", "TextOnAccentFillColorPrimaryBrush"),
        "preview" or "thumbnail" => ("AccentFillColorDefaultBrush", "TextOnAccentFillColorPrimaryBrush"),
        _ => ("SubtleFillColorSecondaryBrush", "TextFillColorPrimaryBrush"),
    };

    private static string JoinKeywords(IReadOnlyList<string> keywords)
        => keywords.Count == 0 ? "—" : string.Join(", ", keywords);

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    private static Style Sty(string key) => (Style)Application.Current.Resources[key];

    #endregion

    #region Actions

    private async Task OnDownloadArtifactAsync(Caom2Artifact art)
    {
        try
        {
            DownloadBar.IsOpen = true;
            DownloadBar.ActionButton = null;
            DownloadBar.Severity = InfoBarSeverity.Informational;
            DownloadBar.Title = $"Resolving {Caom2Format.ArtifactFileName(art.Uri)}…";
            DownloadProgress.Visibility = Visibility.Visible;
            DownloadProgress.IsIndeterminate = true;
            DownloadText.Text = string.Empty;

            var links = await _dataLink.GetLinksAsync(_publisherID);
            var fileName = Caom2Format.ArtifactFileName(art.Uri);
            var match = links.DirectFiles.FirstOrDefault(f =>
                f.Filename.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                || f.Url.Contains(fileName, StringComparison.OrdinalIgnoreCase));
            var url = match?.Url ?? links.DirectFileUrl ?? _dataLink.GetDownloadUrl(_publisherID);

            await DownloadUrlToFileAsync(url, fileName);
        }
        catch (Exception ex)
        {
            DownloadBar.Severity = InfoBarSeverity.Error;
            DownloadBar.Title = "Download failed";
            DownloadBar.Message = ex.Message;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task DownloadUrlToFileAsync(string url, string suggestedName)
    {
        var hWnd = WindowHelper.ActiveWindows.Count > 0
            ? WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0])
            : nint.Zero;
        if (hWnd == nint.Zero) return;

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        if (!System.IO.Path.HasExtension(suggestedName)) suggestedName += ".fits";
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("FITS Image", new List<string> { ".fits" });
        picker.FileTypeChoices.Add("All Files", new List<string> { "." });

        var file = await picker.PickSaveFileAsync();
        if (file is null) { DownloadBar.IsOpen = false; return; }

        DownloadBar.Title = $"Downloading {file.Name}…";
        var tempPath = file.Path + ".tmp";

        using (var response = await _dataLink.DownloadAsync(url))
        {
            var totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create);
            if (totalBytes.HasValue) { DownloadProgress.IsIndeterminate = false; DownloadProgress.Maximum = totalBytes.Value; }

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (totalBytes.HasValue)
                {
                    DownloadProgress.Value = downloaded;
                    DownloadText.Text = $"{Caom2Format.Bytes(downloaded)} / {Caom2Format.Bytes(totalBytes.Value)}";
                }
                else
                {
                    DownloadText.Text = Caom2Format.Bytes(downloaded);
                }
            }
            await fileStream.FlushAsync();
        }

        if (File.Exists(file.Path)) File.Delete(file.Path);
        File.Move(tempPath, file.Path);

        DownloadBar.Severity = InfoBarSeverity.Success;
        DownloadBar.Title = $"Downloaded {file.Name}";
        DownloadProgress.Visibility = Visibility.Collapsed;

        // Offer to open it in the 3D Cube Viewer (it reports gracefully if not a NAXIS3 cube).
        var openCube = new Button { Content = "Open in Cube Viewer" };
        var savedPath = file.Path;
        openCube.Click += (_, _) => OpenInCubeRequested?.Invoke(savedPath);
        DownloadBar.ActionButton = openCube;
    }

    private async void OnViewOnCadc(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://www.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/en/search/"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"View on CADC failed: {ex.Message}");
        }
    }

    private void OnSignIn(object sender, RoutedEventArgs e) => SignInRequested?.Invoke();

    private void OnRetry(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    /// <summary>Re-fetch the current observation (e.g. after the user signs in).</summary>
    public Task RefreshAsync() => ReloadAsync();

    /// <summary>Raised to the host to navigate back (Close button mirrors the title-bar Back).</summary>
    public event Action? CloseRequested;

    /// <summary>Raised to open a just-downloaded FITS spectral cube in the 3D Cube Viewer.</summary>
    public event Action<string>? OpenInCubeRequested;

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    #endregion
}
