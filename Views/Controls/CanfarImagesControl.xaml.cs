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

    public string StatusTooltip => Status switch
    {
        ImageDiscoveryStatus.Discovered => "Manifest cached",
        ImageDiscoveryStatus.Failed => "Last probe failed",
        _ => "Not yet inspected",
    };

    partial void OnStatusChanged(ImageDiscoveryStatus value)
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusTooltip));
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
    private readonly ObservableCollection<CanfarImageRow> _rows = new();
    private List<RawImage> _images = new();

    /// <summary>Raised when the user picks an image via "Use this image" in the find-by-package dialog.</summary>
    public event EventHandler<string>? UseImageRequested;

    public CanfarImagesControl(IImageService imageService, ImageDiscoveryCoordinator coordinator, ImageDiscoverySettingsService settings)
    {
        InitializeComponent();
        _imageService = imageService;
        _coordinator = coordinator;
        _settings = settings;
        ImageList.ItemsSource = _rows;
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

        CountText.Text = $"({_images.Count})";

        var types = _images.SelectMany(i => i.Types).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        TypeSelector.Items.Clear();
        foreach (var type in types)
            TypeSelector.Items.Add(new SelectorBarItem { Text = Capitalize(type), Tag = type });

        if (TypeSelector.Items.Count > 0)
            TypeSelector.SelectedItem = TypeSelector.Items[0];
    }

    private void OnTypeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var type = sender.SelectedItem?.Tag as string ?? string.Empty;
        _rows.Clear();
        var rows = _images
            .Where(i => i.Types.Contains(type))
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

        // Cached outcome shows instantly without re-probing.
        if (_coordinator.Outcome(row.ImageId) is { IsSuccess: true })
        {
            ApplyStatus(row);
            return;
        }

        row.MetaLine = "Inspecting…";
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
            row.MetaLine = $"{os}{PackageCount(m)} packages";
        }
        else if (outcome is { IsSuccess: false })
        {
            row.Status = ImageDiscoveryStatus.Failed;
            row.MetaLine = outcome.Message ?? "Probe failed";
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
        var sorted = _rows
            .OrderBy(r => StatusOrder(r.Status))
            .ThenBy(r => r.ImageId, StringComparer.Ordinal)
            .ToList();
        _rows.Clear();
        foreach (var row in sorted) _rows.Add(row);
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
