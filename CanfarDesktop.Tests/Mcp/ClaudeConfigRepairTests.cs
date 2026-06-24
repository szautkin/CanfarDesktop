using System.Text.Json.Nodes;
using Xunit;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Tests.Mcp;

public class ClaudeConfigRepairTests
{
    private static string TempConfig()
        => Path.Combine(Path.GetTempPath(), "claude-cfg-" + Guid.NewGuid().ToString("N"), "claude_desktop_config.json");

    [Fact]
    public void Apply_FreshConfig_WritesServerEntry()
    {
        var repair = new ClaudeConfigRepair(TempConfig());

        repair.Apply(@"C:\app\CanfarDesktop.McpBridge.exe");

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(repair.ConfigPath))!;
        var server = (JsonObject)root["mcpServers"]![ClaudeConfigMerge.ServerKey]!;
        Assert.Equal(@"C:\app\CanfarDesktop.McpBridge.exe", (string)server["command"]!);
        Assert.Equal("mcp", (string)((JsonArray)server["args"]!)[0]!);
    }

    [Fact]
    public void Apply_PreservesOtherServers_AndKeepsBak()
    {
        var path = TempConfig();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"mcpServers":{"other":{"command":"other.exe"}},"theme":"dark"}""");

        var repair = new ClaudeConfigRepair(path);
        repair.Apply("bridge.exe");

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var servers = (JsonObject)root["mcpServers"]!;
        Assert.True(servers.ContainsKey("other"));                 // sibling preserved
        Assert.True(servers.ContainsKey(ClaudeConfigMerge.ServerKey)); // ours added
        Assert.Equal("dark", (string)root["theme"]!);              // top-level key preserved
        Assert.True(File.Exists(path + ".bak"));                   // prior file backed up
    }

    [Fact]
    public void ReadExisting_AbsentFile_IsNull()
        => Assert.Null(new ClaudeConfigRepair(TempConfig()).ReadExisting());
}
