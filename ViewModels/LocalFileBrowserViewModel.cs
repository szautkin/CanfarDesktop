namespace CanfarDesktop.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using Microsoft.UI.Dispatching;

/// <summary>
/// ViewModel for the local file browser side panel.
/// </summary>
public partial class LocalFileBrowserViewModel : ObservableObject
{
    private readonly ILocalFileService _fileService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty] private string _rootPath = "";
    [ObservableProperty] private LocalFileNode? _selectedNode;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _sortByDate;

    public ObservableCollection<LocalFileNode> RootNodes { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    public event Action<string>? FileOpenRequested;
    public event Action? PickFolderRequested;

    public LocalFileBrowserViewModel(ILocalFileService fileService)
    {
        _fileService = fileService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _fileService.DirectoryChanged += OnDirectoryChanged;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    public void SetRootPath(string path)
    {
        if (!Directory.Exists(path)) return;
        RootPath = path;
        RebuildBreadcrumbs();
        RefreshRoot();
    }

    [RelayCommand]
    public void RefreshRoot()
    {
        RootNodes.Clear();
        if (string.IsNullOrEmpty(RootPath) || !Directory.Exists(RootPath)) return;

        var children = _fileService.GetChildren(RootPath, ShowHidden);
        var filtered = ApplyFilter(children);
        var sorted = ApplySort(filtered);

        foreach (var child in sorted)
            RootNodes.Add(child);

        _fileService.WatchDirectory(RootPath);
    }

    public List<LocalFileNode> LoadChildren(LocalFileNode parent)
    {
        if (!parent.IsFolder) return [];
        var children = _fileService.GetChildren(parent.FullPath, ShowHidden);
        var sorted = ApplySort(children);
        parent.Children = sorted;
        parent.HasUnrealizedChildren = false;
        _fileService.WatchDirectory(parent.FullPath);
        return sorted;
    }

    // ── File operations ──────────────────────────────────────────────────────

    [RelayCommand]
    public void OpenSelected()
    {
        if (SelectedNode is null) return;
        if (SelectedNode.IsFolder)
            SetRootPath(SelectedNode.FullPath);
        else
            FileOpenRequested?.Invoke(SelectedNode.FullPath);
    }

    [RelayCommand]
    public void NewFolder()
    {
        var parent = SelectedNode?.IsFolder == true ? SelectedNode.FullPath : RootPath;
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;

        var name = "New Folder";
        var counter = 1;
        while (Directory.Exists(Path.Combine(parent, name)))
            name = $"New Folder ({counter++})";

        try { _fileService.CreateFolder(parent, name); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CreateFolder failed: {ex.Message}"); }
    }

    [RelayCommand]
    public void NewFile()
    {
        var parent = SelectedNode?.IsFolder == true ? SelectedNode.FullPath : RootPath;
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;

        var name = "untitled.ipynb";
        var counter = 1;
        while (File.Exists(Path.Combine(parent, name)))
            name = $"untitled ({counter++}).ipynb";

        try { _fileService.CreateFile(parent, name); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CreateFile failed: {ex.Message}"); }
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedNode is null) return;
        try { _fileService.Delete(SelectedNode.FullPath); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Delete failed: {ex.Message}"); }
    }

    public void RenameSelected(string newName)
    {
        if (SelectedNode is null || string.IsNullOrWhiteSpace(newName)) return;
        _fileService.Rename(SelectedNode.FullPath, newName);
    }

    [RelayCommand]
    public void CopyPath()
    {
        // Clipboard handled in View (needs UI thread)
    }

    // ── Breadcrumbs ──────────────────────────────────────────────────────────

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(RootPath)) return;

        var parts = RootPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var accumulated = "";
        for (var i = 0; i < parts.Length; i++)
        {
            accumulated = i == 0
                ? (OperatingSystem.IsWindows() ? parts[0] + "\\" : "/" + parts[0])
                : Path.Combine(accumulated, parts[i]);
            Breadcrumbs.Add(new BreadcrumbSegment(parts[i], accumulated));
        }
    }

    [RelayCommand]
    public void NavigateToBreadcrumb(BreadcrumbSegment? segment)
    {
        if (segment is not null && Directory.Exists(segment.FullPath))
            SetRootPath(segment.FullPath);
    }

    // ── Folder Up ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    public void FolderUp()
    {
        var parent = Path.GetDirectoryName(RootPath.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            SetRootPath(parent);
    }

    private bool CanGoUp() =>
        !string.IsNullOrEmpty(RootPath) &&
        Path.GetDirectoryName(RootPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 };

    partial void OnRootPathChanging(string value) => FolderUpCommand.NotifyCanExecuteChanged();

    // ── Open Folder ──────────────────────────────────────────────────────────

    [RelayCommand]
    public void OpenFolder() => PickFolderRequested?.Invoke();

    // ── Sort ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    public void ToggleSort()
    {
        SortByDate = !SortByDate;
        RefreshRoot();
    }

    private List<LocalFileNode> ApplySort(List<LocalFileNode> nodes)
    {
        // Folders always first
        var folders = nodes.Where(n => n.IsFolder);
        var files = nodes.Where(n => !n.IsFolder);

        if (SortByDate)
        {
            folders = folders.OrderByDescending(n => n.DateModified);
            files = files.OrderByDescending(n => n.DateModified);
        }
        else
        {
            folders = folders.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
            files = files.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
        }

        return folders.Concat(files).ToList();
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    private List<LocalFileNode> ApplyFilter(List<LocalFileNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(FilterText)) return nodes;

        var filter = FilterText.Trim();
        return nodes.Where(n =>
            n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    // ── Change notifications ─────────────────────────────────────────────────

    partial void OnShowHiddenChanged(bool value) => RefreshRoot();

    partial void OnFilterTextChanged(string value)
    {
        // Debounce handled by TextChanged event delay in View
    }

    private void OnDirectoryChanged(string path)
    {
        // FileSystemWatcher fires on thread-pool — marshal to UI thread
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (path == RootPath)
                RefreshRoot();
        });
    }
}
