using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class StorageBrowserViewModel : ObservableObject
{
    private readonly IStorageService _storageService;
    private string _username = string.Empty;

    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private VoSpaceNode? _selectedNode;

    public BulkObservableCollection<VoSpaceNode> Nodes { get; } = [];
    public BulkObservableCollection<string> BreadcrumbParts { get; } = [];

    public StorageBrowserViewModel(IStorageService storageService)
    {
        _storageService = storageService;
    }

    public void SetUsername(string username)
    {
        _username = username;
    }

    [RelayCommand]
    public async Task NavigateToAsync(string path)
    {
        CurrentPath = path;
        UpdateBreadcrumbs();
        await LoadCurrentFolderAsync();
    }

    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var lastSlash = CurrentPath.LastIndexOf('/');
        var parentPath = lastSlash > 0 ? CurrentPath[..lastSlash] : string.Empty;
        await NavigateToAsync(parentPath);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadCurrentFolderAsync();
    }

    [RelayCommand]
    public async Task CreateFolderAsync(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return;

        using var task = TaskRegistry.Begin(TaskKind.Storage, $"New folder {folderName}");
        try
        {
            var basePath = string.IsNullOrEmpty(CurrentPath) ? _username : $"{_username}/{CurrentPath}";
            await _storageService.CreateFolderAsync(basePath, folderName);
            await RefreshAsync();
            task.Succeed();
        }
        catch (Exception ex)
        {
            task.Fail(ex.Message);
            ErrorMessage = $"Create folder failed: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (SelectedNode is null) return;

        using var task = TaskRegistry.Begin(TaskKind.Storage, $"Delete {SelectedNode.Name}");
        try
        {
            var nodePath = string.IsNullOrEmpty(CurrentPath)
                ? $"{_username}/{SelectedNode.Name}"
                : $"{_username}/{CurrentPath}/{SelectedNode.Name}";
            await _storageService.DeleteNodeAsync(nodePath);
            SelectedNode = null;
            await RefreshAsync();
            task.Succeed();
        }
        catch (Exception ex)
        {
            task.Fail(ex.Message);
            ErrorMessage = $"Delete failed: {ex.Message}";
            HasError = true;
        }
    }

    public async Task<Stream?> DownloadSelectedAsync()
    {
        if (SelectedNode is null || SelectedNode.IsContainer) return null;

        using var task = TaskRegistry.Begin(TaskKind.Storage, $"Download {SelectedNode.Name}");
        try
        {
            var filePath = string.IsNullOrEmpty(CurrentPath)
                ? $"{_username}/{SelectedNode.Name}"
                : $"{_username}/{CurrentPath}/{SelectedNode.Name}";
            var stream = await _storageService.DownloadFileAsync(filePath);
            task.Succeed();
            return stream;
        }
        catch (Exception ex)
        {
            task.Fail(ex.Message);
            ErrorMessage = $"Download failed: {ex.Message}";
            HasError = true;
            return null;
        }
    }

    public async Task UploadAsync(string fileName, Stream content)
    {
        using var task = TaskRegistry.Begin(TaskKind.Storage, $"Upload {fileName}");
        try
        {
            var remotePath = string.IsNullOrEmpty(CurrentPath)
                ? $"{_username}/{fileName}"
                : $"{_username}/{CurrentPath}/{fileName}";
            await _storageService.UploadFileAsync(remotePath, content);
            await RefreshAsync();
            task.Succeed();
        }
        catch (Exception ex)
        {
            task.Fail(ex.Message);
            ErrorMessage = $"Upload failed: {ex.Message}";
            HasError = true;
        }
    }

    public async Task<Stream> DownloadStreamAsync(string remotePath)
    {
        var fullPath = $"{_username}/{remotePath}";
        return await _storageService.DownloadFileAsync(fullPath);
    }

    private async Task LoadCurrentFolderAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            var fullPath = string.IsNullOrEmpty(CurrentPath) ? _username : $"{_username}/{CurrentPath}";
            var nodes = await _storageService.ListNodesAsync(fullPath, limit: 500);

            // Folders first, then files, alphabetically within each group.
            // ReplaceAll raises a single Reset so the bound list rebuilds once.
            Nodes.ReplaceAll(nodes.OrderByDescending(n => n.IsContainer).ThenBy(n => n.Name));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateBreadcrumbs()
    {
        var parts = new List<string> { "Home" };
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            foreach (var part in CurrentPath.Split('/'))
                if (!string.IsNullOrWhiteSpace(part))
                    parts.Add(part);
        }
        BreadcrumbParts.ReplaceAll(parts);
    }
}
