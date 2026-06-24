namespace CanfarDesktop.Mcp.Listener;

/// <summary>
/// Pure builder for the named-pipe SDDL. macOS guards its MCP unix socket with 0600 file permissions;
/// the Windows-equivalent hardening is a pipe DACL granting full access to the owning user only and
/// nobody else. A local pipe server with no NetworkService/Everyone ACE is unreachable remotely, so this
/// limits the pipe to the same interactive user. No OS types here so the descriptor format is testable.
/// </summary>
public static class McpPipeSddl
{
    /// <summary>Protected DACL granting Full Access to <paramref name="ownerSid"/> only (no inheritance).</summary>
    public static string OwnerOnly(string ownerSid) => $"D:P(A;;FA;;;{ownerSid})";
}
