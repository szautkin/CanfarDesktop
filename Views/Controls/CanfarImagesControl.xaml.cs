using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views.Controls;

/// <summary>One image row in the Canfar Images widget, with a live discovery status glyph.</summary>
public partial class CanfarImageRow : ObservableObject
{
    public string ImageId { get; }
    public string Label { get; }
    public string[] Types { get; }

    [ObservableProperty] private ImageDiscoveryStatus _status;
    [ObservableProperty] private string _metaLine = string.Empty;

    public CanfarImageRow(string imageId, string label, string[] types)
    {
        ImageId = imageId;
        Label = label;
        Types = types;
    }

    // Segoe Fluent glyphs: discovered = filled check circle, failed = warning, unknown = empty circle.
    public string StatusGlyph => char.ConvertFromUtf32(Status switch
    {
        ImageDiscoveryStatus.Discovered => 0xEC61, // CompletedSolid (filled check circle)
        ImageDiscoveryStatus.Failed => 0xE7BA,      // Warning
        _ => 0xECCA,                                // RadioBtnOff (empty circle)
    });

    public Brush StatusBrush => (Brush)Application.Current.Resources[Status switch
    {
        ImageDiscoveryStatus.Discovered => "SystemFillColorSuccessBrush",
        ImageDiscoveryStatus.Failed => "SystemFillColorCautionBrush",
        _ => "TextFillColorTertiaryBrush",
    }];

    /// <summary>
    /// What the row's one button does right now. An image nobody has probed needs probing; a probed one
    /// has contents worth reading.
    /// </summary>
    public string ActionLabel => Status == ImageDiscoveryStatus.Discovered
        ? Helpers.Loc.T("Images_ContentsBtn")
        : Helpers.Loc.T("Images_RowInspectBtn");

    public string ActionTooltip => Status == ImageDiscoveryStatus.Discovered
        ? Helpers.Loc.T("Images_ContentsTooltip")
        : Helpers.Loc.T("Images_InspectTooltip");

    public string StatusTooltip => Status switch
    {
        ImageDiscoveryStatus.Discovered => Helpers.Loc.T("Images_StatusDiscovered"),
        ImageDiscoveryStatus.Failed => Helpers.Loc.T("Images_StatusFailed"),
        _ => Helpers.Loc.T("Images_StatusUnknown"),
    };

    partial void OnStatusChanged(ImageDiscoveryStatus value)
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ActionTooltip));
    }
}

/// <summary>
/// Dashboard widget listing the CANFAR images a launch can start, narrowed by session type and then by
/// project, each with a live discovery status (inspected first, then failed, then never inspected) and one
/// action — Inspect, or the contents of an image already inspected. The rules are
/// <see cref="ImageCatalogue"/>'s, shared with Find by package and the agents' image listing.
/// </summary>
public sealed partial class CanfarImagesControl : UserControl
{
    private readonly IImageService _imageService;
    private readonly ImageDiscoveryCoordinator _coordinator;
    private readonly ImageDiscoverySettingsService _settings;
    private readonly IUserImageStore _userImages;
    private readonly ObservableCollection<CanfarImageRow> _rows = new();

    /// <summary>The images a launch can start, as the catalogue gave them — what Find by package searches.</summary>
    private List<RawImage> _launchable = new();

    /// <summary>The same images, parsed — what the filters and rows read.</summary>
    private List<ParsedImage> _images = new();

    private string _type = ImageCatalogue.All;
    private string _project = ImageCatalogue.All;

    /// <summary>True while a chip row is being filled, when its selection changing is ours, not the person's.</summary>
    private bool _filling;

    /// <summary>Raised when the user picks an image via "Use this image" in the find-by-package dialog.</summary>
    public event EventHandler<string>? UseImageRequested;

