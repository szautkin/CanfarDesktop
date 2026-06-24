using System.Text.Json;

namespace CanfarDesktop.Mcp.Wire;

/// <summary>MCP protocol constants. The server ECHOES the client's protocolVersion (no server pin).</summary>
public static class McpProtocol
{
    /// <summary>Only used by tests to drive an initialize handshake — never pinned by the server.</summary>
    public const string TestProtocolVersion = "2024-11-05";
}

public sealed record ClientInfo(string Name, string Version);
public sealed record ServerInfo(string Name, string Version);

/// <summary>Params of an <c>initialize</c> request.</summary>
public sealed record InitializeParams(string ProtocolVersion, ClientInfo? ClientInfo, JsonValue? Capabilities)
{
    public static InitializeParams Parse(JsonValue value)
    {
        if (value is not JsonObject obj)
            throw new JsonException("initialize params must be an object");
        var protocol = (obj["protocolVersion"] as JsonString)?.Value
            ?? throw new JsonException("initialize params missing protocolVersion");

        ClientInfo? client = null;
        if (obj["clientInfo"] is JsonObject ci)
            client = new ClientInfo((ci["name"] as JsonString)?.Value ?? "", (ci["version"] as JsonString)?.Value ?? "");

        return new InitializeParams(protocol, client, obj["capabilities"]);
    }

    /// <summary>A stable client id "name/version", or a fallback when clientInfo is absent.</summary>
    public string ClientId => ClientInfo is { } c && c.Name.Length > 0 ? $"{c.Name}/{c.Version}" : "unknown-client";
}

public sealed record ToolsCapability(bool ListChanged)
{
    public JsonValue ToJson() => new JsonObject(new Dictionary<string, JsonValue> { ["listChanged"] = new JsonBool(ListChanged) });
}

public sealed record ResourcesCapability(bool Subscribe, bool ListChanged)
{
    public JsonValue ToJson() => new JsonObject(new Dictionary<string, JsonValue>
    {
        ["subscribe"] = new JsonBool(Subscribe),
        ["listChanged"] = new JsonBool(ListChanged),
    });
}

public sealed record ServerCapabilities(ToolsCapability Tools, ResourcesCapability Resources)
{
    public static ServerCapabilities Default => new(new ToolsCapability(false), new ResourcesCapability(false, false));

    public JsonValue ToJson() => new JsonObject(new Dictionary<string, JsonValue>
    {
        ["tools"] = Tools.ToJson(),
        ["resources"] = Resources.ToJson(),
        ["logging"] = new JsonObject(new Dictionary<string, JsonValue>()),
    });
}

public sealed record InitializeResult(string ProtocolVersion, ServerCapabilities Capabilities, ServerInfo ServerInfo, string? Instructions = null)
{
    public JsonValue ToJson()
    {
        var members = new Dictionary<string, JsonValue>
        {
            ["protocolVersion"] = new JsonString(ProtocolVersion),
            ["capabilities"] = Capabilities.ToJson(),
            ["serverInfo"] = new JsonObject(new Dictionary<string, JsonValue>
            {
                ["name"] = new JsonString(ServerInfo.Name),
                ["version"] = new JsonString(ServerInfo.Version),
            }),
        };
        if (Instructions is not null) members["instructions"] = new JsonString(Instructions);
        return new JsonObject(members);
    }
}

/// <summary>A tool's manifest entry for <c>tools/list</c>.</summary>
public sealed record ToolDefinitionWire(string Name, string Description, JsonValue InputSchema)
{
    public JsonValue ToJson() => new JsonObject(new Dictionary<string, JsonValue>
    {
        ["name"] = new JsonString(Name),
        ["description"] = new JsonString(Description),
        ["inputSchema"] = InputSchema,
    });
}

public sealed record ListToolsResult(IReadOnlyList<ToolDefinitionWire> Tools, string? NextCursor = null)
{
    public JsonValue ToJson()
    {
        var members = new Dictionary<string, JsonValue>
        {
            ["tools"] = new JsonArray(Tools.Select(t => t.ToJson()).ToList()),
        };
        if (NextCursor is not null) members["nextCursor"] = new JsonString(NextCursor);
        return new JsonObject(members);
    }
}

/// <summary>Params of a <c>tools/call</c> request. Absent arguments become <see cref="JsonValue.Null"/>.</summary>
public sealed record CallToolParams(string Name, JsonValue Arguments)
{
    public static CallToolParams Parse(JsonValue value)
    {
        if (value is not JsonObject obj)
            throw new JsonException("tools/call params must be an object");
        var name = (obj["name"] as JsonString)?.Value ?? throw new JsonException("tools/call params missing name");
        return new CallToolParams(name, obj["arguments"] ?? JsonValue.Null);
    }
}

/// <summary>One content block in a <c>tools/call</c> result (text / image / forward-compat other).</summary>
public abstract record CallToolContent
{
    public static CallToolContent Text(string text) => new TextContent(text);
    public static CallToolContent Image(string base64Data, string mimeType) => new ImageContent(base64Data, mimeType);

    public JsonValue ToJson() => this switch
    {
        TextContent t => new JsonObject(new Dictionary<string, JsonValue>
        {
            ["type"] = new JsonString("text"),
            ["text"] = new JsonString(t.Value),
        }),
        ImageContent i => new JsonObject(new Dictionary<string, JsonValue>
        {
            ["type"] = new JsonString("image"),
            ["data"] = new JsonString(i.Data),
            ["mimeType"] = new JsonString(i.MimeType),
        }),
        OtherContent o => o.Value,
        _ => throw new JsonException($"Unknown content block {GetType().Name}"),
    };
}

public sealed record TextContent(string Value) : CallToolContent;
public sealed record ImageContent(string Data, string MimeType) : CallToolContent;
public sealed record OtherContent(JsonValue Value) : CallToolContent;

public sealed record CallToolResult(IReadOnlyList<CallToolContent> Content, bool? IsError = null)
{
    public JsonValue ToJson()
    {
        var members = new Dictionary<string, JsonValue>
        {
            ["content"] = new JsonArray(Content.Select(c => c.ToJson()).ToList()),
        };
        if (IsError is { } e) members["isError"] = new JsonBool(e);
        return new JsonObject(members);
    }
}
