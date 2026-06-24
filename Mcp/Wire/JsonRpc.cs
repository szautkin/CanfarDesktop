using System.Text.Json;

namespace CanfarDesktop.Mcp.Wire;

/// <summary>JSON-RPC 2.0 message id — an int, a string, or null. 1-to-1 with the macOS JSONRPCID.</summary>
public readonly struct JsonRpcId : IEquatable<JsonRpcId>
{
    private enum Kind : byte { Null, Int, String }

    private readonly Kind _kind;
    private readonly long _int;
    private readonly string? _str;

    private JsonRpcId(Kind kind, long i, string? s) { _kind = kind; _int = i; _str = s; }

    public static readonly JsonRpcId Null = new(Kind.Null, 0, null);
    public static JsonRpcId FromInt(long value) => new(Kind.Int, value, null);
    public static JsonRpcId FromString(string value) => new(Kind.String, 0, value);

    public bool IsNull => _kind == Kind.Null;

    public JsonValue ToJson() => _kind switch
    {
        Kind.Int => new JsonInt(_int),
        Kind.String => new JsonString(_str!),
        _ => JsonValue.Null,
    };

    /// <summary>Parse an id from JSON; throws when it isn't int/string/null (mirrors macOS typeMismatch).</summary>
    public static JsonRpcId FromJson(JsonValue value) => value switch
    {
        JsonInt i => FromInt(i.Value),
        JsonString s => FromString(s.Value),
        JsonNull => Null,
        _ => throw new JsonException("JSON-RPC id must be an integer, string, or null"),
    };

    public bool Equals(JsonRpcId other) => _kind == other._kind && _int == other._int && _str == other._str;
    public override bool Equals(object? obj) => obj is JsonRpcId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_kind, _int, _str);
    public override string ToString() => _kind switch { Kind.Int => _int.ToString(), Kind.String => _str!, _ => "null" };
}

/// <summary>Standard + server-defined JSON-RPC / MCP error codes.</summary>
public static class JsonRpcErrorCode
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServiceUnavailable = -32000;
    public const int SessionNotApproved = -32001;
    public const int ServerNotInitialized = -32002;
}

public sealed record JsonRpcErrorPayload(int Code, string Message, JsonValue? Data = null)
{
    public JsonValue ToJson()
    {
        var members = new Dictionary<string, JsonValue>
        {
            ["code"] = new JsonInt(Code),
            ["message"] = new JsonString(Message),
        };
        if (Data is not null) members["data"] = Data;
        return new JsonObject(members);
    }
}

/// <summary>
/// A parsed JSON-RPC request. <see cref="IsNotification"/> is true when the <c>id</c> KEY was absent
/// (no reply is sent) — distinct from a present-but-null id. A present-but-malformed id throws at
/// parse time rather than collapsing to null (mirrors macOS).
/// </summary>
public sealed record JsonRpcRequest(JsonRpcId Id, string Method, JsonValue? Params)
{
    public bool IsNotification { get; init; }

    public static JsonRpcRequest Parse(string json) => Parse(JsonValue.Parse(json));

    public static JsonRpcRequest Parse(JsonValue root)
    {
        if (root is not JsonObject obj)
            throw new JsonException("JSON-RPC request must be a JSON object");

        if (obj.Members.GetValueOrDefault("method") is not JsonString method)
            throw new JsonException("JSON-RPC request missing 'method'");

        var hasId = obj.Members.TryGetValue("id", out var idValue);
        var id = hasId ? JsonRpcId.FromJson(idValue!) : JsonRpcId.Null;
        obj.Members.TryGetValue("params", out var prms);

        return new JsonRpcRequest(id, method.Value, prms) { IsNotification = !hasId };
    }
}

/// <summary>A JSON-RPC response — result XOR error. Empty success serializes <c>result: null</c>.</summary>
public sealed record JsonRpcResponse
{
    public JsonRpcId Id { get; init; }
    public JsonValue? Result { get; init; }
    public JsonRpcErrorPayload? Error { get; init; }

    public static JsonRpcResponse Success(JsonRpcId id, JsonValue? result)
        => new() { Id = id, Result = result ?? JsonValue.Null };

    public static JsonRpcResponse Failure(JsonRpcId id, JsonRpcErrorPayload error)
        => new() { Id = id, Error = error };

    public JsonValue ToJson()
    {
        var members = new Dictionary<string, JsonValue>
        {
            ["jsonrpc"] = new JsonString("2.0"),
            ["id"] = Id.ToJson(),
        };
        if (Error is not null) members["error"] = Error.ToJson();
        else members["result"] = Result ?? JsonValue.Null;
        return new JsonObject(members);
    }

    public string ToJsonString() => ToJson().ToJsonString();
}
