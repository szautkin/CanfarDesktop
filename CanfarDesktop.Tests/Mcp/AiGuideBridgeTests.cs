using System.Text;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The AI Guide bridge hook in <see cref="McpServerService"/>: per-request description overrides and
/// user guide tools applied to <c>tools/list</c>, and guide-tool short-circuit on <c>tools/call</c>.
/// Mirrors the macOS MCPBridgeService behaviour.
/// </summary>
public class AiGuideBridgeTests
{
    private const string Init =
        @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2024-11-05"",""clientInfo"":{""name"":""Claude"",""version"":""1.0""}}}";

    private static McpToolRouter Router() => new(new IMcpTool[]
    {
        new DescribeAppTool("1.0"),
        new GetAuthStateTool(() => new AuthSnapshot(true, "alice")),
    });

    private static McpServerService NewServer(Func<AiGuideSnapshot> aiGuide)
        => new(Router(), new ServerIdentity("Verbinal", "1.1.0"), aiGuide: aiGuide);

    private static async Task<JsonObject> Send(McpServerService s, string json)
    {
        var bytes = await s.HandleFrameAsync(Encoding.UTF8.GetBytes(json));
        return (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(bytes!));
    }

    private static JsonObject ResultOf(JsonObject resp) => (JsonObject)resp["result"]!;

    private static List<JsonObject> ToolList(JsonObject result)
        => ((JsonArray)result["tools"]!).Items.Cast<JsonObject>().ToList();

    private static string Name(JsonObject tool) => ((JsonString)tool["name"]!).Value;
    private static string Desc(JsonObject tool) => ((JsonString)tool["description"]!).Value;

    private static AiGuideSnapshot Snapshot(
        Dictionary<string, string>? overrides = null,
        params AiGuideToolEntry[] guides)
        => new(overrides ?? new Dictionary<string, string>(), guides);

    [Fact]
    public async Task ToolsList_SubstitutesOverriddenDescription()
    {
        var snap = Snapshot(new Dictionary<string, string> { ["describe_app"] = "My custom blurb." });
        var s = NewServer(() => snap);
        await Send(s, Init);

        var tools = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}")));
        var describe = Assert.Single(tools, t => Name(t) == "describe_app");
        Assert.Equal("My custom blurb.", Desc(describe));
        // A non-overridden tool keeps its built-in description.
        var auth = Assert.Single(tools, t => Name(t) == "get_auth_state");
        Assert.Contains("signed in", Desc(auth));
    }

    [Fact]
    public async Task ToolsList_AppendsGuideTools_WithEmptyObjectSchema()
    {
        var guide = new AiGuideToolEntry(Guid.NewGuid(), "survey_tips", "How to run a survey", "Step 1. Step 2.");
        var s = NewServer(() => Snapshot(guides: guide));
        await Send(s, Init);

        var tools = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/list""}")));
        var entry = Assert.Single(tools, t => Name(t) == "survey_tips");
        Assert.Equal("How to run a survey", Desc(entry));
        var schema = (JsonObject)entry["inputSchema"]!;
        Assert.Equal("object", ((JsonString)schema["type"]!).Value);
        // Built-ins are still present alongside the guide tool.
        Assert.Contains(tools, t => Name(t) == "describe_app");
    }

    [Fact]
    public async Task ToolsCall_GuideTool_ReturnsStoredBody_WithoutDispatch()
    {
        var guide = new AiGuideToolEntry(Guid.NewGuid(), "survey_tips", "one-liner", "The full body.");
        var s = NewServer(() => Snapshot(guides: guide));
        await Send(s, Init);

        var result = ResultOf(await Send(s,
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""tools/call"",""params"":{""name"":""survey_tips""}}"));
        var content = (JsonArray)result["content"]!;
        Assert.Equal("The full body.", ((JsonString)((JsonObject)content.Items[0])["text"]!).Value);
        Assert.False(((JsonBool)result["isError"]!).Value);
    }

    [Fact]
    public async Task ToolsCall_GuideWithoutBody_FallsBackToDescription()
    {
        var guide = new AiGuideToolEntry(Guid.NewGuid(), "tip", "Just the one-liner.", null);
        var s = NewServer(() => Snapshot(guides: guide));
        await Send(s, Init);

        var result = ResultOf(await Send(s,
            @"{""jsonrpc"":""2.0"",""id"":5,""method"":""tools/call"",""params"":{""name"":""tip""}}"));
        var content = (JsonArray)result["content"]!;
        Assert.Equal("Just the one-liner.", ((JsonString)((JsonObject)content.Items[0])["text"]!).Value);
    }

    [Fact]
    public async Task ToolsCall_BuiltinStillDispatches_WhenGuidesPresent()
    {
        var guide = new AiGuideToolEntry(Guid.NewGuid(), "tip", "x", "y");
        var s = NewServer(() => Snapshot(guides: guide));
        await Send(s, Init);

        var result = ResultOf(await Send(s,
            @"{""jsonrpc"":""2.0"",""id"":6,""method"":""tools/call"",""params"":{""name"":""describe_app""}}"));
        Assert.False(((JsonBool)result["isError"]!).Value);
        Assert.Equal("text", ((JsonString)((JsonObject)((JsonArray)result["content"]!).Items[0])["type"]!).Value);
    }

    [Fact]
    public async Task ToolsList_IsReReadPerCall_LiveRetune()
    {
        // The delegate returns whatever the holder currently points at — proving the manifest re-tunes
        // live (no reconnect) the way the macOS bridge does.
        var snap = AiGuideSnapshot.Empty;
        var s = NewServer(() => snap);
        await Send(s, Init);

        var before = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":7,""method"":""tools/list""}")));
        Assert.DoesNotContain(before, t => Name(t) == "new_guide");

        snap = Snapshot(guides: new AiGuideToolEntry(Guid.NewGuid(), "new_guide", "added at runtime", null));
        var after = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":8,""method"":""tools/list""}")));
        Assert.Contains(after, t => Name(t) == "new_guide");
    }

    [Fact]
    public async Task ToolsList_GuideCollidingWithBuiltin_DoesNotDuplicate()
    {
        // A guide whose name equals a built-in (e.g. a DB hand-edit) must not produce a duplicate name.
        var guide = new AiGuideToolEntry(Guid.NewGuid(), "describe_app", "shadow", "shadow body");
        var s = NewServer(() => Snapshot(guides: guide));
        await Send(s, Init);

        var tools = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":10,""method"":""tools/list""}")));
        Assert.Single(tools, t => Name(t) == "describe_app");                 // exactly one, the built-in
        Assert.DoesNotContain("shadow", Desc(Assert.Single(tools, t => Name(t) == "describe_app")));
    }

    [Fact]
    public async Task ToolsCall_GuideCollidingWithBuiltin_DispatchesBuiltin()
    {
        var guide = new AiGuideToolEntry(Guid.NewGuid(), "describe_app", "shadow", "SHADOW_BODY_SENTINEL");
        var s = NewServer(() => Snapshot(guides: guide));
        await Send(s, Init);

        var result = ResultOf(await Send(s,
            @"{""jsonrpc"":""2.0"",""id"":11,""method"":""tools/call"",""params"":{""name"":""describe_app""}}"));
        var text = ((JsonString)((JsonObject)((JsonArray)result["content"]!).Items[0])["text"]!).Value;
        Assert.DoesNotContain("SHADOW_BODY_SENTINEL", text); // the built-in ran, not the guide
        Assert.False(((JsonBool)result["isError"]!).Value);
    }

    [Fact]
    public async Task ToolsList_OverrideForUnknownTool_IsIgnored()
    {
        var snap = Snapshot(new Dictionary<string, string> { ["no_such_tool"] = "ghost" });
        var s = NewServer(() => snap);
        await Send(s, Init);

        var tools = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":12,""method"":""tools/list""}")));
        Assert.DoesNotContain(tools, t => Name(t) == "no_such_tool");         // orphan override never appears
        Assert.Contains(tools, t => Name(t) == "describe_app");               // and nothing else breaks
    }

    [Fact]
    public async Task NoResolver_BehavesAsUntunedManifest()
    {
        var s = new McpServerService(Router(), new ServerIdentity("Verbinal", "1.1.0")); // no aiGuide
        await Send(s, Init);
        var tools = ToolList(ResultOf(await Send(s, @"{""jsonrpc"":""2.0"",""id"":9,""method"":""tools/list""}")));
        Assert.Contains(tools, t => Name(t) == "describe_app");
        Assert.Equal(2, tools.Count); // exactly the two built-ins, nothing appended
    }
}
