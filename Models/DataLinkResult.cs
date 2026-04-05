namespace CanfarDesktop.Models;

public class DataLinkResult
{
    public List<string> Thumbnails { get; set; } = [];
    public List<string> Previews { get; set; } = [];
    public string? DownloadUrl { get; set; }
    /// <summary>All downloadable files from DataLink #this semantic.</summary>
    public List<DataLinkFile> DirectFiles { get; set; } = [];

    /// <summary>First direct file URL for convenience.</summary>
    public string? DirectFileUrl => DirectFiles.Count > 0 ? DirectFiles[0].Url : null;
}

/// <summary>
/// One downloadable artifact from DataLink #this semantic.
/// </summary>
public class DataLinkFile
{
    public required string Url { get; init; }
    public string ContentType { get; init; } = "";
    public string Description { get; init; } = "";
    public string Filename => System.Uri.TryCreate(Url, System.UriKind.Absolute, out var uri)
        ? System.IO.Path.GetFileName(uri.LocalPath)
        : "file";
}
