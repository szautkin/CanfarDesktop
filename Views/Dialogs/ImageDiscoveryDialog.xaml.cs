using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using CanfarDesktop.Models;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.ViewModels.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Find-images-by-package: a faceted filter over the discovery cache. Left pane = checkbox sections
/// across every ecosystem (OS family/version, Python, R, apt/dpkg, rpm, apk) with auto-disable
/// faceting + a session-type filter; right pane = the matching images grouped by project, each
/// inspectable in place. 1-to-1 with the macOS Image Discovery sheet.
/// </summary>
public sealed partial class ImageDiscoveryDialog : ContentDialog
{
    private readonly ImageDiscoveryViewModel _viewModel;

    /// <summary>The image the user committed with "Use this image" (null if they closed).</summary>
    public string? PickedImageId { get; private set; }

    public ImageDiscoveryDialog(ImageDiscoveryCoordinator coordinator, IReadOnlyList<RawImage> catalogue)
    {
        InitializeComponent();
        _viewModel = new ImageDiscoveryViewModel(coordinator);
        DataContext = _viewModel;
        _viewModel.Load(catalogue);

        // Grouped, virtualized right-pane list (project sections over FilteredGroups).
        var grouped = new CollectionViewSource
        {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath("Images"),
            Source = _viewModel.FilteredGroups,
        };
        MatchListView.ItemsSource = grouped.View;

        PrimaryButtonClick += (_, _) => PickedImageId = _viewModel.SelectedRow?.Id;
    }
}
