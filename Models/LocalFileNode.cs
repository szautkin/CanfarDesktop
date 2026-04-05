namespace CanfarDesktop.Models;

using Microsoft.UI.Xaml.Media;

/// <summary>
/// Represents a file or folder in the local filesystem for the TreeView.
/// </summary>
public class LocalFileNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public string Extension { get; set; } = "";
    public DateTime DateModified { get; set; }
    public long SizeBytes { get; set; }
    public List<LocalFileNode> Children { get; set; } = [];
    public bool HasUnrealizedChildren { get; set; }

    /// <summary>
    /// Segoe Fluent Icon glyph based on file type.
    /// </summary>
    public string Icon => IsFolder ? "\uE8B7" : Extension.ToLowerInvariant() switch
    {
        ".ipynb" => "\uE70B",  // Notebook
        ".py" => "\uE943",     // Code
        ".fits" or ".fit" => "\uE7B8", // Image/data
        ".csv" or ".tsv" => "\uE9F9", // Table
        ".json" => "\uE8A5",  // Document
        ".md" or ".txt" or ".rst" => "\uE8A4", // Text
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "\uEB9F", // Image
        ".pdf" => "\uEA90",   // PDF
        _ => "\uE7C3",        // Generic file
    };

    /// <summary>
    /// Foreground brush for the file-type icon. Folders use the system accent color;
    /// files use the default text color so they blend with surrounding text.
    /// </summary>
    public Brush IconForeground => IsFolder
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];

    public string FormattedSize => IsFolder ? "" : SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    public string FormattedDate => DateModified == default ? "" : DateModified.ToString("yyyy-MM-dd HH:mm");
}
