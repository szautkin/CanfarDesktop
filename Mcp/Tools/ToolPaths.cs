namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// Shared local-path validation for tools that read from / write to a caller-supplied LOCAL filesystem
/// path (figure/bundle exports, VOSpace up/downloads). One canonical place for the rooted-path rule + its
/// error wording.
/// </summary>
internal static class ToolPaths
{
    /// <summary>
    /// Require a rooted, canonicalizable local path; returns its full (absolute) form. Throws
    /// <see cref="InvalidArgument"/> (as <see cref="McpToolException"/>) for a relative or malformed path.
    /// The caller keeps its own empty-string check inline.
    /// </summary>
    public static string RequireRootedFullPath(string trimmed, string argName)
    {
        if (!System.IO.Path.IsPathRooted(trimmed))
            throw new McpToolException(new InvalidArgument($"{argName} must be a full (rooted) path"));
        try { return System.IO.Path.GetFullPath(trimmed); }
        catch { throw new McpToolException(new InvalidArgument($"invalid {argName}")); }
    }
}
