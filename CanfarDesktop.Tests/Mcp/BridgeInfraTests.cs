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
    public void PipeNameForCurrentUser_IsDeterministicAndWellFormed()
    {
        var name = McpPipeName.ForCurrentUser();
        Assert.StartsWith("verbinal-canfar-mcp-", name);
        Assert.Equal("verbinal-canfar-mcp-".Length + 32, name.Length);   // 32 hex suffix
        Assert.Matches("^verbinal-canfar-mcp-[0-9a-f]{32}$", name);
        Assert.Equal(name, McpPipeName.ForCurrentUser());                // stable across calls (no sidecar needed)
    }

    [Fact]
    public void PipeSddl_OwnerOnly_IsProtectedFullAccessForSid()
        => Assert.Equal("D:P(A;;FA;;;S-1-5-21-1-2-3-1001)", McpPipeSddl.OwnerOnly("S-1-5-21-1-2-3-1001"));

    // ── Bridge locator: stable-path rule for packaged installs ────────────────

    [Fact]
    public void RequiresStableCopy_TrueOnlyForWindowsAppsPaths()
    {
        Assert.True(CanfarDesktop.Mcp.Config.McpBridgeLocator.RequiresStableCopy(
            @"C:\Program Files\WindowsApps\CodeBG.Verbinal_1.3.0.0_x64__abc\mcp-bridge\CanfarDesktop.McpBridge.exe"));
        Assert.True(CanfarDesktop.Mcp.Config.McpBridgeLocator.RequiresStableCopy(
            @"C:\Program Files\windowsapps\pkg\CanfarDesktop.McpBridge.exe")); // case-insensitive

        Assert.False(CanfarDesktop.Mcp.Config.McpBridgeLocator.RequiresStableCopy(
            @"C:\Users\u\source\repos\CanfarDesktop\CanfarDesktop.McpBridge\bin\Debug\net8.0-windows\CanfarDesktop.McpBridge.exe"));
        Assert.False(CanfarDesktop.Mcp.Config.McpBridgeLocator.RequiresStableCopy(
            @"C:\Users\u\WindowsAppsX\CanfarDesktop.McpBridge.exe")); // no separator-delimited match
    }

    [Fact]
    public void ResolveStable_DevTreePath_ReturnsSourceUnchanged()
    {
        // A dev-tree bridge (not under WindowsApps) must be registered directly, not copied.
        var dir = Path.Combine(Path.GetTempPath(), "vb-bridge-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, CanfarDesktop.Mcp.Config.McpBridgeLocator.BridgeExeName);
            File.WriteAllText(exe, "stub");
            Assert.Equal(exe, CanfarDesktop.Mcp.Config.McpBridgeLocator.ResolveStable(dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
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
    public void ServiceUnavailableFor_ToleratesLeadingBom()
    {
        // A client may newline-frame a message that carries a UTF-8 BOM; we must still answer it.
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(@"{""jsonrpc"":""2.0"",""id"":9,""method"":""ping""}")).ToArray();
        var bytes = BridgeRelay.ServiceUnavailableFor(withBom)!;
        var resp = (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(bytes));
        Assert.Equal(9, ((JsonInt)resp["id"]!).Value);
    }

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
