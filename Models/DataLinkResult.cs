using CanfarDesktop.Helpers;

namespace CanfarDesktop.Models;

public class DataLinkResult
{
    public List<string> Thumbnails { get; set; } = [];
    public List<string> Previews { get; set; } = [];
    public string? DownloadUrl { get; set; }
    /// <summary>All downloadable files from DataLink #this semantic.</summary>
    public List<DataLinkFile> DirectFiles { get; set; } = [];

    /// <summary>
    /// The <c>error_message</c> text of every faulted row. A fault beside real rows is not fatal — the
    /// service is entitled to refuse part of a request — but a response that is ENTIRELY faults used to
    /// parse to an empty file list, which reads as "resolved, nothing here" rather than
    /// "UsageFault: invalid ID". <see cref="IsEntirelyFaults"/> is the difference.
    /// </summary>
    public List<string> Faults { get; set; } = [];

    /// <summary>True when the service answered with faults and nothing else.</summary>
    public bool IsEntirelyFaults =>
        Faults.Count > 0 && DirectFiles.Count == 0 && Previews.Count == 0 && Thumbnails.Count == 0;

    /// <summary>
    /// The direct file to download when the caller did not pick one. Not simply the first: a `#this`
    /// row marks a science product and most collections publish exactly one, but JWST publishes
    /// several and the four-kilobyte `_asn.json` index is usually ahead of the 46 MB `_i2d.fits`.
    /// </summary>
    public string? DirectFileUrl => DataLinkArtifactSelector.PreferScienceFile(DirectFiles)?.Url;
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