    public CanfarImagesControl(IImageService imageService, ImageDiscoveryCoordinator coordinator,
                               ImageDiscoverySettingsService settings, IUserImageStore userImages)
    {
        InitializeComponent();
        _imageService = imageService;
        _coordinator = coordinator;
        _settings = settings;
        _userImages = userImages;
        ImageList.ItemsSource = _rows;

        // The user's own images belong in the same card as the platform's: one merged catalogue, so the
        // card, the package search and the launch form are all looking at the same list.
        _userImages.Changed += () => DispatcherQueue.TryEnqueue(() => _ = LoadAsync());
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ImageDiscoverySettingsDialog.ShowAsync(XamlRoot);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image discovery settings error: {ex.Message}");
        }
    }

    public async Task LoadAsync()
    {
        List<RawImage> catalogue;
        try
        {
            catalogue = await _imageService.GetImagesAsync();
        }
        catch (Exception ex)
        {
            DiscoveredText.Text = Helpers.Loc.F("Images_LoadFailed", ex.Message);
            return;
        }

        // The user's own additions, merged in and de-duplicated against the catalogue.
        var mine = _userImages.All().Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var added in _userImages.All())
            if (!catalogue.Any(i => string.Equals(i.Id, added.Id, StringComparison.OrdinalIgnoreCase)))
                catalogue.Add(new RawImage { Id = added.Id, Types = added.Types.ToArray() });

        // Only what a launch can start; the count says as much.
        var launchable = catalogue
            .Select(raw => (Raw: raw, Image: ImageParser.Parse(raw)))
            .Where(x => ImageCatalogue.Launchable(x.Image, mine))
            .ToList();
        _launchable = launchable.Select(x => x.Raw).ToList();
        _images = launchable.Select(x => x.Image).ToList();
        CountText.Text = $"({_images.Count})";

        // A choice kept while it is still offered; otherwise All, never the first type — an image just
        // added may name no type at all, and landing on one would hide it from the list it was added to.
        var types = ImageCatalogue.Types(_images);
        _type = ImageCatalogue.Surviving(_type, types);
        Fill(TypeSelector, "ImagesType", types.Select(t => (t, SessionTypes.Label(t))), _type);

        FillProjects();
        RebuildRows();
    }

    /// <summary>The project row, from the projects the chosen type leaves; a project gone with it takes the choice back to All.</summary>
    private void FillProjects()
    {
        var projects = ImageCatalogue.Projects(_images, _type);
        _project = ImageCatalogue.Surviving(_project, projects);
        Fill(ProjectFilter, "ImagesProject", projects.Select(p => (p, p)), _project);
        ProjectRow.Visibility = projects.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Fill a chip row: All first, then each choice, the chosen one selected. One builder for both rows, so
    /// both mean the same by All. Each chip is named for agents to point at: ImagesType[notebook].
    /// </summary>
    private void Fill(SelectorBar bar, string name, IEnumerable<(string Value, string Label)> choices, string chosen)
    {
        _filling = true;
        try
        {
            bar.Items.Clear();
            SelectorBarItem? selected = null;
            foreach (var (value, label) in choices.Prepend((ImageCatalogue.All, Helpers.Loc.T("Images_All"))))
            {
                var chip = new SelectorBarItem { Text = label, Tag = value, Name = $"{name}[{(value.Length == 0 ? "All" : value)}]" };
                bar.Items.Add(chip);
                if (string.Equals(value, chosen, StringComparison.OrdinalIgnoreCase)) selected = chip;
            }
            bar.SelectedItem = selected;
        }
        finally
        {
            _filling = false;
        }
    }

    private void OnTypeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_filling || sender.SelectedItem?.Tag is not string type) return;
        _type = type;
        FillProjects(); // the projects on offer depend on the type
        RebuildRows();
    }

    private void OnProjectChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_filling || sender.SelectedItem?.Tag is not string project) return;
        _project = project;
        RebuildRows();
    }

    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var image in ImageCatalogue.Shown(_images, _type, _project, StatusOf))
        {
            var row = new CanfarImageRow(image.Id, image.Label, image.Types);
            ApplyStatus(row);
            _rows.Add(row);
        }
        ShowDiscovered();
    }

    /// <summary>"Discovered 3 of 12 images" — of the rows shown, not the whole catalogue, since it sits right above them.</summary>
    private void ShowDiscovered()
        => DiscoveredText.Text = _images.Count == 0 ? string.Empty
            : Helpers.Loc.F("Images_DiscoveredOf", _rows.Count(r => r.Status == ImageDiscoveryStatus.Discovered), _rows.Count);

    private async Task ShowContentsAsync(Models.ImageDiscovery.ImageManifest manifest)
    {
        try
        {
            var dialog = new ImageDetailDialog { XamlRoot = XamlRoot };
            dialog.Initialize(manifest);

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.PickedImageId is { } picked)
                UseImageRequested?.Invoke(this, picked);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image detail error: {ex.Message}");
        }
    }

    /// <summary>
    /// The other door: images the catalogue does not list. Only ever opened on purpose — nothing here
    /// searches the registry on its own.
    /// </summary>
    private async void OnFindInRegistry(object sender, RoutedEventArgs e)
    {
        try
        {
            var registry = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<IRegistryService>(App.Services);

            var dialog = new RegistryBrowserDialog(registry, _userImages, _settings) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();

            // Anything added is in the merged catalogue now; the store's Changed event reloads the card.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Registry browser error: {ex.Message}");
        }
    }

    private async void OnFindByPackage(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ImageDiscoveryDialog(_coordinator, _launchable) { XamlRoot = XamlRoot };
            var result = await dialog.ShowAsync();
            RefreshStatuses(); // the dialog may have probed images
            if (result == ContentDialogResult.Primary && dialog.PickedImageId is { } imageId)
                UseImageRequested?.Invoke(this, imageId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discovery dialog error: {ex.Message}");
        }
    }

    private async void OnInspectClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not CanfarImageRow row) return;

        // Already probed: show what is in it rather than probing again. The card could say an image had
        // been probed and how many packages it held, but not WHICH — so choosing between two images that
        // both "have astropy" meant launching one and looking.
        if (_coordinator.Outcome(row.ImageId) is { IsSuccess: true, Manifest: { } manifest })
        {
            ApplyStatus(row);
            await ShowContentsAsync(manifest);
            return;
        }

        row.MetaLine = Helpers.Loc.T("Images_Inspecting");
        try
        {
            await _coordinator.DiscoverAsync(row.ImageId);
        }
        catch
        {
            // Failure is persisted in the cache; ApplyStatus reflects it.
        }
        ApplyStatus(row);
        ReSort();
    }

    /// <summary>What is known of an image's contents, from the discovery cache.</summary>
    private ImageDiscoveryStatus StatusOf(string imageId) => _coordinator.Outcome(imageId) switch
    {
        { IsSuccess: true, Manifest: not null } => ImageDiscoveryStatus.Discovered,
        { IsSuccess: false } => ImageDiscoveryStatus.Failed,
        _ => ImageDiscoveryStatus.Unknown,
    };

    private void ApplyStatus(CanfarImageRow row)
    {
        var outcome = _coordinator.Outcome(row.ImageId);
        row.Status = StatusOf(row.ImageId);
        row.MetaLine = (row.Status, outcome) switch
        {
            (ImageDiscoveryStatus.Discovered, { Manifest: { } m }) =>
                (m.OsFamily != "unknown" ? $"{m.OsFamily} {m.OsVersion} · " : string.Empty) + Helpers.Loc.F("Images_Packages", PackageCount(m)),
            (ImageDiscoveryStatus.Failed, _) => outcome?.Message ?? Helpers.Loc.T("Images_ProbeFailed"),
            _ => row.ImageId,
        };
    }

    private void RefreshStatuses()
    {
        foreach (var row in _rows) ApplyStatus(row);
        ReSort();
    }

    private void ReSort()
    {
        // In-place Move (instead of Clear+re-add) keeps the ListView's scroll
        // position when an Inspect result re-orders the list.
        var sorted = _rows
            .OrderBy(r => ImageCatalogue.Order(r.Status))
            .ThenBy(r => r.ImageId, StringComparer.Ordinal)
            .ToList();
        for (var target = 0; target < sorted.Count; target++)
        {
            var current = _rows.IndexOf(sorted[target]);
            if (current != target) _rows.Move(current, target);
        }
        ShowDiscovered();
    }

    private static int PackageCount(ImageManifest m)
        => m.DpkgPackages.Count + m.RpmPackages.Count + m.ApkPackages.Count + m.PythonPackages.Count + m.RPackages.Count;
}
