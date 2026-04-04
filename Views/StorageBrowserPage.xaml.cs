using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views;

public sealed partial class StorageBrowserPage : UserControl
{
    public StorageBrowserViewModel ViewModel { get; }
    private string _sortColumn = "name";
    private bool _sortAscending = true;

    public StorageBrowserPage(StorageBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName is nameof(ViewModel.IsLoading))
                LoadingRing.IsActive = ViewModel.IsLoading;
            else if (e.PropertyName is nameof(ViewModel.HasError))
            {
                ErrorBar.IsOpen = ViewModel.HasError;
                ErrorBar.Message = ViewModel.ErrorMessage;
            }
        });

        ViewModel.Nodes.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            ApplySort();
            EmptyState.Visibility = ViewModel.Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ItemCountText.Text = $"{ViewModel.Nodes.Count} items";
        });

        ViewModel.BreadcrumbParts.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            PathBreadcrumb.ItemsSource = ViewModel.BreadcrumbParts.ToList();
        });
    }

    public async Task LoadAsync(string username)
    {
        ViewModel.SetUsername(username);
        await ViewModel.NavigateToAsync(string.Empty);
    }

    #region Sort

    private void ApplySort()
    {
        var sorted = ViewModel.Nodes
            .OrderByDescending(n => n.IsContainer) // folders first always
            .ThenBy(n => n, Comparer<VoSpaceNode>.Create((a, b) =>
            {
                var cmp = _sortColumn switch
                {
                    "size" => (a.SizeBytes ?? 0).CompareTo(b.SizeBytes ?? 0),
                    "date" => (a.LastModified ?? DateTime.MinValue).CompareTo(b.LastModified ?? DateTime.MinValue),
                    _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                };
                return _sortAscending ? cmp : -cmp;
            }))
            .ToList();

        FileList.ItemsSource = sorted;
    }

    private void OnSortByName(object s, TappedRoutedEventArgs e) => ToggleSort("name");
    private void OnSortBySize(object s, TappedRoutedEventArgs e) => ToggleSort("size");
    private void OnSortByDate(object s, TappedRoutedEventArgs e) => ToggleSort("date");

    private void ToggleSort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
        ApplySort();
    }

    #endregion

    #region Navigation

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedNode = e.AddedItems.Count > 0 ? e.AddedItems[0] as VoSpaceNode : null;
        SelectionText.Text = ViewModel.SelectedNode is not null
            ? $"{ViewModel.SelectedNode.Name} ({ViewModel.SelectedNode.FormattedSize})"
            : "";
    }

    private async void OnFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is { IsContainer: true } folder)
        {
            var newPath = string.IsNullOrEmpty(ViewModel.CurrentPath)
                ? folder.Name : $"{ViewModel.CurrentPath}/{folder.Name}";
            await ViewModel.NavigateToAsync(newPath);
        }
    }

    private async void OnBreadcrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs e)
    {
        var parts = ViewModel.BreadcrumbParts.ToList();
        if (e.Index == 0)
            await ViewModel.NavigateToAsync(string.Empty);
        else
        {
            var path = string.Join("/", parts.Skip(1).Take(e.Index));
            await ViewModel.NavigateToAsync(path);
        }
    }

    private async void OnGoUp(object s, RoutedEventArgs e) => await ViewModel.GoUpCommand.ExecuteAsync(null);
    private async void OnRefresh(object s, RoutedEventArgs e) => await ViewModel.RefreshCommand.ExecuteAsync(null);

    #endregion

    #region Actions

    private async void OnNewFolder(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { PlaceholderText = "Folder name" };
        var dialog = new ContentDialog
        {
            Title = "New Folder",
            Content = input,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
            await ViewModel.CreateFolderCommand.ExecuteAsync(input.Text.Trim());
    }

    private async void OnUpload(object sender, RoutedEventArgs e)
    {
        try
        {
            var hWnd = nint.Zero;
            if (WindowHelper.ActiveWindows.Count > 0)
                hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            TransferRing.IsActive = true;
            TransferText.Text = $"Uploading {file.Name}...";

            using var stream = await file.OpenStreamForReadAsync();
            await ViewModel.UploadAsync(file.Name, stream);

            TransferRing.IsActive = false;
            TransferText.Text = "";
        }
        catch (Exception ex)
        {
            TransferRing.IsActive = false;
            TransferText.Text = "";
            System.Diagnostics.Debug.WriteLine($"Upload error: {ex.Message}");
        }
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is null || ViewModel.SelectedNode.IsContainer) return;

        try
        {
            var hWnd = nint.Zero;
            if (WindowHelper.ActiveWindows.Count > 0)
                hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowHelper.ActiveWindows[0]);
            if (hWnd == nint.Zero) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = ViewModel.SelectedNode.Name;
            picker.FileTypeChoices.Add("All Files", new List<string> { "." });

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            TransferRing.IsActive = true;
            TransferText.Text = $"Downloading {ViewModel.SelectedNode.Name}...";

            using var remoteStream = await ViewModel.DownloadSelectedAsync();
            if (remoteStream is not null)
            {
                using var localStream = await file.OpenStreamForWriteAsync();
                localStream.SetLength(0);
                await remoteStream.CopyToAsync(localStream);
            }

            TransferRing.IsActive = false;
            TransferText.Text = "";
        }
        catch (Exception ex)
        {
            TransferRing.IsActive = false;
            TransferText.Text = "";
            System.Diagnostics.Debug.WriteLine($"Download error: {ex.Message}");
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete",
            Content = ViewModel.SelectedNode.IsContainer
                ? $"Delete folder \"{ViewModel.SelectedNode.Name}\"? (Must be empty)"
                : $"Delete \"{ViewModel.SelectedNode.Name}\"?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteSelectedCommand.ExecuteAsync(null);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is null) return;
        var fullPath = string.IsNullOrEmpty(ViewModel.CurrentPath)
            ? ViewModel.SelectedNode.Name
            : $"{ViewModel.CurrentPath}/{ViewModel.SelectedNode.Name}";
        var package = new DataPackage();
        package.SetText($"vos://cadc.nrc.ca~arc/home/{fullPath}");
        Clipboard.SetContent(package);
    }

    #endregion

    #region Drag and Drop

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
        {
            if (item is Windows.Storage.StorageFile file)
            {
                try
                {
                    TransferRing.IsActive = true;
                    TransferText.Text = $"Uploading {file.Name}...";

                    using var stream = await file.OpenStreamForReadAsync();
                    await ViewModel.UploadAsync(file.Name, stream);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Drop upload error: {ex.Message}");
                }
            }
        }

        TransferRing.IsActive = false;
        TransferText.Text = "";
    }

    #endregion
}
