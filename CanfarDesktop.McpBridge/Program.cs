using System.IO.Pipes;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Bridge;
using CanfarDesktop.Mcp.Transport;

// ─────────────────────────────────────────────────────────────────────────────
// The MCP stdio ↔ named-pipe bridge that Claude Desktop (or `claude mcp`) launches.
//
// It computes the SAME deterministic per-user pipe name the running app uses (no sidecar /
// AppData handoff — MSIX virtualizes those), dials that pipe, and relays whole JSON-RPC
// documents both ways. It lives exactly as long as the client keeps stdio open: while the app
// isn't running / MCP is off, every request gets a well-formed serviceUnavailable at once, and
// the first request after the app comes back is relayed to it — see BridgeRelay.
// ─────────────────────────────────────────────────────────────────────────────

await using var stdio = OsTransports.ForStdio();

var pipeName = McpPipeName.ForCurrentUser();

await BridgeRelay.RunAsync(stdio, DialAsync);

async Task<IMcpTransport?> DialAsync(CancellationToken ct)
{
    var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    try
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(connectTimeout.Token);

        // Owned: a dropped connection is replaced by a new one, so each must let its handle go.
        return OsTransports.ForPipe(pipe, ownsPipe: true);
    }
    catch
    {
        // App isn't running / MCP disabled.
        await pipe.DisposeAsync();
        return null;
    }
}
