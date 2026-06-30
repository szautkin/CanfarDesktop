using System.Text;
using System.Text.Json;

namespace CanfarDesktop.Mcp;

/// <summary>Outcome of a live MCP self-test: did a client reach the running server, and what did it see.</summary>
public sealed record McpSelfTestResult(bool Reachable, int? ToolCount, string? ServerName, string? Error)
{
    public static McpSelfTestResult Unreachable(string error) => new(false, null, null, error);
}

/// <summary>
/// Pure JSON-RPC request building + response parsing for <see cref="McpSelfTest"/>. Split from the
/// pipe-dialing OS glue so the wire shape is unit-testable with no named pipe. ndjson framing is added by
/// the transport, so these are unframed, single-line JSON documents.
/// </summary>
public static class McpSelfTestProtocol
{
    public const string InitializeParams =
        """{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"verbinal-selftest","version":"1.0"}}""";

    /// <summary>A JSON-RPC request document (with an id) for the given method + raw params JSON.</summary>
    public static byte[] BuildRequest(int id, string method, string paramsJson) =>
        Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{paramsJson}}}""");

    /// <summary>A JSON-RPC notification document (no id, no response expected).</summary>
    public static byte[] BuildNotification(string method) =>
        Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","method":"{{method}}"}""");

    /// <summary>Tool count from a <c>tools/list</c> response (<c>result.tools.length</c>), or null if the shape differs.</summary>
    public static int? ParseToolCount(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
                return tools.GetArrayLength();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Server name from an <c>initialize</c> response (<c>result.serverInfo.name</c>), or null.</summary>
    public static string? ParseServerName(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("serverInfo", out var info)
                && info.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                return name.GetString();
        }
        catch (JsonException) { }
        return null;
    }
}
