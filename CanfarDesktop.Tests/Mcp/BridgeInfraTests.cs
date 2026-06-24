using System.Text;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Bridge;
using CanfarDesktop.Mcp.Listener;
using CanfarDesktop.Mcp.Transport;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class BridgeInfraTests
{
    private static async Task<string> Read(InMemoryTransport t)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return (await t.ReadResponseAsync(cts.Token))!;
    }

    // ── Constants / sidecar ───────────────────────────────────────────────────

    [Fact]
    public void NewPipeName_IsPrefixedAndUnguessable()
    {
        var name = McpConstants.NewPipeName(Guid.Empty);
        Assert.StartsWith("verbinal-canfar-mcp-", name);
    }

    [Fact]
    public void Sidecar_WriteReadDelete_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sidecar-" + Guid.NewGuid().ToString("N"));
        var sidecar = new McpSidecar(dir);

        Assert.Null(sidecar.Read());
        sidecar.Write("verbinal-canfar-mcp-abc");
        Assert.Equal("verbinal-canfar-mcp-abc", sidecar.Read());
        sidecar.Write("verbinal-canfar-mcp-def"); // atomic overwrite
        Assert.Equal("verbinal-canfar-mcp-def", sidecar.Read());
        sidecar.Delete();
        Assert.Null(sidecar.Read());
    }

    // ── Bridge relay ──────────────────────────────────────────────────────────

    [Fact]
    public void ServiceUnavailableFor_Request_HasCodeAndId()
    {
        var bytes = BridgeRelay.ServiceUnavailableFor(Encoding.UTF8.GetBytes(@"{""jsonrpc"":""2.0"",""id"":7,""method"":""ping""}"))!;
        var resp = (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(bytes));
        Assert.Equal(7, ((JsonInt)resp["id"]!).Value);
        Assert.Equal(-32000, ((JsonInt)((JsonObject)resp["error"]!)["code"]!).Value);
    }

    [Fact]
    public void ServiceUnavailableFor_Notification_IsNull()
        => Assert.Null(BridgeRelay.ServiceUnavailableFor(Encoding.UTF8.GetBytes(@"{""jsonrpc"":""2.0"",""method"":""x""}")));

    [Fact]
    public async Task Relay_PumpsBothDirections()
    {
        var stdio = new InMemoryTransport();
        var pipe = new InMemoryTransport();
        var relay = BridgeRelay.RelayAsync(stdio, pipe);

        stdio.Inject("request-doc");
        Assert.Equal("request-doc", await Read(pipe)); // stdio -> pipe

        pipe.Inject("response-doc");
        Assert.Equal("response-doc", await Read(stdio)); // pipe -> stdio

        stdio.CompleteIncoming();
        pipe.CompleteIncoming();
        await relay;
    }

    [Fact]
    public async Task DrainAndFail_RepliesServiceUnavailable()
    {
        var stdio = new InMemoryTransport();
        var drain = BridgeRelay.DrainAndFailAsync(stdio);

        stdio.Inject(@"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/list""}");
        var resp = (JsonObject)JsonValue.Parse(await Read(stdio));
        Assert.Equal(-32000, ((JsonInt)((JsonObject)resp["error"]!)["code"]!).Value);

        stdio.CompleteIncoming();
        await drain;
    }
}
