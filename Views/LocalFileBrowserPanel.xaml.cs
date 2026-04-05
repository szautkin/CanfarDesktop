using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;

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

        // Watch for directory changes and refresh on UI thread
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.RootPath))
                DispatcherQueue.TryEnqueue(() => FileTree.ItemsSource = ViewModel.RootNodes);
        };

        FileTree.ItemsSource = ViewModel.RootNodes;
    }

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
        {
            var children = ViewModel.LoadChildren(node);
            // TreeView needs the children in the node's Children list
            // which we already set in LoadChildren
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FilterText = FilterBox.Text;
        // TODO: apply filter to tree view items
    }

    private void OnNewFile(object s, RoutedEventArgs e) => ViewModel.NewFileCommand.Execute(null);
    private void OnNewFolder(object s, RoutedEventArgs e) => ViewModel.NewFolderCommand.Execute(null);
    private void OnRefresh(object s, RoutedEventArgs e) => ViewModel.RefreshRootCommand.Execute(null);

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
}
