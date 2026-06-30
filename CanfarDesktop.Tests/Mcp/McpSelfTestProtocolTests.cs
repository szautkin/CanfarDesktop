using System.Text;
using Xunit;
using CanfarDesktop.Mcp;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>The pure wire shape of the connection wizard's Verify self-test (request building + response parsing).</summary>
public class McpSelfTestProtocolTests
{
    [Fact]
    public void BuildRequest_IsWellFormedJsonRpc()
    {
        var json = Encoding.UTF8.GetString(McpSelfTestProtocol.BuildRequest(7, "tools/list", "{}"));
        Assert.Equal("""{"jsonrpc":"2.0","id":7,"method":"tools/list","params":{}}""", json);
    }

    [Fact]
    public void BuildRequest_InlinesInitializeParams()
    {
        var json = Encoding.UTF8.GetString(McpSelfTestProtocol.BuildRequest(1, "initialize", McpSelfTestProtocol.InitializeParams));
        using var doc = System.Text.Json.JsonDocument.Parse(json); // valid JSON
        Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal("2024-11-05", doc.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void BuildNotification_HasNoId()
    {
        var json = Encoding.UTF8.GetString(McpSelfTestProtocol.BuildNotification("notifications/initialized"));
        Assert.Equal("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", json);
        Assert.DoesNotContain("\"id\"", json);
    }

    [Fact]
    public void ParseToolCount_CountsToolsArray()
    {
        Assert.Equal(3, McpSelfTestProtocol.ParseToolCount("""{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"a"},{"name":"b"},{"name":"c"}]}}"""));
        Assert.Equal(0, McpSelfTestProtocol.ParseToolCount("""{"result":{"tools":[]}}"""));
    }

    [Theory]
    [InlineData("""{"result":{}}""")]            // no tools
    [InlineData("""{"error":{"code":-32000}}""")] // error response
    [InlineData("not json")]                       // garbage
    public void ParseToolCount_UnexpectedShape_ReturnsNull(string json)
        => Assert.Null(McpSelfTestProtocol.ParseToolCount(json));

    [Fact]
    public void ParseServerName_ReadsServerInfoName()
    {
        Assert.Equal("verbinal-canfar",
            McpSelfTestProtocol.ParseServerName("""{"result":{"serverInfo":{"name":"verbinal-canfar","version":"1.0"}}}"""));
        Assert.Null(McpSelfTestProtocol.ParseServerName("""{"result":{}}"""));
        Assert.Null(McpSelfTestProtocol.ParseServerName("garbage"));
    }
}
