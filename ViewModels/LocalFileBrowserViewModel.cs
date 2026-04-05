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

    public ObservableCollection<LocalFileNode> RootNodes { get; } = [];

    /// <summary>Raised when a file should be opened (e.g., .ipynb in notebook tab).</summary>
    public event Action<string>? FileOpenRequested;

    public LocalFileBrowserViewModel(ILocalFileService fileService)
    {
        _fileService = fileService;
        // Capture the UI thread dispatcher at construction time (always called from UI thread via DI).
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _fileService.DirectoryChanged += OnDirectoryChanged;
    }

    public void SetRootPath(string path)
    {
        RootPath = path;
        RefreshRoot();
    }

    [RelayCommand]
    public void RefreshRoot()
    {
        RootNodes.Clear();
        if (string.IsNullOrEmpty(RootPath) || !Directory.Exists(RootPath)) return;

        var children = _fileService.GetChildren(RootPath, ShowHidden);
        foreach (var child in children)
            RootNodes.Add(child);

        _fileService.WatchDirectory(RootPath);
    }

    public List<LocalFileNode> LoadChildren(LocalFileNode parent)
    {
        if (!parent.IsFolder) return [];
        var children = _fileService.GetChildren(parent.FullPath, ShowHidden);
        parent.Children = children;
        parent.HasUnrealizedChildren = false;
        _fileService.WatchDirectory(parent.FullPath);
        return children;
    }

    [RelayCommand]
    public void OpenSelected()
    {
        if (SelectedNode is null) return;
        FileOpenRequested?.Invoke(SelectedNode.FullPath);
    }

    [RelayCommand]
    public void NewFolder()
    {
        var parent = SelectedNode?.IsFolder == true ? SelectedNode.FullPath : RootPath;
        if (string.IsNullOrEmpty(parent)) return;

        var name = "New Folder";
        var counter = 1;
        while (Directory.Exists(Path.Combine(parent, name)))
            name = $"New Folder ({counter++})";

        _fileService.CreateFolder(parent, name);
    }

    [RelayCommand]
    public void NewFile()
    {
        var parent = SelectedNode?.IsFolder == true ? SelectedNode.FullPath : RootPath;
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;

        // Deduplicate: untitled.ipynb → untitled (1).ipynb → untitled (2).ipynb
        var name = "untitled.ipynb";
        var counter = 1;
        while (File.Exists(Path.Combine(parent, name)))
            name = $"untitled ({counter++}).ipynb";

        _fileService.CreateFile(parent, name);
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedNode is null) return;
        _fileService.Delete(SelectedNode.FullPath);
    }

    [RelayCommand]
    public void CopyPath()
    {
        if (SelectedNode is null) return;
        _fileService.CopyToClipboard(SelectedNode.FullPath);
    }

    private void OnDirectoryChanged(string path)
    {
        // FileSystemWatcher fires on a thread-pool thread — marshal to UI thread before
        // touching RootNodes (ObservableCollection raises CollectionChanged on its caller's thread).
        if (path == RootPath)
            _dispatcherQueue.TryEnqueue(RefreshRoot);
    }

    partial void OnShowHiddenChanged(bool value) => RefreshRoot();
    partial void OnRootPathChanged(string value) => RebuildBreadcrumbs();

    // ── Breadcrumbs ──────────────────────────────────────────────────────────

    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

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
    public void NavigateToBreadcrumb(BreadcrumbSegment segment)
    {
        if (Directory.Exists(segment.FullPath))
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

    /// <summary>Raised when the ViewModel wants the View to show a folder picker.</summary>
    public event Action? PickFolderRequested;

    [RelayCommand]
    public void OpenFolder() => PickFolderRequested?.Invoke();
}
