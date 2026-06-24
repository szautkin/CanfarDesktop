using System.Text;
using System.Text.Json;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Transport;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp;

/// <summary>Identifies this server in the initialize handshake.</summary>
public sealed record ServerIdentity(string Name, string Version);

/// <summary>Gate that decides whether a connecting client may proceed past initialize.</summary>
public interface IApprovalGate
{
    Task<bool> PermitAsync(string clientId);
}

public sealed class AllowAllGate : IApprovalGate
{
    public static readonly AllowAllGate Instance = new();
    public Task<bool> PermitAsync(string clientId) => Task.FromResult(true);
}

public sealed class DenyAllGate : IApprovalGate
{
    public Task<bool> PermitAsync(string clientId) => Task.FromResult(false);
}

/// <summary>
/// ONE instance per connection. Serves the MCP method set over an <see cref="IMcpTransport"/>:
/// initialize (echoes the client's protocolVersion, runs the approval gate), tools/list (agent-safe
/// only), tools/call (via the router), ping / logging/setLevel (empty ok), resources/* (empty),
/// anything else → methodNotFound. Notifications (absent id) get no reply; tools/* before initialize
/// → serverNotInitialized. The serve loop + dispatch are pure and testable over InMemoryTransport.
/// 1-to-1 with the macOS MCPBridgeService.
/// </summary>
public sealed class McpServerService
{
    private readonly McpToolRouter _router;
    private readonly ServerIdentity _identity;
    private readonly IApprovalGate _gate;

    private bool _initialized;
    private string _clientId = "unknown-client";

    public McpServerService(McpToolRouter router, ServerIdentity identity, IApprovalGate? gate = null)
    {
        _router = router;
        _identity = identity;
        _gate = gate ?? AllowAllGate.Instance;
    }

    public async Task ServeAsync(IMcpTransport transport, CancellationToken cancellationToken = default)
    {
        await foreach (var frame in transport.Incoming.ReadAllAsync(cancellationToken))
        {
            if (frame.Length == 0) continue; // keep-alive
            var response = await HandleFrameAsync(frame, cancellationToken);
            if (response is not null)
                await transport.SendAsync(response, cancellationToken);
        }
    }

    /// <summary>Process one incoming document; returns the response bytes, or null for a notification.</summary>
    public async Task<byte[]?> HandleFrameAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        JsonValue root;
        try { root = JsonValue.Parse((ReadOnlySpan<byte>)frame); }
        catch { return Encode(JsonRpcResponse.Failure(JsonRpcId.Null, Err(JsonRpcErrorCode.ParseError, "parse error"))); }

        JsonRpcRequest request;
        try { request = JsonRpcRequest.Parse(root); }
        catch (JsonException ex) { return Encode(JsonRpcResponse.Failure(JsonRpcId.Null, Err(JsonRpcErrorCode.InvalidRequest, ex.Message))); }

        if (request.IsNotification) return null; // notifications/initialized etc. → no reply

        return Encode(await DispatchAsync(request, cancellationToken));
    }

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest req, CancellationToken ct) => req.Method switch
    {
        "initialize" => await HandleInitializeAsync(req),
        "ping" => JsonRpcResponse.Success(req.Id, EmptyObject()),
        "logging/setLevel" => JsonRpcResponse.Success(req.Id, EmptyObject()),
        "tools/list" => RequireInitialized(req) ?? HandleToolsList(req),
        "tools/call" => RequireInitialized(req) ?? await HandleToolsCallAsync(req, ct),
        "resources/list" => RequireInitialized(req) ?? JsonRpcResponse.Success(req.Id,
            new JsonObject(new Dictionary<string, JsonValue> { ["resources"] = new JsonArray(Array.Empty<JsonValue>()) })),
        "resources/read" => RequireInitialized(req) ?? JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.InvalidParams, "resources/read is not supported")),
        _ => JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.MethodNotFound, $"method not found: {req.Method}")),
    };

    private JsonRpcResponse? RequireInitialized(JsonRpcRequest req)
        => _initialized ? null : JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.ServerNotInitialized, "Server has not been initialized."));

    private async Task<JsonRpcResponse> HandleInitializeAsync(JsonRpcRequest req)
    {
        if (req.Params is null)
            return JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.InvalidParams, "missing params"));

        InitializeParams p;
        try { p = InitializeParams.Parse(req.Params); }
        catch (JsonException ex) { return JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.InvalidParams, ex.Message)); }

        _clientId = p.ClientId;
        if (!await _gate.PermitAsync(_clientId))
            return JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.SessionNotApproved, "Client not approved by user."));

        _initialized = true;
        // Echo the client's protocolVersion — never pin a server constant.
        var result = new InitializeResult(p.ProtocolVersion, ServerCapabilities.Default, new ServerInfo(_identity.Name, _identity.Version));
        return JsonRpcResponse.Success(req.Id, result.ToJson());
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest req)
        => JsonRpcResponse.Success(req.Id, new ListToolsResult(_router.ExternalManifest.Select(d => d.ToWire()).ToList()).ToJson());

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest req, CancellationToken ct)
    {
        if (req.Params is null)
            return JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.InvalidParams, "missing params"));

        CallToolParams p;
        try { p = CallToolParams.Parse(req.Params); }
        catch (JsonException ex) { return JsonRpcResponse.Failure(req.Id, Err(JsonRpcErrorCode.InvalidParams, ex.Message)); }

        var context = McpToolContext.ForExternal(_clientId, Guid.NewGuid());
        var result = await _router.DispatchAsync(p.Name, p.Arguments, context, ct);
        return JsonRpcResponse.Success(req.Id, MapToolResult(result).ToJson());
    }

    private static CallToolResult MapToolResult(ToolResult result) => result switch
    {
        DataResult d => new CallToolResult(new[] { CallToolContent.Text(Encoding.UTF8.GetString(d.Json)) }, IsError: false),
        FailedResult f => new CallToolResult(new[] { CallToolContent.Text(f.Reason.Description) }, IsError: true),
        ImageToolResult img => new CallToolResult(BuildImageContent(img), IsError: false),
        _ => new CallToolResult(new[] { CallToolContent.Text("Unknown tool result") }, IsError: true),
    };

    private static IReadOnlyList<CallToolContent> BuildImageContent(ImageToolResult img)
    {
        var blocks = new List<CallToolContent> { CallToolContent.Image(Convert.ToBase64String(img.Data), img.MimeType) };
        if (img.Caption is { Length: > 0 }) blocks.Add(CallToolContent.Text(img.Caption));
        return blocks;
    }

    private static JsonRpcErrorPayload Err(int code, string message) => new(code, message);
    private static JsonValue EmptyObject() => new JsonObject(new Dictionary<string, JsonValue>());
    private static byte[] Encode(JsonRpcResponse response) => Encoding.UTF8.GetBytes(response.ToJsonString());
}
