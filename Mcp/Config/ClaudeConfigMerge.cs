using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanfarDesktop.Mcp.Config;

/// <summary>
/// Pure merge of the Verbinal MCP server entry into a Claude Desktop config
/// (<c>claude_desktop_config.json</c>): adds/updates <c>mcpServers["verbinal-canfar"]</c> while
/// PRESERVING every sibling server, its env, and all other top-level keys. The caller writes the
/// result atomically (temp+rename) with a .bak. 1-to-1 with the macOS config merge.
/// </summary>
public static class ClaudeConfigMerge
{
    /// <summary>The stable key for our server in <c>mcpServers</c>.</summary>
    public const string ServerKey = "verbinal-canfar";

    /// <summary>The argument the bridge exe is launched with to enter MCP-serve mode.</summary>
    public static readonly IReadOnlyList<string> DefaultArgs = new[] { "mcp" };

    /// <summary>
    /// Return the merged config JSON (pretty-printed). <paramref name="existingJson"/> may be null/
    /// empty (a fresh config is created). <paramref name="command"/> is the bridge exe path/alias.
    /// </summary>
    public static string MergedRoot(string? existingJson, string command, IReadOnlyList<string>? args = null)
    {
        args ??= DefaultArgs;

        JsonObject root;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            root = new JsonObject();
        }
        else
        {
            try { root = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { root = new JsonObject(); } // unparseable → start fresh (the .bak keeps the old)
        }

        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        servers[ServerKey] = new JsonObject
        {
            ["command"] = command,
            ["args"] = new JsonArray(args.Select(a => (JsonNode)a).ToArray()),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The <c>claude mcp add</c> command for Claude Code (we never auto-edit ~/.claude.json).</summary>
    public static string ClaudeCodeAddCommand(string exePath)
        => $"claude mcp add --transport stdio --scope user {ServerKey} -- \"{exePath}\" {string.Join(' ', DefaultArgs)}";
}
