using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views.Controls;

/// <summary>One image row in the Canfar Images widget, with a live discovery status.</summary>
public partial class CanfarImageRow : ObservableObject
{
    public string ImageId { get; }
    public string Label { get; }

    [ObservableProperty]
    private string _status = string.Empty;

    public CanfarImageRow(string imageId, string label)
    {
        ImageId = imageId;
        Label = label;
    }
}

/// <summary>
/// Dashboard widget listing CANFAR session container images by type, with a per-image "Inspect"
/// action that runs the image-discovery probe and surfaces the package count inline.
/// </summary>
public sealed partial class CanfarImagesControl : UserControl
{
    private readonly IImageService _imageService;
    private readonly ImageDiscoveryCoordinator _coordinator;
    private readonly ObservableCollection<CanfarImageRow> _rows = new();
    private List<RawImage> _images = new();

    public CanfarImagesControl(IImageService imageService, ImageDiscoveryCoordinator coordinator)
    {
        InitializeComponent();
        _imageService = imageService;
        _coordinator = coordinator;
        ImageList.ItemsSource = _rows;
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
        foreach (var image in _images.Where(i => i.Types.Contains(type)).OrderBy(i => i.Id, StringComparer.Ordinal))
            _rows.Add(new CanfarImageRow(image.Id, ToLabel(image.Id)));
    }

    private async void OnFindByPackage(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ImageDiscoveryDialog(_coordinator) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discovery dialog error: {ex.Message}");
        }
    }

    private async void OnInspectClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not CanfarImageRow row) return;

        // Show a cached outcome instantly without re-probing.
        var cached = _coordinator.Outcome(row.ImageId);
        if (cached is { IsSuccess: true, Manifest: { } cachedManifest })
        {
            row.Status = $"{PackageCount(cachedManifest)} packages (cached)";
            return;
        }

        row.Status = "Discovering…";
        try
        {
            var manifest = await _coordinator.DiscoverAsync(row.ImageId);
            row.Status = $"{PackageCount(manifest)} packages found";
        }
        catch (ImageDiscoveryException ide)
        {
            row.Status = ide.Message;
        }
        catch (Exception ex)
        {
            row.Status = ex.Message;
        }
    }

    private static int PackageCount(ImageManifest m)
        => m.DpkgPackages.Count + m.RpmPackages.Count + m.ApkPackages.Count + m.PythonPackages.Count + m.RPackages.Count;

    private static string ToLabel(string imageId)
    {
        var slash = imageId.LastIndexOf('/');
        return slash >= 0 && slash < imageId.Length - 1 ? imageId[(slash + 1)..] : imageId;
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
