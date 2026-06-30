namespace CanfarDesktop.Mcp;

/// <summary>Centralized MCP identifiers — pipe naming, sidecar location, server identity.</summary>
public static class McpConstants
{
    public const string ServerDisplayName = "Verbinal";

    /// <summary>Pipe names are namespaced + unguessable per launch so a stale pipe is never reused.</summary>
    public const string PipeNamePrefix = "verbinal-canfar-mcp-";

    public const string SidecarFolderName = "Verbinal";
    public const string SidecarFileName = "mcp.pipe-name";

    /// <summary>Self-reported name of the app's own loopback MCP probe (the connection wizard's Verify
    /// self-test). The approval gate always permits it, regardless of the require-approval policy.</summary>
    public const string SelfTestClientName = "verbinal-selftest";

    public static string NewPipeName(Guid launchId) => PipeNamePrefix + launchId.ToString("N");
}
