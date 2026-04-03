namespace CanfarDesktop.Models;

public class DataLinkResult
{
    public List<string> Thumbnails { get; set; } = [];
    public List<string> Previews { get; set; } = [];
    public string? DownloadUrl { get; set; }
}
