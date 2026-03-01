using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

// Raw image from Skaha API
public class RawImage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("types")]
    public string[] Types { get; set; } = [];
}

// Parsed/normalized image info
public class ParsedImage
{
    public string Id { get; set; } = string.Empty;        // full image id
    public string Registry { get; set; } = string.Empty;  // e.g. "images.canfar.net"
    public string Project { get; set; } = string.Empty;   // e.g. "skaha"
    public string Name { get; set; } = string.Empty;      // e.g. "notebook-scipy"
    public string Version { get; set; } = string.Empty;   // e.g. "1.0"
    public string Label { get; set; } = string.Empty;     // display label: "name:version"
    public string[] Types { get; set; } = [];              // session types this image supports
}
