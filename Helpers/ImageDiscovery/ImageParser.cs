using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers.ImageDiscovery;

/// <summary>
/// Parses a registry-qualified image id into its parts and groups parsed images by project for the
/// matching-images pane. 1-to-1 with macOS <c>ImageParser</c>.
/// </summary>
public static class ImageParser
{
    /// <summary>
    /// Split <c>registry/project/name:version</c>. 3+ segments → registry/project/rest;
    /// 2 → project/name (no registry); 1 → bare name. Version defaults to <c>latest</c> when no
    /// <c>:</c> is present. The last <c>:</c> separates name and version (mirrors macOS).
    /// </summary>
    public static ParsedImage Parse(RawImage raw)
    {
        var fullId = raw.Id ?? string.Empty;
        var parts = fullId.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string registry, project, nameWithVersion;
        if (parts.Length >= 3)
        {
            registry = parts[0];
            project = parts[1];
            nameWithVersion = string.Join("/", parts.Skip(2));
        }
        else if (parts.Length == 2)
        {
            registry = string.Empty;
            project = parts[0];
            nameWithVersion = parts[1];
        }
        else
        {
            registry = string.Empty;
            project = string.Empty;
            nameWithVersion = parts.Length > 0 ? parts[0] : fullId;
        }

        string name, version;
        var lastColon = nameWithVersion.LastIndexOf(':');
        if (lastColon >= 0)
        {
            name = nameWithVersion[..lastColon];
            version = nameWithVersion[(lastColon + 1)..];
        }
        else
        {
            name = nameWithVersion;
            version = "latest";
        }

        return new ParsedImage
        {
            Id = fullId,
            Registry = registry,
            Project = project,
            Name = name,
            Version = version,
            Label = $"{name}:{version}",
            Types = raw.Types ?? Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Group parsed images into project sections for the right pane: images within a project sorted
    /// by label ascending, groups sorted by project ascending (mirrors macOS filteredImagesByProject).
    /// </summary>
    public static IReadOnlyList<(string Project, IReadOnlyList<ParsedImage> Images)> GroupByProject(
        IEnumerable<ParsedImage> images)
        => images
            .GroupBy(i => i.Project)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key,
                (IReadOnlyList<ParsedImage>)g.OrderBy(i => i.Label, StringComparer.Ordinal).ToList()))
            .ToList();
}
