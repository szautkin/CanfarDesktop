using System.IO.Pipes;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Bridge;
using CanfarDesktop.Mcp.Transport;

// ─────────────────────────────────────────────────────────────────────────────
// The MCP stdio ↔ named-pipe bridge that Claude Desktop (or `claude mcp`) launches.
//
// It computes the SAME deterministic per-user pipe name the running app uses (no sidecar /
// AppData handoff — MSIX virtualizes those), dials that pipe, and relays whole JSON-RPC
// documents both ways. If the app isn't running / MCP is off, the dial fails and it answers
// every request with a well-formed serviceUnavailable so the client gets a clean error.
// ─────────────────────────────────────────────────────────────────────────────

await using var stdio = OsTransports.ForStdio();

var pipeName = McpPipeName.ForCurrentUser();

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
    // App isn't running / MCP disabled / pipe died mid-session.
    await BridgeRelay.DrainAndFailAsync(stdio);
}
