namespace CanfarDesktop.Models;

/// <summary>
/// A downloaded observation file tracked by the Research module.
/// Persisted to JSON on disk.
/// </summary>
public class DownloadedObservation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PublisherID { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string ObservationID { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string RA { get; set; } = string.Empty;
    public string Dec { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string CalLevel { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    public string? ThumbnailURL { get; set; }
    public string? PreviewURL { get; set; }

    public bool FileExists => !string.IsNullOrWhiteSpace(LocalPath) && File.Exists(LocalPath);
    public string Filename => string.IsNullOrEmpty(LocalPath) ? "" : Path.GetFileName(LocalPath);

    public string FormattedSize => FileSize switch
    {
        null => "",
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{FileSize / (1024.0 * 1024):F1} MB",
        _ => $"{FileSize / (1024.0 * 1024 * 1024):F2} GB"
    };

    /// <summary>
    /// Create from a search result row + DataLink info.
    /// </summary>
    public static DownloadedObservation FromSearchResult(SearchResultRow row, string? localPath,
        DataLinkResult? dataLink, Func<string, string> getHeader)
    {
        string SafeGet(string key)
        {
            try { return row.Get(getHeader(key)); }
            catch { return string.Empty; }
        }

        return new DownloadedObservation
        {
            PublisherID = SafeGet("publisherid"),
            Collection = SafeGet("collection"),
            ObservationID = SafeGet("observationid"),
            TargetName = SafeGet("targetname"),
            Instrument = SafeGet("instrument"),
            Filter = SafeGet("filter"),
            RA = SafeGet("ra(j20000)"),
            Dec = SafeGet("dec(j20000)"),
            StartDate = SafeGet("startdate"),
            CalLevel = SafeGet("callev"),
            LocalPath = localPath ?? string.Empty,
            ThumbnailURL = dataLink?.Thumbnails.FirstOrDefault(),
            PreviewURL = dataLink?.Previews.FirstOrDefault()
        };
    }
}
