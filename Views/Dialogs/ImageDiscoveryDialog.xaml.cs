using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Searches the image-discovery cache: pick Python packages on the left (virtualized multi-select
/// list — never a CheckBox-per-row list, per the data-train memory) and see which discovered images
/// contain all of them on the right. Images must be probed first (via the Canfar Images widget's
/// Inspect) to appear here.
/// </summary>
public sealed partial class ImageDiscoveryDialog : ContentDialog
{
    private readonly ImageDiscoveryCoordinator _coordinator;
    private List<string> _packages = new();

    public ImageDiscoveryDialog(ImageDiscoveryCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        Load();
    }

    private void Load()
    {
        _packages = _coordinator.AllPackages().Python.OrderBy(p => p, StringComparer.Ordinal).ToList();
        PackageList.ItemsSource = _packages;
        UpdateMatches();
    }

    private void OnPackageSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMatches();

    private void UpdateMatches()
    {
        var query = new PackageQuery();
        foreach (var item in PackageList.SelectedItems)
            if (item is string s) query.Python.Add(s);

        if (query.IsEmpty)
        {
            MatchList.ItemsSource = null;
            MatchHeader.Text = "Select packages to find images";
            return;
        }

        var matches = _coordinator.Search(query);
        MatchList.ItemsSource = matches.Select(ToLabel).ToList();
        MatchHeader.Text = $"Matching images ({matches.Count})";
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();
        if (text.Length == 0) return;
        var match = _packages.FirstOrDefault(p => p.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        if (match is not null) PackageList.ScrollIntoView(match);
    }

    private static string ToLabel(string imageId)
    {
        var slash = imageId.LastIndexOf('/');
        return slash >= 0 && slash < imageId.Length - 1 ? imageId[(slash + 1)..] : imageId;
    }
}
