namespace CanfarDesktop.Models;

public enum VoSpaceNodeType
{
    Container,
    DataNode,
    LinkNode
}

/// <summary>
/// Represents a file or folder in the VOSpace/ARC storage system.
/// </summary>
public class VoSpaceNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public VoSpaceNodeType Type { get; set; }
    public long? SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsPublic { get; set; }

    public bool IsContainer => Type == VoSpaceNodeType.Container;

    public string FormattedSize => SizeBytes switch
    {
        null => "",
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string FormattedDate => LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";

    public string FileExtension => System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public string Icon => Type switch
    {
        VoSpaceNodeType.Container => "\uE8B7",  // folder
        VoSpaceNodeType.LinkNode => "\uE71B",    // link
        _ => FileExtension switch
        {
            ".fits" or ".fz" => "\uE9D9",           // science
            ".csv" or ".tsv" or ".vot" => "\uE80A",  // table
            ".py" or ".sh" or ".bash" => "\uE943",   // code
            ".jpg" or ".jpeg" or ".png" or ".gif" => "\uEB9F", // image
            ".pdf" => "\uEA90",                      // document
            ".tar" or ".gz" or ".zip" => "\uF012",   // archive
            _ => "\uE8A5"                            // generic file
        }
    };
}
