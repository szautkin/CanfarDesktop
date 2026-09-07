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

/// <summary>3-state discovery status for an image row (mirrors macOS CanfarImageRow.Status).</summary>
public enum ImageDiscoveryStatus { Unknown, Discovered, Failed }

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
        : Helpers.Loc.T("Images_InspectBtn");

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
/// Dashboard widget listing CANFAR session container images by type, each with a per-image "Inspect"
/// action and a live discovery status glyph (discovered / failed / not-inspected, discovered first).
/// </summary>
public sealed partial class CanfarImagesControl : UserControl
{
    private readonly IImageService _imageService;
    private readonly ImageDiscoveryCoordinator _coordinator;
    private readonly ImageDiscoverySettingsService _settings;
    private readonly IUserImageStore _userImages;
    private readonly ObservableCollection<CanfarImageRow> _rows = new();
    private List<RawImage> _images = new();

    /// <summary>
    /// A published application, not a session anyone can start.
    ///
    /// Seventy-seven of the platform's images carry this and nothing else — every CASA tag back to
    /// 3.4.0 among them. They were a fifth of this card, and Inspect on one spent a real probe job on an
    /// image no launch tab would ever offer.
    /// </summary>
    private const string DesktopAppType = "desktop-app";

    /// <summary>The project a reference belongs to: host/PROJECT/name:tag.</summary>
    private static string? ProjectOf(string imageId)
    {
        var parts = (imageId ?? string.Empty).Split('/');
        return parts.Length >= 3 ? parts[1] : null;
    }

    /// <summary>Every project shown, in the order the filter offers them.</summary>
    private const string AllProjects = "__all__";

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
        try
        {
            _images = await _imageService.GetImagesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Canfar images load failed: {ex.Message}");
            return;
        }

        // The user's own additions, merged in and de-duplicated against the catalogue.
        foreach (var mine in _userImages.All())
            if (!_images.Any(i => string.Equals(i.Id, mine.Id, StringComparison.OrdinalIgnoreCase)))
                _images.Add(new RawImage { Id = mine.Id, Types = mine.Types.ToArray() });

        CountText.Text = $"({_images.Count})";

        // Only the types a launch tab can actually offer. desktop-app is an application published INSIDE
        // a desktop session, not a session to start, so a tab for it offers nothing that can be launched.
        var types = _images
            .SelectMany(i => i.Types)
            .Where(t => !string.Equals(t, DesktopAppType, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        TypeSelector.Items.Clear();
        foreach (var type in types)
            TypeSelector.Items.Add(new SelectorBarItem { Text = Capitalize(type), Tag = type });

        if (TypeSelector.Items.Count > 0)
            TypeSelector.SelectedItem = TypeSelector.Items[0];

        BuildProjectFilter();
    }

    /// <summary>
    /// The projects present, so a card of several hundred images can be narrowed to the one collection
    /// someone works in. Rebuilt on load because an added image can bring a project with it.
    /// </summary>
    private void BuildProjectFilter()
    {
        var previous = (ProjectFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? AllProjects;

        var projects = _images
            .Select(i => ProjectOf(i.Id))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ProjectFilter.Items.Clear();
        ProjectFilter.Items.Add(new ComboBoxItem { Content = Helpers.Loc.T("Images_AllProjects"), Tag = AllProjects });
        foreach (var project in projects)
            ProjectFilter.Items.Add(new ComboBoxItem { Content = project, Tag = project });

        ProjectFilter.SelectedIndex = Math.Max(0, ProjectFilter.Items
            .OfType<ComboBoxItem>()
            .ToList()
            .FindIndex(i => (string?)i.Tag == previous));

        ProjectFilter.Visibility = projects.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnProjectChanged(object sender, SelectionChangedEventArgs e) => RebuildRows();

    private void OnTypeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => RebuildRows();

    private void RebuildRows()
    {
        var type = TypeSelector.SelectedItem?.Tag as string ?? string.Empty;
        var project = (ProjectFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? AllProjects;

        _rows.Clear();
        var rows = _images
            .Where(i => i.Types.Contains(type))
            .Where(i => project == AllProjects
                     || string.Equals(ProjectOf(i.Id), project, StringComparison.OrdinalIgnoreCase))
            .Select(ImageParser.Parse)
            .Select(p =>
            {
                var row = new CanfarImageRow(p.Id, p.Label, p.Types);
                ApplyStatus(row);
                return row;
            })
            .OrderBy(r => StatusOrder(r.Status))
            .ThenBy(r => r.ImageId, StringComparer.Ordinal);
        foreach (var row in rows) _rows.Add(row);
    }

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
            var dialog = new ImageDiscoveryDialog(_coordinator, _images) { XamlRoot = XamlRoot };
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

    private void ApplyStatus(CanfarImageRow row)
    {
        var outcome = _coordinator.Outcome(row.ImageId);
        if (outcome is { IsSuccess: true, Manifest: { } m })
        {
            row.Status = ImageDiscoveryStatus.Discovered;
            var os = m.OsFamily != "unknown" ? $"{m.OsFamily} {m.OsVersion} · " : string.Empty;
            row.MetaLine = os + Helpers.Loc.F("Images_Packages", PackageCount(m));
        }
        else if (outcome is { IsSuccess: false })
        {
            row.Status = ImageDiscoveryStatus.Failed;
            row.MetaLine = outcome.Message ?? Helpers.Loc.T("Images_ProbeFailed");
        }
        else
        {
            row.Status = ImageDiscoveryStatus.Unknown;
            row.MetaLine = row.ImageId;
        }
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
            .OrderBy(r => StatusOrder(r.Status))
            .ThenBy(r => r.ImageId, StringComparer.Ordinal)
            .ToList();
        for (var target = 0; target < sorted.Count; target++)
        {
            var current = _rows.IndexOf(sorted[target]);
            if (current != target) _rows.Move(current, target);
        }
    }

    private static int StatusOrder(ImageDiscoveryStatus s) => s switch
    {
        ImageDiscoveryStatus.Discovered => 0,
        ImageDiscoveryStatus.Failed => 1,
        _ => 2,
    };

    private static int PackageCount(ImageManifest m)
        => m.DpkgPackages.Count + m.RpmPackages.Count + m.ApkPackages.Count + m.PythonPackages.Count + m.RPackages.Count;

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
