using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Cutouts;
using CanfarDesktop.Views.Controls;
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
    private readonly ObservationDownloader _downloader;
    private readonly SearchContext _search;
    private readonly ObservationStore _store;

    private string _publisherID = string.Empty;
    private CAOM2Observation? _current;
    private string _collection = string.Empty;
    private string _observationID = string.Empty;

    /// <summary>This observation's DataLink answer, once fetched: which of its files can be cut out.</summary>
    private DataLinkResult? _links;

    /// <summary>The cutout editor open in the Files tab, if one is.</summary>
    private CutoutEditor? _cutoutEditor;

    /// <summary>Raised when the user presses "Sign in" on the auth-required state.</summary>
    public event Action? SignInRequested;

    public ObservationDetailPage(ICAOM2Service caom2, DataLinkService dataLink,
                                 ObservationDownloader downloader, SearchContext search, ObservationStore store)
    {
        InitializeComponent();
        _caom2 = caom2;
        _dataLink = dataLink;
        _downloader = downloader;
        _search = search;
        _store = store;

        SaveToResearchButton.Content = Loc.T("ObsDetail_SaveToResearch");
        UpdateSaveToResearch();
        // A cached page for the app's life, so it may listen for the app's life: a download landing
        // elsewhere puts this observation in Research, and the button should say so.
        _store.Changed += () => DispatcherQueue.TryEnqueue(UpdateSaveToResearch);
    }

    /// <summary>
    /// "Save to Research" — keep the observation, its details and notes, without downloading a file.
    /// Live once the observation has loaded and Research does not have it; greyed, saying why, otherwise.
    /// </summary>
    private void UpdateSaveToResearch()
    {
        var loaded = _current is not null && !string.IsNullOrEmpty(_publisherID);
        var inResearch = loaded && _store.Has(_publisherID);
        UIFactory.Enable(SaveToResearchButton, loaded && !inResearch,
            inResearch ? Loc.T("ObsDetail_AlreadyInResearch") : Loc.T("ObsDetail_SaveToResearchLoading"));
        if (loaded && !inResearch) ToolTipService.SetToolTip(SaveToResearchButton, Loc.T("ObsDetail_SaveToResearchTip"));
    }

    private void OnSaveToResearch(object sender, RoutedEventArgs e)
    {
        if (_current is null || string.IsNullOrEmpty(_publisherID)) return;

        var ctx = new DownloadContext(_publisherID, _collection, _observationID, _current, _links, IsScience: true);
        if (_store.SaveIfAbsent(ResearchRecordFor(ctx)))
        {
            DownloadBar.IsOpen = true;
            DownloadBar.ActionButton = null;
            DownloadBar.Severity = InfoBarSeverity.Success;
            DownloadBar.Title = Loc.T("ObsDetail_SavedToResearch");
            DownloadBar.Message = string.Empty;
            DownloadText.Text = string.Empty;
            DownloadResearchText.Text = Loc.T("ObsDetail_SavedToResearchNote");
            DownloadResearchLink.Content = Loc.T("ObsDetail_ViewInResearch");
            DownloadResearchRow.Visibility = Visibility.Visible;
        }
        UpdateSaveToResearch();
    }

    /// <summary>Load (or reload) the detail view for a search-result publisher ID.</summary>
    public async Task LoadAsync(string publisherID)
    {
        _publisherID = publisherID;
        (_collection, _observationID) = SplitUri(Caom2Uri.ToObservationUri(publisherID));
        HeaderObsId.Text = string.IsNullOrEmpty(_observationID) ? Loc.T("ObsDetail_ObservationFallback") : _observationID;
        HeaderCollection.Text = _collection;
        HeaderChips.Children.Clear();
        ResetViewState();
        await ReloadAsync();
    }

    /// <summary>
    /// The page is a cached singleton, so without this a new observation opens on
    /// whatever Pivot tab and scroll offset the previous one was left at — and, worse,
    /// still shows the PREVIOUS observation's download banner, whose open-in-viewer
    /// buttons point at the old file.
    /// </summary>
    private void ResetViewState()
    {
        DetailPivot.SelectedIndex = 0;
        foreach (var panel in new FrameworkElement[] { OverviewPanel, CoveragePanel, FilesPanel, ProvenancePanel, RawPanel })
            (panel.Parent as ScrollViewer)?.ChangeView(null, 0, null, disableAnimation: true);

        DownloadBar.IsOpen = false;
        DownloadBar.ActionButton = null;
        DownloadResearchRow.Visibility = Visibility.Collapsed;
        DownloadText.Text = string.Empty;

        // The previous observation's files and cutout editor go with it.
        _links = null;
        _cutoutEditor = null;
        _current = null;
        UpdateSaveToResearch();
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
                await LoadCutoutServicesAsync(result.Observation);
                break;
            case Caom2Status.AuthRequired:
                SetState(auth: true);
                break;
            case Caom2Status.NotFound:
                NotFoundText.Text = Loc.F("ObsDetail_NotFoundBody", _observationID, _collection);
                SetState(notFound: true);
                break;
            default:
                ErrorBar.Message = result.Message ?? Loc.T("ObsDetail_ServiceUnreachable");
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
        _current = obs;
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
        UpdateSaveToResearch();
    }

    private void BuildOverview(CAOM2Observation obs)
    {
        OverviewPanel.Children.Clear();

        OverviewPanel.Children.Add(Card(Loc.T("ObsDetail_CardIdentity"),
            (Loc.T("ObsDetail_RowAlgorithm"), Caom2Format.Text(obs.Algorithm)),
            (Loc.T("ObsDetail_RowSequenceNo"), Caom2Format.Text(obs.SequenceNumber)),
            (Loc.T("ObsDetail_RowMetaRelease"), Caom2Format.Date(obs.MetaRelease)),
            (Loc.T("ObsDetail_RowType"), Caom2Format.Text(obs.ObservationType)),
            (Loc.T("ObsDetail_RowIntent"), Caom2Format.Text(obs.Intent))));

        Border? targetCard = obs.Target is { } t ? Card(Loc.T("ObsDetail_CardTarget"),
            (Loc.T("ObsDetail_RowName"), Caom2Format.Text(t.Name)),
            (Loc.T("ObsDetail_RowType"), Caom2Format.Text(t.Type)),
            (Loc.T("ObsDetail_RowStandard"), Caom2Format.Bool(t.Standard)),
            (Loc.T("ObsDetail_RowRedshift"), Caom2Format.Number(t.Redshift)),
            (Loc.T("ObsDetail_RowMoving"), Caom2Format.Bool(t.Moving)),
            (Loc.T("ObsDetail_RowKeywords"), JoinKeywords(t.Keywords))) : null;

        Border? proposalCard = obs.Proposal is { } p ? Card(Loc.T("ObsDetail_CardProposal"),
            (Loc.T("ObsDetail_RowId"), Caom2Format.Text(p.Id)),
            (Loc.T("ObsDetail_RowPi"), Caom2Format.Text(p.Pi)),
            (Loc.T("ObsDetail_RowProject"), Caom2Format.Text(p.Project)),
            (Loc.T("ObsDetail_RowTitle"), Caom2Format.Text(p.Title)),
            (Loc.T("ObsDetail_RowKeywords"), JoinKeywords(p.Keywords))) : null;

        OverviewPanel.Children.Add(TwoColumn(targetCard, proposalCard));

        Border? scopeCard = (obs.Telescope is not null || obs.Instrument is not null) ? Card(Loc.T("ObsDetail_CardTelescope"),
            (Loc.T("ObsDetail_RowTelescope"), Caom2Format.Text(obs.Telescope?.Name)),
            (Loc.T("ObsDetail_RowInstrument"), Caom2Format.Text(obs.Instrument?.Name)),
            (Loc.T("ObsDetail_RowLocation"), obs.Telescope?.GeoLocation is { } g
                ? $"({Caom2Format.Number(g.X)}, {Caom2Format.Number(g.Y)}, {Caom2Format.Number(g.Z)}) m"
                : "—")) : null;

        Border? envCard = obs.Environment is { } e ? Card(Loc.T("ObsDetail_CardEnvironment"),
            (Loc.T("ObsDetail_RowSeeing"), Caom2Format.Number(e.Seeing)),
            (Loc.T("ObsDetail_RowHumidity"), Caom2Format.Number(e.Humidity)),
            (Loc.T("ObsDetail_RowElevation"), Caom2Format.Degrees(e.Elevation)),
            (Loc.T("ObsDetail_RowAmbientTemp"), Caom2Format.Number(e.AmbientTemp)),
            (Loc.T("ObsDetail_RowPhotometric"), Caom2Format.Bool(e.Photometric))) : null;

        OverviewPanel.Children.Add(TwoColumn(scopeCard, envCard));
    }

    private void BuildCoverage(CAOM2Observation obs)
    {
        CoveragePanel.Children.Clear();
        ForEachPlane(obs, CoveragePanel, (plane, into) =>
        {
            if (plane.Position is { } pos)
            {
                var spatial = Card(Loc.T("ObsDetail_CardSpatial"),
                    (Loc.T("ObsDetail_RowFootprint"), pos.Polygon.Count >= 3 ? "" : "—"),
                    (Loc.T("ObsDetail_RowDimensions"), pos.DimensionPixels is { } d ? $"{d.NAxis1} × {d.NAxis2} px" : "—"),
                    (Loc.T("ObsDetail_RowResolution"), pos.ResolutionArcsec is { } r ? $"{Caom2Format.Number(r)}″" : "—"),
                    (Loc.T("ObsDetail_RowSampleSize"), pos.SampleSizeArcsec is { } s ? $"{Caom2Format.Number(s)}″" : "—"));
                if (pos.Polygon.Count >= 3 && BuildFootprint(pos.Polygon) is { } fp && spatial.Child is StackPanel sp)
                    sp.Children.Insert(1, fp);
                into.Children.Add(spatial);
            }
            if (plane.Energy is { } en)
                into.Children.Add(Card(Loc.T("ObsDetail_CardSpectral"),
                    (Loc.T("ObsDetail_RowBandpass"), Caom2Format.Text(en.BandpassName)),
                    (Loc.T("ObsDetail_RowBand"), Caom2Format.Text(en.EmBand)),
                    (Loc.T("ObsDetail_RowWavelength"), Caom2Format.WavelengthRange(en.LowerMetres, en.UpperMetres)),
                    (Loc.T("ObsDetail_RowResolvingPower"), Caom2Format.Number(en.ResolvingPower)),
                    (Loc.T("ObsDetail_RowRestWavelength"), Caom2Format.Wavelength(en.RestWavMetres))));
            if (plane.Time is { } tm)
                into.Children.Add(Card(Loc.T("ObsDetail_CardTemporal"),
                    (Loc.T("ObsDetail_RowStart"), Caom2Format.MjdToDate(tm.LowerMJD)),
                    (Loc.T("ObsDetail_RowEnd"), Caom2Format.MjdToDate(tm.UpperMJD)),
                    (Loc.T("ObsDetail_RowExposure"), Caom2Format.Seconds(tm.ExposureSeconds))));
            if (plane.Polarization is { States.Count: > 0 } pol)
                into.Children.Add(Card(Loc.T("ObsDetail_CardPolarization"), (Loc.T("ObsDetail_RowStates"), string.Join(", ", pol.States))));
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
                    Text = Loc.T("ObsDetail_NoFiles"),
                    Foreground = Res("TextFillColorTertiaryBrush"),
                    Style = Sty("CaptionTextBlockStyle"),
                });
                return;
            }
            foreach (var art in plane.Artifacts)
                into.Children.Add(BuildArtifactRow(art));
        });

        var link = new HyperlinkButton { Content = Loc.T("ObsDetail_ViewAllFilesCadc"), Margin = new Thickness(0, 4, 0, 0) };
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
                    Text = Loc.T("ObsDetail_NoProvenance"),
                    Foreground = Res("TextFillColorTertiaryBrush"),
                    Style = Sty("CaptionTextBlockStyle"),
                });
                return;
            }
            any = true;
            into.Children.Add(Card(Loc.T("ObsDetail_CardPipeline"),
                (Loc.T("ObsDetail_RowName"), Caom2Format.Text(pv.Name)),
                (Loc.T("ObsDetail_RowVersion"), Caom2Format.Text(pv.Version)),
                (Loc.T("ObsDetail_RowProject"), Caom2Format.Text(pv.Project)),
                (Loc.T("ObsDetail_RowProducer"), Caom2Format.Text(pv.Producer)),
                (Loc.T("ObsDetail_RowRunId"), Caom2Format.Text(pv.RunID)),
                (Loc.T("ObsDetail_RowReference"), Caom2Format.Text(pv.Reference)),
                (Loc.T("ObsDetail_RowLastExecuted"), Caom2Format.Date(pv.LastExecuted))));

            if (pv.Inputs.Count > 0)
            {
                var inputsStack = new StackPanel { Spacing = 4 };
                inputsStack.Children.Add(Heading(Loc.T("ObsDetail_HeadingInputs")));
                foreach (var input in pv.Inputs)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    row.Children.Add(new FontIcon { Glyph = "", FontSize = 12, Foreground = Res("TextFillColorSecondaryBrush") });
                    row.Children.Add(new TextBlock { Text = input, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, Style = Sty("CaptionTextBlockStyle") });
                    AutomationProperties.SetName(row, Loc.F("ObsDetail_InputName", input));
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
                Text = Loc.F("ObsDetail_PlaneHeader",
                    plane.ProductID, Caom2Format.Text(plane.DataProductType), plane.CalibrationLevel?.ToString() ?? "?"),
                Style = Sty("BodyStrongTextBlockStyle"),
            });
            if (string.Equals(plane.Quality, "junk", StringComparison.OrdinalIgnoreCase))
                header.Children.Add(Chip(Loc.T("ObsDetail_JunkChip"), "SystemFillColorCriticalBrush", "TextOnAccentFillColorPrimaryBrush"));

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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var (bg, fg) = ArtifactBadge(art.ProductType);
        var badge = Chip(string.IsNullOrWhiteSpace(art.ProductType) ? Loc.T("ObsDetail_FileBadge") : art.ProductType!, bg, fg);
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
        ToolTipService.SetToolTip(dl, Loc.T("ObsDetail_DownloadTooltip"));
        AutomationProperties.SetName(dl, Loc.F("ObsDetail_DownloadFileName", Caom2Format.ArtifactFileName(art.Uri)));
        dl.Click += (_, _) => _ = OnDownloadArtifactAsync(art);
        Grid.SetColumn(dl, 4);
        grid.Children.Add(dl);

        // On every FITS file — the only kind SODA cuts; never on a preview, catalogue or package. Live
        // where CADC says THIS file can be cut (its DataLink answer has a service for it), greyed with
        // the reason where it does not.
        if (CutoutCandidates.IsFitsFile(art.ContentType, art.Uri, art.ProductType))
        {
            var cutout = _links?.CutoutFor(art.Uri);
            var cut = new Button { Content = Loc.T("Cutout_Button") };
            AutomationProperties.SetName(cut, Loc.F("Cutout_ButtonName", Caom2Format.ArtifactFileName(art.Uri)));
            if (cutout is not null)
            {
                ToolTipService.SetToolTip(cut, Loc.T("Cutout_ButtonTooltip"));
                cut.Click += (_, _) => ShowCutoutEditor(cutout, art, spec: null);
            }
            var cutWrapper = UIFactory.Explained(cut);
            UIFactory.Enable(cut, cutout is not null,
                _links is null ? Loc.T("Cutout_Checking") : Loc.T("Cutout_NoneForFile"));
            Grid.SetColumn(cutWrapper, 3);
            grid.Children.Add(cutWrapper);
        }

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

    /// <summary>
    /// The footprint as a sky chart — the cutout editor's canvas, read-only. It used to scale RA and
    /// Dec to fill a box separately, drawing every field the wrong shape, and broke across RA 0°.
    /// </summary>
    private FrameworkElement? BuildFootprint(IReadOnlyList<Caom2SkyVertex> poly)
    {
        if (poly.Count < 3) return null;
        var sketch = new FootprintCanvas
        {
            Footprint = poly.Select(v => new SkyPoint(v.Ra, v.Dec)).ToList(),
            Width = 200,
            Height = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            IsTabStop = false,
        };
        AutomationProperties.SetName(sketch,
            Loc.F("ObsDetail_FootprintName", poly.Count, poly.Min(p => p.Ra), poly.Max(p => p.Ra),
                  poly.Min(p => p.Dec), poly.Max(p => p.Dec)));
        return sketch;
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
            DownloadBar.Title = Loc.F("ObsDetail_Resolving", Caom2Format.ArtifactFileName(art.Uri));
            DownloadBar.Message = string.Empty;                     // stale error text from a prior run
            DownloadResearchRow.Visibility = Visibility.Collapsed;  // stale "Added to Research" claim
            DownloadText.Text = string.Empty;

            var links = await _dataLink.GetLinksAsync(_publisherID);
            var fileName = Caom2Format.ArtifactFileName(art.Uri);

            // Resolve the URL for THIS artifact, branching on its product type FIRST. Preview URLs
            // often embed the science file ID (…GetPreview?ID=cadc:CFHT/1234p.fits), so a blind
            // filename-contains scan across all lists can hand a science click the preview PNG —
            // or vice versa. Science/aux artifacts never consult the preview lists, previews never
            // fall back to the science file, and a missing link is an error, not the wrong file.
            var productType = art.ProductType?.ToLowerInvariant();
            var isPreviewType = productType is "preview" or "thumbnail";
            var url = isPreviewType
                ? MatchByFileName(links.Previews.Concat(links.Thumbnails), fileName)
                  ?? (productType == "thumbnail"
                      ? links.Thumbnails.FirstOrDefault() ?? links.Previews.FirstOrDefault()
                      : links.Previews.FirstOrDefault())
                : links.DirectFiles.FirstOrDefault(f =>
                      f.Filename.Equals(fileName, StringComparison.OrdinalIgnoreCase))?.Url
                  ?? MatchByFileName(links.DirectFiles.Select(f => f.Url), fileName)
                  ?? links.DirectFileUrl ?? _dataLink.GetDownloadUrl(_publisherID);
            if (url is null)
            {
                DownloadBar.Severity = InfoBarSeverity.Error;
                DownloadBar.Title = Loc.T("ObsDetail_DownloadFailed");
                DownloadBar.Message = Loc.F("ObsDetail_NoLinkForArtifact", fileName);
                return;
            }

            // Snapshot everything completion needs NOW: this page is a cached singleton, and the
            // user can open a different observation while the download streams — reading the live
            // fields at completion would stamp observation B's record with A's file.
            var ctx = new DownloadContext(
                _publisherID, _collection, _observationID, _current, links,
                IsScience: !isPreviewType && productType is null or "" or "science");

            await DownloadUrlToFileAsync(url, fileName, ctx);
        }
        catch (Exception ex)
        {
            ShowDownloadFailed(ex.Message);
        }
    }

    private void ShowDownloadFailed(string why)
    {
        DownloadBar.IsOpen = true;
        DownloadBar.Severity = InfoBarSeverity.Error;
        DownloadBar.Title = Loc.T("ObsDetail_DownloadFailed");
        DownloadBar.Message = why;
        DownloadText.Text = string.Empty;
    }

    /// <summary>Download state captured at START (the page is a cached singleton — live fields may
    /// describe a different observation by the time a long download completes).</summary>
    private sealed record DownloadContext(
        string PublisherID, string Collection, string ObservationID,
        CAOM2Observation? Observation, Models.DataLinkResult? Links, bool IsScience);

    /// <summary>Find a URL whose file name matches, tolerating URL-encoding ('+' → %2B) and
    /// query-embedded file IDs.</summary>
    private static string? MatchByFileName(IEnumerable<string> urls, string fileName)
    {
        foreach (var u in urls)
        {
            if (u.Contains(fileName, StringComparison.OrdinalIgnoreCase)) return u;
            if (Uri.TryCreate(u, UriKind.Absolute, out var uri))
            {
                if (System.IO.Path.GetFileName(uri.LocalPath)
                        .Equals(fileName, StringComparison.OrdinalIgnoreCase)) return u;
                if (Uri.UnescapeDataString(uri.Query).Contains(fileName, StringComparison.OrdinalIgnoreCase))
                    return u;
            }
        }
        return null;
    }

    private async Task DownloadUrlToFileAsync(string url, string suggestedName, DownloadContext ctx)
    {
        var file = await PickSaveFileAsync(suggestedName);
        if (file is null) { DownloadBar.IsOpen = false; return; }

        // Only the SCIENCE file lands in the Research archive: a calibration/aux FITS would clobber
        // the science record. Built now, from what this page knows at the start.
        var record = ctx.IsScience ? ResearchRecordFor(ctx) : null;
        await DownloadAndOfferAsync(new ObservationDownloadRequest(ctx.PublisherID, file.Path, record, Url: url),
            file.Name, ctx.PublisherID, addedToResearch: record is not null);
    }

    /// <summary>Where to save a file, asked with the file's own extension first; null when the person cancels.</summary>
    private static async Task<Windows.Storage.StorageFile?> PickSaveFileAsync(string suggestedName)
    {
        var hWnd = WindowHelper.ActiveWindows.Count > 0
            ? WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0])
            : nint.Zero;
        if (hWnd == nint.Zero) return null;

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        if (!System.IO.Path.HasExtension(suggestedName)) suggestedName += ".fits";
        picker.SuggestedFileName = suggestedName;
        // The picker ENFORCES the selected file type's extension: with only ".fits" offered, a
        // preview "x_preview_1024.png" became "x_preview_1024.png.fits" and fpack "x.fits.fz"
        // became "x.fits.fz.fits". Always offer the artifact's REAL extension first so every
        // file keeps its original name.
        var actualExt = System.IO.Path.GetExtension(suggestedName).ToLowerInvariant();
        if (actualExt.Length > 1 && actualExt != ".fits")
            picker.FileTypeChoices.Add(Loc.F("ObsDetail_FileTypeOriginal", actualExt), new List<string> { actualExt });
        picker.FileTypeChoices.Add(Loc.T("ObsDetail_FileTypeFits"), new List<string> { ".fits" });
        picker.FileTypeChoices.Add(Loc.T("ObsDetail_FileTypeAll"), new List<string> { "." });

        return await picker.PickSaveFileAsync();
    }

    /// <summary>
    /// Hand a download to the app — it runs and reports in the status bar whatever this page does next
    /// — then, if the page still shows the same observation when it lands, offer what to do with it.
    /// The one path for a whole file and for a cutout.
    /// </summary>
    private async Task DownloadAndOfferAsync(ObservationDownloadRequest request, string displayName,
                                             string publisherId, bool addedToResearch)
    {
        DownloadBar.IsOpen = true;
        DownloadBar.ActionButton = null;
        DownloadBar.Severity = InfoBarSeverity.Informational;
        DownloadBar.Title = Loc.F("ObsDetail_Downloading", displayName);
        DownloadBar.Message = string.Empty;
        DownloadText.Text = Loc.T("Download_InStatusBar");
        DownloadResearchRow.Visibility = Visibility.Collapsed;

        try
        {
            await _downloader.Start(request);
        }
        catch (Exception ex)
        {
            if (_publisherID == publisherId) ShowDownloadFailed(ex.Message);
            return;
        }

        // The page is a cached singleton: the person may be looking at another observation by now.
        if (_publisherID != publisherId) return;

        DownloadBar.Severity = InfoBarSeverity.Success;
        DownloadBar.Title = Loc.F("ObsDetail_Downloaded", displayName);
        DownloadText.Text = string.Empty;
        await OfferDownloadedAsync(request.TargetPath, addedToResearch);
    }

    private async Task OfferDownloadedAsync(string savedPath, bool addedToResearch)
    {

        // Classify by CONTENT, not extension — a mis-served download can put FITS bytes in a
        // ".png" (and vice versa), and that is exactly the case the suggestion must survive.
        // Off the UI thread: the file was just written, so AV on-write scans can hold the open.
        var shape = await Task.Run(() => FitsSniff.Inspect(savedPath));

        if (shape.Kind != FitsKind.NotFits)
        {
            // Recommend the right viewer but never restrict: a spectral cube defaults to the 3D Cube
            // Viewer, a 2D image / detector stack to the FITS viewer — and whenever a 3rd axis exists
            // the user can pick the other from the dropdown.
            DownloadBar.ActionButton = BuildViewerButton(savedPath, shape);

            if (addedToResearch)
            {
                DownloadResearchText.Text = Loc.T("ObsDetail_AddedToResearch");
                DownloadResearchLink.Content = Loc.T("ObsDetail_ViewInResearch");
                DownloadResearchRow.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // Preview PNG / README / other sidecar: shell-open with the OS default app.
            var open = new Button { Content = Loc.T("ObsDetail_OpenDownloaded") };
            open.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo { FileName = savedPath, UseShellExecute = true });
                }
                catch { }
            };
            DownloadBar.ActionButton = open;
        }
    }

    private void OnViewInResearchClick(object sender, RoutedEventArgs e) => ViewInResearchRequested?.Invoke();

    /// <summary>Build the "open in viewer" action. Pure 2D → a single FITS-viewer button. A file with
    /// a real third axis → a dropdown offering BOTH viewers (recommendation reflected in the label:
    /// spectral cube → Cube Viewer, detector stack → FITS Viewer), so the user is never restricted.</summary>
    private Microsoft.UI.Xaml.Controls.Primitives.ButtonBase BuildViewerButton(string path, FitsShape shape)
    {
        if (!shape.HasCubeAxis)
        {
            var b = new Button { Content = Loc.T("ObsDetail_OpenInFitsViewer") };
            b.Click += (_, _) => OpenInFitsRequested?.Invoke(path);
            return b;
        }

        var fitsItem = new MenuFlyoutItem { Text = Loc.T("ObsDetail_OpenInFitsViewer") };
        fitsItem.Click += (_, _) => OpenInFitsRequested?.Invoke(path);
        var cubeItem = new MenuFlyoutItem { Text = Loc.T("ObsDetail_OpenInCubeViewer") };
        cubeItem.Click += (_, _) => OpenInCubeRequested?.Invoke(path);

        var flyout = new MenuFlyout();
        if (shape.RecommendCube) { flyout.Items.Add(cubeItem); flyout.Items.Add(fitsItem); }
        else { flyout.Items.Add(fitsItem); flyout.Items.Add(cubeItem); }

        return new DropDownButton
        {
            Content = Loc.T(shape.RecommendCube ? "ObsDetail_OpenInCubeViewer" : "ObsDetail_OpenInFitsViewer"),
            Flyout = flyout,
        };
    }

    /// <summary>
    /// The Research record for something downloaded from this page — the shared builder, with the
    /// collection and id this page read from the publisher id in case CAOM2 had nothing to say.
    /// </summary>
    private static DownloadedObservation ResearchRecordFor(DownloadContext ctx, CutoutSpec? cutout = null)
    {
        var record = ResearchRecords.ForObservation(ctx.PublisherID, ctx.Observation, ctx.Links, cutout);
        if (string.IsNullOrEmpty(record.Collection)) record.Collection = ctx.Collection;
        if (string.IsNullOrEmpty(record.ObservationID)) record.ObservationID = ctx.ObservationID;
        return record;
    }

    // ── Cutouts ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Learn which files can be cut out — DataLink describes a service for each — and rebuild the Files
    /// tab with a Cutout button where there is one. After the observation shows, not before: it is a
    /// second request, and the observation is worth seeing without waiting for it.
    /// </summary>
    private async Task LoadCutoutServicesAsync(CAOM2Observation obs)
    {
        var publisherId = _publisherID;
        DataLinkResult links;
        try { links = await _dataLink.GetLinksAsync(publisherId); }
        catch { links = new DataLinkResult(); } // the observation still shows; its files say they cannot be cut

        if (_publisherID != publisherId || !ReferenceEquals(_current, obs)) return;
        _links = links;
        BuildFiles(obs); // either way: the buttons go from "checking" to what the answer said
    }

    /// <summary>The files of the open observation that can be cut out.</summary>
    public IReadOnlyList<SodaDescriptor> CutoutServices => _links?.Cutouts ?? [];

    /// <summary>
    /// Open the cutout editor for one file at the top of the Files tab, starting from the last search
    /// (its target and wavelengths) or, when given, from <paramref name="spec"/> — an agent's proposal.
    /// </summary>
    public CutoutEditor ShowCutoutEditor(SodaDescriptor file, Caom2Artifact? art, CutoutSpec? spec)
    {
        CloseCutoutEditor();

        var hints = _search.CutoutHints;
        var target = hints is { Ra: { } ra, Dec: { } dec } ? new SkyPoint(ra, dec) : (SkyPoint?)null;
        var size = art?.ContentLength ?? ArtifactFor(file)?.ContentLength;
        var editor = new CutoutEditor(file, CutoutPrefill.Suggest(file, hints), size, target);
        if (spec is not null) editor.Load(spec);
        editor.DownloadRequested += chosen => _ = OnDownloadCutoutAsync(file, chosen);
        editor.CloseRequested += CloseCutoutEditor;

        FilesPanel.Children.Insert(0, editor);
        _cutoutEditor = editor;
        DetailPivot.SelectedItem = FilesPanel.Parent is ScrollViewer { Parent: PivotItem tab } ? tab : DetailPivot.SelectedItem;
        editor.StartBringIntoView();
        return editor;
    }

    private void CloseCutoutEditor()
    {
        if (_cutoutEditor is { } open) FilesPanel.Children.Remove(open);
        _cutoutEditor = null;
    }

    private Caom2Artifact? ArtifactFor(SodaDescriptor file)
        => _current?.Planes.SelectMany(p => p.Artifacts).FirstOrDefault(a => a.Uri == file.ArtifactId);

    /// <summary>
    /// Download a cutout the editor has approved: checked once more, saved where the person says, and
    /// handed to the app like any download — recorded in Research as a cutout of this observation.
    /// </summary>
    private async Task OnDownloadCutoutAsync(SodaDescriptor file, CutoutSpec spec)
    {
        string url;
        try { url = SodaRequest.Url(file, spec); }
        catch (InvalidOperationException ex) { ShowDownloadFailed(ex.Message); return; }

        var saveAs = await PickSaveFileAsync(spec.FileNameFor(file.FileName));
        if (saveAs is null) return;

        var ctx = new DownloadContext(_publisherID, _collection, _observationID, _current, _links, IsScience: true);
        await DownloadAndOfferAsync(
            new ObservationDownloadRequest(ctx.PublisherID, saveAs.Path, ResearchRecordFor(ctx, spec), Url: url),
            saveAs.Name, ctx.PublisherID, addedToResearch: true);
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
    public event Action<string>? OpenInFitsRequested;
    public event Action? ViewInResearchRequested;

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    #endregion
}
