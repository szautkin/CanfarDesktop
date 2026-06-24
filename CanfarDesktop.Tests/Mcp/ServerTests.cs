using System.Text;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ServerTests
{
    private const string Init =
        @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2024-11-05"",""clientInfo"":{""name"":""Claude"",""version"":""1.0""}}}";

    private static McpServerService NewServer(IApprovalGate? gate = null)
    {
        var router = new McpToolRouter(new IMcpTool[]
        {
            new DescribeAppTool("1.0"),
            new GetAuthStateTool(() => new AuthSnapshot(true, "alice")),
        });
        return new McpServerService(router, new ServerIdentity("Verbinal", "1.1.0"), gate);
    }

    private static async Task<JsonObject?> Send(McpServerService s, string json)
    {
        var bytes = await s.HandleFrameAsync(Encoding.UTF8.GetBytes(json));
        return bytes is null ? null : (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static JsonObject ResultOf(JsonObject? resp) => (JsonObject)resp!["result"]!;
    private static JsonObject ErrorOf(JsonObject? resp) => (JsonObject)resp!["error"]!;
    private static long Code(JsonObject? resp) => ((JsonInt)ErrorOf(resp)["code"]!).Value;

    [Fact]
    public async Task Initialize_EchoesProtocolVersion_AndServerInfo()
    {
        var result = ResultOf(await Send(NewServer(), Init));
        Assert.Equal("2024-11-05", ((JsonString)result["protocolVersion"]!).Value); // echoed, not pinned
        Assert.Equal("Verbinal", ((JsonString)((JsonObject)result["serverInfo"]!)["name"]!).Value);
    }

    [Fact]
    public async Task ToolsBeforeInitialize_ServerNotInitialized()
    {
        var resp = await Send(NewServer(), @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}");
        Assert.Equal(-32002, Code(resp));
    }

    [Fact]
    public async Task Notification_GetsNoReply()
    {
        var resp = await Send(NewServer(), @"{""jsonrpc"":""2.0"",""method"":""notifications/initialized""}");
        Assert.Null(resp);
    }

    [Fact]
    public async Task Ping_ReturnsEmptyObject()
    {
        var result = ResultOf(await Send(NewServer(), @"{""jsonrpc"":""2.0"",""id"":3,""method"":""ping""}"));
        Assert.Empty(((JsonObject)result).Members);
    }

    [Fact]
    public async Task UnknownMethod_MethodNotFound()
    {
        var resp = await Send(NewServer(), @"{""jsonrpc"":""2.0"",""id"":4,""method"":""bogus/thing""}");
        Assert.Equal(-32601, Code(resp));
    }

    [Fact]
    public async Task ParseError_ReturnsParseError()
    {
        var resp = await Send(NewServer(), "{ not valid json");
        Assert.Equal(-32700, Code(resp));
    }

    [Fact]
    public async Task DenyGate_Initialize_NotApproved()
    {
        var resp = await Send(NewServer(new DenyAllGate()), Init);
        Assert.Equal(-32001, Code(resp));
    }

    [Fact]
    public async Task AfterInit_ToolsList_ExposesAgentSafeTools()
    {
        var s = NewServer();
        await Send(s, Init);
        var result = ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":5,""method"":""tools/list""}"));
        var names = ((JsonArray)result["tools"]!).Items.Select(t => ((JsonString)((JsonObject)t)["name"]!).Value).ToList();
        Assert.Contains("describe_app", names);
        Assert.Contains("get_auth_state", names);
    }

    [Fact]
    public async Task AfterInit_ToolsCall_ReturnsTextContent()
    {
        var s = NewServer();
        await Send(s, Init);
        var result = ResultOf(await Send(s,
            @"{""jsonrpc"":""2.0"",""id"":6,""method"":""tools/call"",""params"":{""name"":""describe_app""}}"));
        var content = (JsonArray)result["content"]!;
        Assert.Equal("text", ((JsonString)((JsonObject)content.Items[0])["type"]!).Value);
        Assert.False(((JsonBool)result["isError"]!).Value);
    }

    [Fact]
    public async Task AfterInit_ToolsCall_UnknownTool_IsErrorTrue()
    {
        var s = NewServer();
        await Send(s, Init);
        var result = ResultOf(await Send(s,
            @"{""jsonrpc"":""2.0"",""id"":7,""method"":""tools/call"",""params"":{""name"":""does_not_exist""}}"));
        Assert.True(((JsonBool)result["isError"]!).Value); // tool failure surfaces as isError, not a JSON-RPC error
    }
}
