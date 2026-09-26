using System.Text.Json;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Bridge;

/// <summary>
/// What the bridge answers in the app's place when it cannot reach it.
///
/// <para>Enough for a client that starts before the app to connect, rather than mark the server failed
/// and leave it failed until the person reconnects it by hand — which is what Claude Code, Claude
/// Desktop and OpenCode do with a server whose initialize fails. And enough for the agent to be told,
/// at every tool it calls, what is wrong and what the person can do about it.</para>
///
/// <para>The answers are the app's own (<c>McpServerService</c>), as far as the bridge can give them:
/// the client's protocolVersion echoed, ping and logging/setLevel empty, no resources, the tools the
/// app last listed (<see cref="AppSnapshot"/>). A tool call is a tool error saying why, which the agent
/// reads, where a protocol error is often shown only to the person. One difference: the initialize
/// says the tool list can change, because the bridge says so when the app comes.</para>
/// </summary>
public static class BridgeStandIn
{
    /// <summary>Why a request could not reach the app, when nothing more is known.</summary>
    public const string NotRunning =
        "Verbinal is not running, or its MCP server is off. Ask the person to start Verbinal and turn on "
        + "Settings ▸ AI agent ▸ Enable MCP server, then try again.";

    /// <summary>
    /// The answer to <paramref name="request"/>, for why the app could not answer it; a serviceUnavailable
    /// error for a method the bridge does not stand in for.
    /// </summary>
    public static JsonRpcResponse Answer(JsonRpcRequest request, string why, AppSnapshot snapshot) => request.Method switch
    {
        "initialize" => Initialize(request, why, snapshot),
        "tools/list" => JsonRpcResponse.Success(request.Id, snapshot[AppSnapshot.ToolsKey] ?? new ListToolsResult([]).ToJson()),
        "tools/call" => JsonRpcResponse.Success(request.Id, new CallToolResult([CallToolContent.Text(why)], IsError: true).ToJson()),
        "ping" or "logging/setLevel" => JsonRpcResponse.Success(request.Id, new JsonObject(new Dictionary<string, JsonValue>())),
        "resources/list" => JsonRpcResponse.Success(request.Id,
            new JsonObject(new Dictionary<string, JsonValue> { ["resources"] = new JsonArray([]) })),
        _ => Unavailable(request, why),
    };

    /// <summary>Whether answering <paramref name="method"/> for the app tells the client what its tools are.</summary>
    public static bool Lists(string method) => method is "initialize" or "tools/list";

    private static JsonRpcResponse Initialize(JsonRpcRequest request, string why, AppSnapshot snapshot)
    {
        InitializeParams asked;
        try { asked = InitializeParams.Parse(request.Params ?? JsonValue.Null); }
        catch (JsonException) { return Unavailable(request, why); }

        var version = (snapshot[AppSnapshot.ServerInfoKey]?["version"] as JsonString)?.Value ?? "unknown";
        var result = new InitializeResult(
            asked.ProtocolVersion,
            ServerCapabilities.Default with { Tools = new ToolsCapability(ListChanged: true) },
            new ServerInfo(McpConstants.ServerName, version),
            $"{why} Until then, each tool answers with that. This server connects to Verbinal by itself once "
            + "it is running, and then says its tool list has changed.");
        return JsonRpcResponse.Success(request.Id, result.ToJson());
    }

    private static JsonRpcResponse Unavailable(JsonRpcRequest request, string why)
        => JsonRpcResponse.Failure(request.Id, new JsonRpcErrorPayload(JsonRpcErrorCode.ServiceUnavailable, why));
}
