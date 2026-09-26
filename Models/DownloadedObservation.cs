using System.Text.Json.Serialization;
using CanfarDesktop.Models.Cutouts;

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
    public string DataRelease { get; set; } = string.Empty;
    // Citation handle (SCI-9-2): CADC/CAOM2 assigns no per-observation DOI/bibcode, so the originating
    // proposal (id/PI/title) is the closest citable reference we can record.
    public string ProposalId { get; set; } = string.Empty;
    public string ProposalPi { get; set; } = string.Empty;
    public string ProposalTitle { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public long? FileSize { get; set; }

    /// <summary>
    /// Which of the observation's archive files the local file is (e.g. cadc:HST/j8pu0y010_flt.fits),
    /// when that is known: stamped when a particular file is downloaded, so Download fetches the same one
    /// again and a local cutout knows what it cuts. Null for records from before, or when the app chose
    /// the file itself.
    /// </summary>
    public string? ArtifactId { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    public string? ThumbnailURL { get; set; }
    public string? PreviewURL { get; set; }

    /// <summary>Provenance stamp when the download was initiated by an MCP agent; null = user-authored.</summary>
    public AgentAttribution? AgentAttribution { get; set; }

    /// <summary>
    /// Null for the complete observation. Set, this record is a CUTOUT of it — part of one file, cut on
    /// CADC's side — and says which part. Still the observation's record (its metadata and notes are
    /// the observation's), but never to be shown as, or mistaken for, the whole.
    /// </summary>
    public CutoutSpec? Cutout { get; set; }

    [JsonIgnore]
    public bool IsCutout => Cutout is not null;

    /// <summary>
    /// Which product of the observation this is: null for the complete one, the cutout's key otherwise.
    /// With the publisher id, what makes a record the same record — so a cutout never replaces the
    /// full download, nor one cutout another.
    /// </summary>
    [JsonIgnore]
    public string? ProductKey => Cutout?.Key;

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
            DataRelease = SafeGet("datarelease"),
            ProposalId = SafeGet("proposalid"),
            ProposalPi = SafeGet("piname"),
            ProposalTitle = SafeGet("proposaltitle"),
            LocalPath = localPath ?? string.Empty,
            ThumbnailURL = dataLink?.Thumbnails.FirstOrDefault(),
            PreviewURL = dataLink?.Previews.FirstOrDefault()
        };
    }
}
