using System.IO.Pipes;
using CanfarDesktop.Mcp.Bridge;
using CanfarDesktop.Mcp.Listener;
using CanfarDesktop.Mcp.Transport;

// ─────────────────────────────────────────────────────────────────────────────
// The MCP stdio ↔ named-pipe bridge that Claude Desktop (or `claude mcp`) launches.
//
// It reads the live pipe name the running, MCP-enabled app advertised via the sidecar,
// dials that pipe, and relays whole JSON-RPC documents both ways. If the app isn't running
// (no sidecar) or the pipe can't be reached, it answers every request with a well-formed
// serviceUnavailable so the client gets a clean, explanatory error instead of a hang or a
// broken pipe. Mirrors the macOS launch-agent bridge.
// ─────────────────────────────────────────────────────────────────────────────

await using var stdio = OsTransports.ForStdio();

var pipeName = new McpSidecar().Read();
if (pipeName is null)
{
    await BridgeRelay.DrainAndFailAsync(stdio);
    return;
}

try
{
    var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        await pipe.ConnectAsync(connectTimeout.Token);

    await using var pipeTransport = OsTransports.ForPipe(pipe);
    await BridgeRelay.RelayAsync(stdio, pipeTransport);
}
catch
{
    // App went away between reading the sidecar and connecting, or the pipe died mid-session.
    await BridgeRelay.DrainAndFailAsync(stdio);
}
