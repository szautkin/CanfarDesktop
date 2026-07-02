using System.IO.Pipes;
using CanfarDesktop.Mcp.Transport;

namespace CanfarDesktop.Mcp.Listener;

/// <summary>
/// The in-app named-pipe server. On <see cref="Start"/> it advertises an unguessable per-launch pipe
/// name via the sidecar, then accepts connections on a hardened (owner-only) pipe and serves each one
/// with a fresh <see cref="McpServerService"/> (one instance per connection, per the protocol contract).
/// A new listening instance is created as soon as a connection is accepted, so a client can connect
/// while another is being served. OS-coupled — build-verified; the framing/serve logic it drives is
/// unit-tested elsewhere. 1-to-1 with the macOS in-app listener.
/// </summary>
public sealed class McpListenerService : IAsyncDisposable
{
    // Named-pipe server instances are cheap; a generous cap absorbs several concurrent clients
    // (Claude Desktop + Claude Code + agent bursts) without exhausting the pipe.
    private const int MaxInstances = 16;

    // Back-off before retrying after a transient "all instances busy" so the accept loop self-heals
    // instead of spinning while connections free up.
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(1);

    private readonly Func<McpServerService> _serverFactory;
    private readonly McpSidecar _sidecar;
    private readonly Action<string>? _log;

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public McpListenerService(Func<McpServerService> serverFactory, McpSidecar? sidecar = null, Action<string>? log = null)
    {
        _serverFactory = serverFactory;
        _sidecar = sidecar ?? new McpSidecar();
        _log = log;
    }

    /// <summary>The pipe name currently advertised, or null when not started.</summary>
    public string? PipeName { get; private set; }

    public bool IsRunning => _acceptLoop is { IsCompleted: false };

    /// <summary>Begin listening on a per-launch pipe and write the sidecar. Idempotent.</summary>
    public void Start(Guid launchId)
    {
        if (_acceptLoop is not null) return;
        // Deterministic per-user name (no sidecar handoff) — the bridge computes the same name. The
        // sidecar is still written as a diagnostic / back-compat breadcrumb, but is no longer required.
        PipeName = McpPipeName.ForCurrentUser();
        _sidecar.Write(PipeName);
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(PipeName, _cts.Token));
        _log?.Invoke($"MCP listener started on pipe {PipeName}");
    }

    private async Task AcceptLoopAsync(string pipeName, CancellationToken ct)
    {
        var security = McpPipeSecurity.ForCurrentUser();
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    pipeName, PipeDirection.InOut, MaxInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    inBufferSize: 0, outBufferSize: 0, security);
            }
            catch (IOException ex)
            {
                // "All instances busy" is transient — instances free as clients disconnect. Backing
                // off and retrying keeps the server alive; the old `break` here killed the accept loop
                // permanently, dropping MCP for the rest of the app's lifetime once the cap was hit.
                _log?.Invoke($"MCP listener could not open pipe instance ({ex.Message}); retrying in {BusyRetryDelay.TotalSeconds:0}s");
                try { await Task.Delay(BusyRetryDelay, ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }
            catch (IOException)
            {
                await server.DisposeAsync(); // client gone before we accepted — try again
                continue;
            }

            _ = HandleConnectionAsync(server, ct); // serve concurrently; loop opens the next instance
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            await using var transport = OsTransports.ForPipe(server);
            await _serverFactory().ServeAsync(transport, ct);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"MCP connection ended: {ex.Message}");
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* shutting down */ }
        }
        _sidecar.Delete();
        _cts?.Dispose();
    }
}
