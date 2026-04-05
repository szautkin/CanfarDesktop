namespace CanfarDesktop.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

/// <summary>
/// ViewModel for the local file browser side panel.
/// </summary>
public partial class LocalFileBrowserViewModel : ObservableObject
{
    private readonly ILocalFileService _fileService;

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
        if (string.IsNullOrEmpty(parent)) return;

        _fileService.CreateFile(parent, "untitled.ipynb");
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
        // Refresh will be dispatched to UI thread by the View
        if (path == RootPath)
            RefreshRoot();
    }

    partial void OnShowHiddenChanged(bool value) => RefreshRoot();
}
