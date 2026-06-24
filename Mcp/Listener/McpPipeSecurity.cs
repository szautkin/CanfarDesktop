using System.IO.Pipes;
using System.Security.Principal;

namespace CanfarDesktop.Mcp.Listener;

/// <summary>
/// OS glue that turns <see cref="McpPipeSddl"/> into a live <see cref="PipeSecurity"/> for the current
/// interactive user. Build-verified; the descriptor string it depends on is unit-tested via McpPipeSddl.
/// </summary>
public static class McpPipeSecurity
{
    /// <summary>A <see cref="PipeSecurity"/> granting full access to the current user only.</summary>
    public static PipeSecurity ForCurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("Current Windows identity has no user SID.");
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm(McpPipeSddl.OwnerOnly(sid.Value));
        return security;
    }
}
