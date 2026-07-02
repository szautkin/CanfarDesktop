using Windows.ApplicationModel.DataTransfer;
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

    private void OnCopyJson(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Detail is not { } detail || string.IsNullOrEmpty(detail.Json)) return;
        var data = new DataPackage();
        data.SetText(detail.Json);
        Clipboard.SetContent(data);
    }

    // The per-ecosystem package lists stay virtualized (they can hold thousands of
    // dpkg rows), so they keep their own ScrollViewer. Forward the mouse wheel to
    // the detail pane whenever an inner list can't scroll further, so hovering a
    // package list doesn't trap the wheel.
    private void OnPackageListLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || Equals(list.Tag, "wheelHooked")) return;
        list.Tag = "wheelHooked";
        list.AddHandler(PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPackageListWheel), handledEventsToo: true);
    }

    private void OnPackageListWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not ListView list) return;
        var inner = Helpers.VisualTree.FindDescendant<ScrollViewer>(list);
        if (inner is null) return;

        var delta = e.GetCurrentPoint(list).Properties.MouseWheelDelta;
        var atTop = inner.VerticalOffset <= 0.5;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
        if ((delta > 0 && atTop) || (delta < 0 && atBottom))
            DetailScroll.ChangeView(null, DetailScroll.VerticalOffset - delta, null);
    }
}
