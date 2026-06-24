using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class WireTests
{
    // ── JsonValue ─────────────────────────────────────────────────────────────

    [Fact]
    public void JsonValue_RoundTrips_AllShapes()
    {
        const string json = """{"a":1,"b":1.5,"c":"x","d":true,"e":null,"f":[1,2],"g":{"h":false}}""";
        var value = JsonValue.Parse(json);

        var obj = Assert.IsType<JsonObject>(value);
        Assert.IsType<JsonInt>(obj["a"]);       // integer-valued number → JsonInt
        Assert.IsType<JsonDouble>(obj["b"]);    // fractional → JsonDouble
        Assert.Equal("x", Assert.IsType<JsonString>(obj["c"]).Value);
        Assert.True(Assert.IsType<JsonBool>(obj["d"]).Value);
        Assert.IsType<JsonNull>(obj["e"]);
        Assert.Equal(2, Assert.IsType<JsonArray>(obj["f"]).Items.Count);
        Assert.IsType<JsonObject>(obj["g"]);

        // Re-serialize losslessly (semantically equal).
        Assert.Equal(Normalize(json), Normalize(value.ToJsonString()));
    }

    [Fact]
    public void JsonValue_IndexerReturnsNull_ForMissingOrNonObject()
    {
        Assert.Null(JsonValue.Parse("""{"a":1}""")["missing"]);
        Assert.Null(JsonValue.Parse("[1,2]")["a"]);
    }

    // ── JsonRpcId / request parsing ───────────────────────────────────────────

    [Fact]
    public void Request_AbsentId_IsNotification()
    {
        var req = JsonRpcRequest.Parse("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.True(req.IsNotification);
        Assert.True(req.Id.IsNull);
        Assert.Equal("notifications/initialized", req.Method);
    }

    [Fact]
    public void Request_PresentId_IsNotNotification()
    {
        var req = JsonRpcRequest.Parse("""{"jsonrpc":"2.0","id":7,"method":"ping"}""");
        Assert.False(req.IsNotification);
        Assert.Equal(JsonRpcId.FromInt(7), req.Id);
    }

    [Fact]
    public void Request_StringId_Supported()
    {
        var req = JsonRpcRequest.Parse("""{"jsonrpc":"2.0","id":"abc","method":"ping"}""");
        Assert.Equal(JsonRpcId.FromString("abc"), req.Id);
    }

    [Fact]
    public void Request_MalformedId_Throws_NotCollapsedToNull()
        => Assert.Throws<JsonException>(() => JsonRpcRequest.Parse("""{"jsonrpc":"2.0","id":[1,2],"method":"ping"}"""));

    // ── Response serialization ────────────────────────────────────────────────

    [Fact]
    public void Response_Success_EmitsResult_NotError()
    {
        var resp = JsonRpcResponse.Success(JsonRpcId.FromInt(1), JsonValue.Parse("""{"ok":true}"""));
        var obj = (JsonObject)resp.ToJson();
        Assert.Equal("2.0", ((JsonString)obj["jsonrpc"]!).Value);
        Assert.NotNull(obj["result"]);
        Assert.Null(obj["error"]);
    }

    [Fact]
    public void Response_Failure_EmitsErrorCodeMessage()
    {
        var resp = JsonRpcResponse.Failure(JsonRpcId.FromInt(1),
            new JsonRpcErrorPayload(JsonRpcErrorCode.MethodNotFound, "method not found: foo"));
        var err = (JsonObject)((JsonObject)resp.ToJson())["error"]!;
        Assert.Equal(-32601, ((JsonInt)err["code"]!).Value);
        Assert.Contains("method not found", ((JsonString)err["message"]!).Value);
    }

    [Fact]
    public void Response_EmptySuccess_SerializesNullResult()
    {
        var resp = JsonRpcResponse.Success(JsonRpcId.FromInt(1), null);
        Assert.IsType<JsonNull>(((JsonObject)resp.ToJson())["result"]);
    }

    // ── MCP messages ──────────────────────────────────────────────────────────

    [Fact]
    public void Initialize_EchoesClientProtocolVersion()
    {
        var p = InitializeParams.Parse(JsonValue.Parse(
            "{\"protocolVersion\":\"" + McpProtocol.TestProtocolVersion + "\",\"clientInfo\":{\"name\":\"Claude\",\"version\":\"1.0\"}}"));
        Assert.Equal("2024-11-05", p.ProtocolVersion);
        Assert.Equal("Claude/1.0", p.ClientId);

        var result = new InitializeResult(p.ProtocolVersion, ServerCapabilities.Default, new ServerInfo("Verbinal", "1.1.0"));
        var json = (JsonObject)result.ToJson();
        Assert.Equal("2024-11-05", ((JsonString)json["protocolVersion"]!).Value); // echoed, not pinned
    }

    [Fact]
    public void CallToolContent_TextAndImageShapes()
    {
        var text = (JsonObject)CallToolContent.Text("hello").ToJson();
        Assert.Equal("text", ((JsonString)text["type"]!).Value);
        Assert.Equal("hello", ((JsonString)text["text"]!).Value);

        var image = (JsonObject)CallToolContent.Image("BASE64", "image/png").ToJson();
        Assert.Equal("image", ((JsonString)image["type"]!).Value);
        Assert.Equal("BASE64", ((JsonString)image["data"]!).Value);
        Assert.Equal("image/png", ((JsonString)image["mimeType"]!).Value);
    }

    [Fact]
    public void CallToolResult_IncludesIsErrorOnlyWhenSet()
    {
        var withFlag = (JsonObject)new CallToolResult(new[] { CallToolContent.Text("x") }, IsError: true).ToJson();
        Assert.True(((JsonBool)withFlag["isError"]!).Value);

        var withoutFlag = (JsonObject)new CallToolResult(new[] { CallToolContent.Text("x") }).ToJson();
        Assert.Null(withoutFlag["isError"]);
    }

    [Fact]
    public void CallToolParams_AbsentArguments_BecomeNull()
    {
        var p = CallToolParams.Parse(JsonValue.Parse("""{"name":"describe_app"}"""));
        Assert.Equal("describe_app", p.Name);
        Assert.IsType<JsonNull>(p.Arguments);
    }

    private static string Normalize(string json)
        => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json));
}
