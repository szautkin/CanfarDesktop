using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        ViewModel.Breadcrumbs.CollectionChanged += (_, _) =>
            // Wait one layout pass so the ItemsRepeater has measured the new items.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                BreadcrumbScroller.ScrollToHorizontalOffset(double.MaxValue));
    }

    // ── TreeView events ──────────────────────────────────────────────────────

    /// <summary>Single click: select only (highlight item, update SelectedNode).</summary>
    private void OnItemSelected(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is LocalFileNode node)
            ViewModel.SelectedNode = node;
    }

    /// <summary>Double click: open file or navigate into folder.</summary>
    private void OnItemDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LocalFileNode node }) return;

        if (node.IsFolder)
        {
            ViewModel.SetRootPath(node.FullPath);
        }
        else
        {
            OpenFile(node.FullPath);
        }
    }

    private void OpenFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".ipynb":
                // Open in notebook tab
                FileOpenRequested?.Invoke(filePath);
                break;
            default:
                // Open with system default app
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Open file failed: {ex.Message}");
                }
                break;
        }
    }

    private void OnNodeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is not LocalFileNode node || !node.IsFolder) return;

        // Only load once — after the first expand the TreeViewNode already has children.
        if (args.Node.Children.Count > 0) return;

        var children = ViewModel.LoadChildren(node);
        foreach (var child in children)
            args.Node.Children.Add(new TreeViewNode { Content = child, HasUnrealizedChildren = child.IsFolder });
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    private void OnTreeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is null) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Delete:
                ViewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.F2:
                StartRename(ViewModel.SelectedNode);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Enter:
                if (ViewModel.SelectedNode.IsFolder)
                    ViewModel.SetRootPath(ViewModel.SelectedNode.FullPath);
                else
                    OpenFile(ViewModel.SelectedNode.FullPath);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Back:
                ViewModel.FolderUpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    private async void StartRename(LocalFileNode node)
    {
        var input = new TextBox { Text = node.Name };
        input.SelectAll();
        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = input,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(input.Text)
            && input.Text != node.Name)
        {
            try
            {
                ViewModel.RenameSelected(input.Text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Rename failed: {ex.Message}");
            }
        }
    }

    // ── Toolbar buttons ──────────────────────────────────────────────────────

    private void OnToggleSort(object s, RoutedEventArgs e)  => ViewModel.ToggleSortCommand.Execute(null);
    private void OnNewFile(object s, RoutedEventArgs e)    => ViewModel.NewFileCommand.Execute(null);
    private void OnNewFolder(object s, RoutedEventArgs e)  => ViewModel.NewFolderCommand.Execute(null);
    private void OnRefresh(object s, RoutedEventArgs e)    => ViewModel.RefreshRootCommand.Execute(null);
    private void OnFolderUp(object s, RoutedEventArgs e)   => ViewModel.FolderUpCommand.Execute(null);
    private void OnOpenFolder(object s, RoutedEventArgs e) => ViewModel.OpenFolderCommand.Execute(null);

    // ── Breadcrumb navigation ────────────────────────────────────────────────

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: BreadcrumbSegment segment })
            ViewModel.NavigateToBreadcrumbCommand.Execute(segment);
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    private void OnFilterChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        ViewModel.FilterText = sender.Text;
        ViewModel.RefreshRootCommand.Execute(null);
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void OnOpenItem(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: LocalFileNode node })
        {
            if (node.IsFolder)
                ViewModel.SetRootPath(node.FullPath);
            else
                OpenFile(node.FullPath);
        }
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

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: LocalFileNode node }) return;

        // For files, pass /select so Explorer highlights the file in its parent folder.
        var args = node.IsFolder ? $"\"{node.FullPath}\"" : $"/select,\"{node.FullPath}\"";
        try { System.Diagnostics.Process.Start("explorer.exe", args); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ShowInExplorer failed: {ex.Message}"); }
    }

    private void OnRenameItem(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: LocalFileNode node })
        {
            ViewModel.SelectedNode = node;
            StartRename(node);
        }
    }

    private async void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: LocalFileNode node }) return;

        var dialog = new ContentDialog
        {
            Title = "Delete",
            Content = node.IsFolder
                ? $"Delete folder \"{node.Name}\" and all contents?"
                : $"Delete \"{node.Name}\"?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.SelectedNode = node;
            ViewModel.DeleteSelectedCommand.Execute(null);
        }
    }

    // ── Folder picker ────────────────────────────────────────────────────────

    private async Task PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

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
