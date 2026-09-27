using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Bridge;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class BridgeStandInTests
{
    private static JsonRpcRequest Request(string method, string? prms = null)
        => JsonRpcRequest.Parse($@"{{""jsonrpc"":""2.0"",""id"":7,""method"":""{method}""{(prms is null ? "" : $@",""params"":{prms}")}}}");

    private static JsonValue Result(JsonRpcResponse response)
    {
        Assert.Null(response.Error);
        return response.Result!;
    }

    private const string InitializeParams =
        @"{""protocolVersion"":""2025-03-26"",""clientInfo"":{""name"":""opencode"",""version"":""1""}}";

    [Fact]
    public void Initialize_EchoesTheClientsVersion_AndSaysTheToolsCanChange()
    {
        var result = Result(BridgeStandIn.Answer(Request("initialize", InitializeParams), "why", new AppSnapshot()));

        Assert.Equal("2025-03-26", ((JsonString)result["protocolVersion"]!).Value);
        Assert.Equal(McpConstants.ServerName, ((JsonString)result["serverInfo"]!["name"]!).Value);
        Assert.Equal("unknown", ((JsonString)result["serverInfo"]!["version"]!).Value);
        Assert.Equal(new JsonBool(true), result["capabilities"]!["tools"]!["listChanged"]);
        Assert.StartsWith("why ", ((JsonString)result["instructions"]!).Value);
    }

    [Fact]
    public void Initialize_GivesTheVersionTheAppLastGave()
    {
        var snapshot = new AppSnapshot();
        snapshot.Remember(AppSnapshot.ServerInfoKey, JsonValue.Parse(@"{""name"":""verbinal-canfar"",""version"":""1.4.1.0""}"));

        var result = Result(BridgeStandIn.Answer(Request("initialize", InitializeParams), "why", snapshot));

        Assert.Equal("1.4.1.0", ((JsonString)result["serverInfo"]!["version"]!).Value);
    }

    [Fact]
    public void Initialize_WithoutParams_IsRefused()
    {
        var answer = BridgeStandIn.Answer(Request("initialize"), "why", new AppSnapshot());

        Assert.Equal(JsonRpcErrorCode.ServiceUnavailable, answer.Error!.Code);
        Assert.Equal("why", answer.Error.Message);
    }

    [Fact]
    public void ToolsList_IsEmpty_WhenTheAppHasNeverListedAny()
        => Assert.Equal(@"{""tools"":[]}", Result(BridgeStandIn.Answer(Request("tools/list"), "why", new AppSnapshot())).ToJsonString());

    [Fact]
    public void ToolsCall_IsAToolError_SayingWhy()
    {
        var result = Result(BridgeStandIn.Answer(Request("tools/call", @"{""name"":""describe_app""}"), BridgeStandIn.NotRunning, new AppSnapshot()));

        Assert.Equal(new JsonBool(true), result["isError"]);
        Assert.Equal(BridgeStandIn.NotRunning, ((JsonString)((JsonArray)result["content"]!).Items[0]["text"]!).Value);
    }

    [Theory]
    [InlineData("ping", "{}")]
    [InlineData("logging/setLevel", "{}")]
    [InlineData("resources/list", @"{""resources"":[]}")]
    public void TheAppsEmptyAnswers_AreGivenAsTheAppGivesThem(string method, string expected)
        => Assert.Equal(expected, Result(BridgeStandIn.Answer(Request(method), "why", new AppSnapshot())).ToJsonString());

    [Fact]
    public void AnythingElse_IsServiceUnavailable()
        => Assert.Equal(JsonRpcErrorCode.ServiceUnavailable,
            BridgeStandIn.Answer(Request("prompts/list"), "why", new AppSnapshot()).Error!.Code);

    [Theory]
    [InlineData("initialize", true)]
    [InlineData("tools/list", true)]
    [InlineData("tools/call", false)]
    [InlineData("ping", false)]
    public void OnlyInitializeAndToolsList_TellTheClientWhatTheToolsAre(string method, bool lists)
        => Assert.Equal(lists, BridgeStandIn.Lists(method));

    // ── AppSnapshot ───────────────────────────────────────────────────────────

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"snapshot-{Guid.NewGuid():N}.json");

    [Fact]
    public void Snapshot_IsReadBack_ByTheNextBridge()
    {
        var file = TempFile();
        try
        {
            new AppSnapshot(file).Remember(AppSnapshot.ToolsKey, JsonValue.Parse(@"{""tools"":[]}"));
            Assert.Equal(@"{""tools"":[]}", new AppSnapshot(file)[AppSnapshot.ToolsKey]!.ToJsonString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Snapshot_ThatIsNotJson_IsNoMemory_AndIsReplaced()
    {
        var file = TempFile();
        try
        {
            File.WriteAllText(file, "{\"tools\":");
            var snapshot = new AppSnapshot(file);
            Assert.Null(snapshot[AppSnapshot.ToolsKey]);

            snapshot.Remember(AppSnapshot.ToolsKey, JsonValue.Parse(@"{""tools"":[]}"));
            Assert.NotNull(new AppSnapshot(file)[AppSnapshot.ToolsKey]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Snapshot_WithoutAFile_IsKeptInMemory()
    {
        var snapshot = new AppSnapshot();
        snapshot.Remember(AppSnapshot.ServerInfoKey, new JsonString("x"));
        Assert.Equal(new JsonString("x"), snapshot[AppSnapshot.ServerInfoKey]);
    }

    [Fact]
    public void Snapshot_ThatCannotBeWritten_IsStillRemembered()
    {
        var file = TempFile();
        File.WriteAllText(file, "");
        try
        {
            var snapshot = new AppSnapshot(Path.Combine(file, "x.json")); // a folder that is a file
            snapshot.Remember(AppSnapshot.ToolsKey, new JsonString("x"));
            Assert.Equal(new JsonString("x"), snapshot[AppSnapshot.ToolsKey]);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
