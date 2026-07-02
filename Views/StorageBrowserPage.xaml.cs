using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views;

public sealed partial class StorageBrowserPage : UserControl
{
    public StorageBrowserViewModel ViewModel { get; }
    public event Action<string>? OpenInFitsViewerRequested;
    public event Action<string>? OpenInCubeViewerRequested;
    private string _sortColumn = "name";
    private bool _sortAscending = true;

    // The ListView binds to this once; loads/sorts mutate it in place so the
    // control never has its ItemsSource reassigned (which resets scroll and
    // drops selection).
    private readonly BulkObservableCollection<VoSpaceNode> _view = [];
    private string? _lastLoadedPath;

    public StorageBrowserPage(StorageBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        FileList.ItemsSource = _view;
        PathBreadcrumb.ItemsSource = ViewModel.BreadcrumbParts;

        ViewModel.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName is nameof(ViewModel.IsLoading))
            {
                LoadingRing.IsActive = ViewModel.IsLoading;
                UpdateEmptyState();
            }
            else if (e.PropertyName is nameof(ViewModel.HasError) or nameof(ViewModel.ErrorMessage))
            {
                ErrorBar.IsOpen = ViewModel.HasError;
                ErrorBar.Message = ViewModel.ErrorMessage;
            }
        });

        ViewModel.Nodes.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            // Same path = refresh (after upload/delete/new-folder): keep the user's
            // selection and scroll position. New path = navigation: start at the top.
            var isRefresh = _lastLoadedPath == ViewModel.CurrentPath;
            _lastLoadedPath = ViewModel.CurrentPath;

            var selectedName = (FileList.SelectedItem as VoSpaceNode)?.Name;
            var scroll = isRefresh ? VisualTree.FindDescendant<ScrollViewer>(FileList)?.VerticalOffset : null;

            _view.ReplaceAll(SortNodes(ViewModel.Nodes));
            UpdateEmptyState();
            ItemCountText.Text = $"{ViewModel.Nodes.Count} items";

            if (isRefresh)
            {
                if (selectedName is not null)
                    FileList.SelectedItem = _view.FirstOrDefault(n => n.Name == selectedName);
                if (scroll is > 0)
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                        VisualTree.FindDescendant<ScrollViewer>(FileList)?.ChangeView(null, scroll, null, disableAnimation: true));
            }
            else
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    VisualTree.FindDescendant<ScrollViewer>(FileList)?.ChangeView(null, 0, null, disableAnimation: true));
            }
        });
    }

    private void UpdateEmptyState()
    {
        // Gate on IsLoading so navigating into a folder doesn't flash "empty"
        // while the listing is still in flight.
        EmptyState.Visibility = !ViewModel.IsLoading && ViewModel.Nodes.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public async Task LoadAsync(string username)
    {
        ViewModel.SetUsername(username);
        await ViewModel.NavigateToAsync(string.Empty);
    }

    #region Sort

    private IEnumerable<VoSpaceNode> SortNodes(IEnumerable<VoSpaceNode> nodes) => nodes
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
        }));

    private void ApplySort()
    {
        // In-place reorder (Move) keeps the ListView's selection and containers alive.
        var sorted = SortNodes(_view).ToList();
        for (var target = 0; target < sorted.Count; target++)
        {
            var current = _view.IndexOf(sorted[target]);
            if (current != target) _view.Move(current, target);
        }
    }

    private void OnSortByName(object s, RoutedEventArgs e) => ToggleSort("name");
    private void OnSortBySize(object s, RoutedEventArgs e) => ToggleSort("size");
    private void OnSortByDate(object s, RoutedEventArgs e) => ToggleSort("date");

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

    // Guards the toolbar's async void handlers: a second click while a dialog or
    // transfer is in flight would otherwise open a second ContentDialog (throws)
    // or start an overlapping operation.
    private bool _actionBusy;

    private void ReportError(string message)
    {
        ViewModel.ErrorMessage = message;
        ViewModel.HasError = true;
    }

    private async void OnNewFolder(object sender, RoutedEventArgs e)
    {
        if (_actionBusy) return;
        _actionBusy = true;
        try
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
        finally
        {
            _actionBusy = false;
        }
    }

    private async void OnUpload(object sender, RoutedEventArgs e)
    {
        if (_actionBusy) return;
        _actionBusy = true;
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
            ReportError($"Upload failed: {ex.Message}");
        }
        finally
        {
            _actionBusy = false;
        }
    }

    private async void OnOpenInFitsViewer(object sender, RoutedEventArgs e)
    {
        var path = await DownloadSelectedFitsToTempAsync();
        if (path is not null) OpenInFitsViewerRequested?.Invoke(path);
    }

    private async void OnOpenInCubeViewer(object sender, RoutedEventArgs e)
    {
        var path = await DownloadSelectedFitsToTempAsync();
        if (path is not null) OpenInCubeViewerRequested?.Invoke(path);
    }

    /// <summary>Download the selected FITS file to the temp dir; returns its local path or null.</summary>
    private async Task<string?> DownloadSelectedFitsToTempAsync()
    {
        if (ViewModel.SelectedNode is null || ViewModel.SelectedNode.IsContainer) return null;
        var name = ViewModel.SelectedNode.Name;
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is not (".fits" or ".fit" or ".fts"))
        {
            ViewModel.ErrorMessage = "Only FITS files can be opened in the viewer";
            ViewModel.HasError = true;
            return null;
        }

        try
        {
            TransferRing.IsActive = true;
            TransferText.Text = $"Downloading {name}...";

            var remotePath = string.IsNullOrEmpty(ViewModel.CurrentPath)
                ? name : $"{ViewModel.CurrentPath}/{name}";
            var tempDir = Path.Combine(Path.GetTempPath(), "Verbinal");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, name);

            using (var stream = await ViewModel.DownloadStreamAsync(remotePath))
            using (var fileStream = new FileStream(tempPath, FileMode.Create))
                await stream.CopyToAsync(fileStream);

            TransferRing.IsActive = false;
            TransferText.Text = "";
            return tempPath;
        }
        catch (Exception ex)
        {
            TransferRing.IsActive = false;
            TransferText.Text = "";
            ViewModel.ErrorMessage = $"Failed to open: {ex.Message}";
            ViewModel.HasError = true;
            return null;
        }
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is null || ViewModel.SelectedNode.IsContainer) return;
        if (_actionBusy) return;
        _actionBusy = true;
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
            ReportError($"Download failed: {ex.Message}");
        }
        finally
        {
            _actionBusy = false;
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is null) return;
        if (_actionBusy) return;
        _actionBusy = true;
        try
        {
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
        finally
        {
            _actionBusy = false;
        }
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
                    ReportError($"Upload of {file.Name} failed: {ex.Message}");
                }
            }
        }

        TransferRing.IsActive = false;
        TransferText.Text = "";
    }

    #endregion
}
