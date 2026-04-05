namespace CanfarDesktop.Services;

using CanfarDesktop.Models;

/// <summary>
/// Local filesystem operations for the file browser panel.
/// </summary>
public interface ILocalFileService
{
    List<LocalFileNode> GetChildren(string directoryPath, bool showHidden = false);
    void CreateFolder(string parentPath, string name);
    void CreateFile(string parentPath, string name);
    void Delete(string path);
    void Rename(string oldPath, string newName);
    void Move(string sourcePath, string destFolderPath);
    void CopyToClipboard(string path);
    event Action<string>? DirectoryChanged;
    void WatchDirectory(string path);
    void UnwatchDirectory(string path);
}
