using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;

namespace CanfarDesktop.Mcp;

/// <summary>
/// The deterministic, per-user MCP pipe name shared by the packaged app (server) and the unpackaged
/// bridge (client). Because both run as the same Windows user, both compute the SAME name with NO file
/// handoff — which sidesteps MSIX AppData virtualization entirely (named pipes live in the kernel
/// namespace and are never virtualized). The name is the prefix + a SHA-256 of the user's SID, so it's
/// stable across app restarts and unique per user; security comes from the owner-only pipe ACL
/// (<see cref="Listener.McpPipeSecurity"/>), not from the name being secret.
/// </summary>
public static class McpPipeName
{
    public static string ForCurrentUser()
    {
        string sid;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            sid = identity.User?.Value ?? "default-user";
        }
        catch
        {
            sid = "default-user";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))).ToLowerInvariant();
        return McpConstants.PipeNamePrefix + hash[..32];
    }
}
