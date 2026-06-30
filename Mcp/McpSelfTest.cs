using System.IO.Pipes;
using System.Text;
using CanfarDesktop.Mcp.Transport;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Drives a real client round-trip against the running named-pipe MCP server — dial the per-user pipe,
/// <c>initialize</c>, <c>tools/list</c> — so the connection wizard's Verify step proves "an agent can
/// actually connect", not just "the toggle is on". Uses the SAME pipe name + transport framing the bridge
/// uses (<see cref="OsTransports.ForPipe"/>, ndjson), so a pass here means a real client passes too.
/// </summary>
public static class McpSelfTest
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(4);

    public static async Task<McpSelfTestResult> RunAsync(CancellationToken ct = default)
    {
        var pipe = new NamedPipeClientStream(".", McpPipeName.ForCurrentUser(), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);
            await pipe.ConnectAsync(connectCts.Token);
        }
        catch
        {
            await pipe.DisposeAsync();
            return McpSelfTestResult.Unreachable("Couldn't reach the MCP server. Make sure it's enabled (Step 1) and try again.");
        }

        await using var transport = OsTransports.ForPipe(pipe);
        try
        {
            await transport.SendAsync(McpSelfTestProtocol.BuildRequest(1, "initialize", McpSelfTestProtocol.InitializeParams), ct);
            var initResp = await ReadOneAsync(transport, ct);
            if (initResp is null)
                return new McpSelfTestResult(false, null, null, "The server accepted the connection but didn't answer initialize.");

            var serverName = McpSelfTestProtocol.ParseServerName(initResp);

            await transport.SendAsync(McpSelfTestProtocol.BuildNotification("notifications/initialized"), ct);
            await transport.SendAsync(McpSelfTestProtocol.BuildRequest(2, "tools/list", "{}"), ct);
            var toolsResp = await ReadOneAsync(transport, ct);

            return new McpSelfTestResult(true, toolsResp is null ? null : McpSelfTestProtocol.ParseToolCount(toolsResp), serverName, null);
        }
        catch (Exception ex)
        {
            return new McpSelfTestResult(false, null, null, $"The connection failed mid-handshake: {ex.Message}");
        }
    }

    /// <summary>Read the next complete document the server sends, bounded by <see cref="ReadTimeout"/>.</summary>
    private static async Task<string?> ReadOneAsync(IMcpTransport transport, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadTimeout);
        try
        {
            while (await transport.Incoming.WaitToReadAsync(cts.Token))
                if (transport.Incoming.TryRead(out var bytes))
                    return Encoding.UTF8.GetString(bytes);
        }
        catch (OperationCanceledException) { }
        return null;
    }
}
