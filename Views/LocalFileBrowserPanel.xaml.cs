using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop.Views;

public sealed partial class LocalFileBrowserPanel : UserControl
{
    public LocalFileBrowserViewModel ViewModel { get; }

    public event Action<string>? FileOpenRequested;

    public LocalFileBrowserPanel(LocalFileBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.FileOpenRequested += path => FileOpenRequested?.Invoke(path);
        ViewModel.PickFolderRequested += async () => await PickFolderAsync();

        FileTree.ItemsSource = ViewModel.RootNodes;
    }

    // ── TreeView events ──────────────────────────────────────────────────────

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is LocalFileNode node)
        {
            ViewModel.SelectedNode = node;
            if (!node.IsFolder)
                FileOpenRequested?.Invoke(node.FullPath);
        }
    }

    private void OnNodeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is LocalFileNode node && node.HasUnrealizedChildren)
            ViewModel.LoadChildren(node);
    }

    // ── Toolbar buttons ──────────────────────────────────────────────────────

    private void OnNewFile(object s, RoutedEventArgs e)   => ViewModel.NewFileCommand.Execute(null);
    private void OnNewFolder(object s, RoutedEventArgs e) => ViewModel.NewFolderCommand.Execute(null);
    private void OnRefresh(object s, RoutedEventArgs e)   => ViewModel.RefreshRootCommand.Execute(null);
    private void OnFolderUp(object s, RoutedEventArgs e)  => ViewModel.FolderUpCommand.Execute(null);
    private void OnOpenFolder(object s, RoutedEventArgs e) => ViewModel.OpenFolderCommand.Execute(null);

    // ── Breadcrumb navigation ─────────────────────────────────────────────

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: BreadcrumbSegment segment })
            ViewModel.NavigateToBreadcrumbCommand.Execute(segment);
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FilterText = FilterBox.Text;
        // TODO: apply filter to tree view items
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void OnOpenItem(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: LocalFileNode node })
            FileOpenRequested?.Invoke(node.FullPath);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: LocalFileNode node })
        {
            var package = new DataPackage();
            package.SetText(node.FullPath);
            Clipboard.SetContent(package);
        }
    }

    private void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: LocalFileNode node })
        {
            ViewModel.SelectedNode = node;
            ViewModel.DeleteSelectedCommand.Execute(null);
        }
    }

    // ── Folder picker (View responsibility — needs window handle + XamlRoot) ─

    private async Task PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        // WinUI 3: picker must be initialized with the HWND (same pattern as StorageBrowserPage).
        var hwnd = ActiveWindows.Count > 0
            ? WindowNative.GetWindowHandle(ActiveWindows[0])
            : IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            ViewModel.SetRootPath(folder.Path);
    }
}
