using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

public static class ImageParser
{
    /// <summary>
    /// Parse a raw image ID like "images.canfar.net/skaha/notebook-scipy:1.0" into components.
    /// </summary>
    public static ParsedImage Parse(RawImage raw)
    {
        var id = raw.Id;
        // Split: registry/project/name:version
        var parts = id.Split('/');
        string registry, project, nameWithVersion;

        if (parts.Length >= 3)
        {
            registry = parts[0];
            project = parts[1];
            nameWithVersion = string.Join("/", parts[2..]);
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
            nameWithVersion = parts[0];
        }

        var colonIdx = nameWithVersion.LastIndexOf(':');
        string name, version;
        if (colonIdx > 0)
        {
            name = nameWithVersion[..colonIdx];
            version = nameWithVersion[(colonIdx + 1)..];
        }
        else
        {
            name = nameWithVersion;
            version = "latest";
        }

        return new ParsedImage
        {
            Id = id,
            Registry = registry,
            Project = project,
            Name = name,
            Version = version,
            Label = $"{name}:{version}",
            Types = raw.Types
        };
    }

    /// <summary>
    /// Group parsed images by session type, then by project.
    /// Returns Dictionary&lt;sessionType, Dictionary&lt;project, List&lt;ParsedImage&gt;&gt;&gt;
    /// </summary>
    public static Dictionary<string, Dictionary<string, List<ParsedImage>>> GroupByTypeAndProject(
        IEnumerable<RawImage> rawImages)
    {
        var result = new Dictionary<string, Dictionary<string, List<ParsedImage>>>();

        foreach (var raw in rawImages)
        {
            var parsed = Parse(raw);
            foreach (var type in parsed.Types)
            {
                if (!result.ContainsKey(type))
                    result[type] = new Dictionary<string, List<ParsedImage>>();

                if (!result[type].ContainsKey(parsed.Project))
                    result[type][parsed.Project] = new List<ParsedImage>();

                result[type][parsed.Project].Add(parsed);
            }
        }

        // Sort versions descending within each project
        foreach (var type in result.Values)
            foreach (var project in type.Keys)
                type[project] = type[project].OrderByDescending(i => i.Version).ToList();

        return result;
    }
}
