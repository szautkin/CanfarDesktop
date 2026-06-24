using Xunit;
using CanfarDesktop.Mcp.Config;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ClaudeConfigMergeTests
{
    private static JsonObject Servers(string json)
        => (JsonObject)((JsonObject)JsonValue.Parse(json))["mcpServers"]!;

    [Fact]
    public void FromEmpty_AddsVerbinalServer()
    {
        var json = ClaudeConfigMerge.MergedRoot(null, @"C:\app\bridge.exe");
        var entry = (JsonObject)Servers(json)["verbinal-canfar"]!;
        Assert.Equal(@"C:\app\bridge.exe", ((JsonString)entry["command"]!).Value);
        Assert.Equal("mcp", ((JsonString)((JsonArray)entry["args"]!).Items[0]).Value);
    }

    [Fact]
    public void PreservesSiblingServers_AndOtherTopLevelKeys()
    {
        const string existing = @"{""mcpServers"":{""other"":{""command"":""x.exe""}},""globalShortcut"":""Ctrl+Space""}";
        var json = ClaudeConfigMerge.MergedRoot(existing, "bridge.exe");

        var servers = Servers(json);
        Assert.NotNull(servers["other"]);          // sibling server preserved
        Assert.NotNull(servers["verbinal-canfar"]); // ours added
        Assert.Equal("Ctrl+Space", ((JsonString)((JsonObject)JsonValue.Parse(json))["globalShortcut"]!).Value);
    }

    [Fact]
    public void UpdatesExistingVerbinalEntry()
    {
        const string existing = @"{""mcpServers"":{""verbinal-canfar"":{""command"":""old.exe"",""args"":[""stale""]}}}";
        var entry = (JsonObject)Servers(ClaudeConfigMerge.MergedRoot(existing, "new.exe"))["verbinal-canfar"]!;
        Assert.Equal("new.exe", ((JsonString)entry["command"]!).Value);
        Assert.Equal("mcp", ((JsonString)((JsonArray)entry["args"]!).Items[0]).Value); // reset to default args
    }

    [Fact]
    public void UnparseableExisting_StartsFresh()
    {
        var json = ClaudeConfigMerge.MergedRoot("{ totally broken", "bridge.exe");
        Assert.NotNull(Servers(json)["verbinal-canfar"]);
    }

    [Fact]
    public void ClaudeCodeAddCommand_IncludesKeyAndPath()
    {
        var cmd = ClaudeConfigMerge.ClaudeCodeAddCommand(@"C:\app\bridge.exe");
        Assert.Contains("claude mcp add", cmd);
        Assert.Contains("verbinal-canfar", cmd);
        Assert.Contains(@"C:\app\bridge.exe", cmd);
        Assert.EndsWith("mcp", cmd);
    }
}
