namespace CanfarDesktop.Models;

public class DataLinkResult
{
    public List<string> Thumbnails { get; set; } = [];
    public List<string> Previews { get; set; } = [];
    public string? DownloadUrl { get; set; }
    /// <summary>Direct file URL from DataLink #this semantic (FITS, no tar wrapping).</summary>
    public string? DirectFileUrl { get; set; }
}
