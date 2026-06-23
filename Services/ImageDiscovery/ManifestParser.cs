using System.Text.Json;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Services.ImageDiscovery;

public enum ManifestParseKind
{
    /// <summary>No bytes to parse.</summary>
    Empty,
    /// <summary>Truncated / unreadable / type-mismatched JSON.</summary>
    Malformed,
    /// <summary>schemaVersion is newer than this build understands — treat the image as not-yet-discovered.</summary>
    UnknownSchema,
}

/// <summary>Raised by <see cref="ManifestParser"/> on unparseable probe output. Never crashes.</summary>
public class ManifestParseException : Exception
{
    public ManifestParseKind Kind { get; }
    public int? SchemaVersion { get; }

    public ManifestParseException(ManifestParseKind kind, string message, int? schemaVersion = null) : base(message)
    {
        Kind = kind;
        SchemaVersion = schemaVersion;
    }
}

/// <summary>
/// Parses the JSON output of the in-container probe into an <see cref="ImageManifest"/>.
/// Empty manifests (image had no dpkg/rpm/apk/pip/conda) are SUCCESS, not failure.
/// </summary>
public static class ManifestParser
{
    /// <summary>Maximum schemaVersion this build understands (kept in lockstep with the probe script).</summary>
    public const int MaxSupportedSchemaVersion = 3;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ImageManifest Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
            throw new ManifestParseException(ManifestParseKind.Empty, "empty manifest");

        ImageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ImageManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException(ManifestParseKind.Malformed, ex.Message);
        }

        if (manifest is null || string.IsNullOrEmpty(manifest.ImageID))
            throw new ManifestParseException(ManifestParseKind.Malformed, "missing required field: imageID");

        if (manifest.SchemaVersion > MaxSupportedSchemaVersion)
            throw new ManifestParseException(ManifestParseKind.UnknownSchema,
                $"unknown schema version {manifest.SchemaVersion}", manifest.SchemaVersion);

        return manifest;
    }
}
