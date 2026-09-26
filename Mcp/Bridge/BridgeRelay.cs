using System.Collections.Concurrent;
using System.Text;
using CanfarDesktop.Mcp.Transport;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Bridge;

/// <summary>
/// Pure relay logic for the console bridge exe: pump complete documents between the MCP client's stdio
/// and the running app's named pipe, for as long as the CLIENT is there. Operates only on
/// <see cref="IMcpTransport"/> so it's testable with InMemoryTransport.
///
/// <para>The client owns this process and never restarts it — Claude Desktop and Claude Code both
/// treat a stdio server that exits as failed until the person reconnects it by hand. So the app
/// closing is not a reason to stop. It used to be a reason to hang: the relay shut down, waited on a
/// stdin read that cannot be cancelled, and sat there answering nothing and holding its exe locked.
/// Now the app coming and going is just a connection that comes and goes: while it is away every
/// request gets a serviceUnavailable answer at once, and the next request after it comes back is
/// relayed to it, with the client's initialize replayed first so the new app instance knows the
/// client. The assistant carries on as if nothing had happened.</para>
/// </summary>
public static class BridgeRelay
{
    /// <summary>
    /// How long a replayed initialize may take. Long, because the app's approval gate can be waiting
    /// on the person; bounded, because a request is held until it answers.
    /// </summary>
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(30);

    private const string NotRunning =
        "Verbinal is not running (or MCP is disabled). Start the app, enable MCP, then retry.";

    private const string ClosedMidRequest =
        "Verbinal closed before answering. Start it again, then retry.";

    /// <summary>
    /// Relay until the client closes stdio. <paramref name="dial"/> connects to the app, or answers
    /// null when it is not there; it is called whenever a request arrives with no connection.
    /// </summary>
    public static Task RunAsync(
        IMcpTransport stdio,
        Func<CancellationToken, Task<IMcpTransport?>> dial,
        TimeSpan? handshakeTimeout = null,
        CancellationToken cancellationToken = default)
        => new Session(stdio, dial, handshakeTimeout ?? DefaultHandshakeTimeout).RunAsync(cancellationToken);

    /// <summary>Build a serviceUnavailable response for a request document, or null for a notification/garbage.</summary>
    public static byte[]? ServiceUnavailableFor(byte[] requestBytes, string? message = null)
    {
        try
        {
            var request = JsonRpcRequest.Parse(JsonValue.Parse((ReadOnlySpan<byte>)requestBytes));
            return request.IsNotification ? null : Failure(request.Id, message ?? NotRunning);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Failure(JsonRpcId id, string message)
        => Encoding.UTF8.GetBytes(JsonRpcResponse.Failure(id,
            new JsonRpcErrorPayload(JsonRpcErrorCode.ServiceUnavailable, message)).ToJsonString());

    private static JsonRpcRequest? RequestIn(byte[] doc)
    {
        try { return JsonRpcRequest.Parse(JsonValue.Parse((ReadOnlySpan<byte>)doc)); }
        catch { return null; }
    }

    /// <summary>The id a response answers and its error message if it failed; null for anything else.</summary>
    private static (JsonRpcId Id, string? Error)? ResponseIn(byte[] doc)
    {
        try
        {
            if (JsonValue.Parse((ReadOnlySpan<byte>)doc) is not JsonObject obj
                || obj.Members.ContainsKey("method")
                || !obj.Members.TryGetValue("id", out var id)) return null;

            var error = obj.Members.GetValueOrDefault("error") is JsonObject e
                ? (e.Members.GetValueOrDefault("message") as JsonString)?.Value ?? "error"
                : null;
            return (JsonRpcId.FromJson(id), error);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>One connection to the app, and the requests sent on it still waiting for an answer.</summary>
    private sealed class Connection(IMcpTransport transport)
    {
        public IMcpTransport Transport { get; } = transport;
        public ConcurrentDictionary<JsonRpcId, byte> Outstanding { get; } = new();
        public Task Pump { get; set; } = Task.CompletedTask;
    }

    private sealed class Session(
        IMcpTransport stdio, Func<CancellationToken, Task<IMcpTransport?>> dial, TimeSpan handshakeTimeout)
    {
        private static readonly byte[] InitializedNotification =
            Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        private Connection? _app;
        private byte[]? _initialize;
        private bool _initializedNotified;
        private int _replays;

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var doc in stdio.Incoming.ReadAllAsync(ct))
                {
                    if (doc.Length == 0) continue; // keep-alive

                    var request = RequestIn(doc);
                    if (request is { Method: "initialize", IsNotification: false })
                    {
                        _initialize = doc;
                        _initializedNotified = false;
                    }
                    else if (request is { Method: "notifications/initialized", IsNotification: true })
                    {
                        _initializedNotified = true;
                    }

                    if (await ForwardAsync(doc, request, ct) is { } refusal
                        && ServiceUnavailableFor(doc, refusal) is { } answer)
                        await stdio.SendAsync(answer, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutting down
            }
            finally
            {
                if (Interlocked.Exchange(ref _app, null) is { } app)
                {
                    await app.Transport.CloseAsync();
                    try { await app.Pump; } catch { /* the client is gone */ }
                }
            }
        }

        /// <summary>Send it to the app; null when sent, otherwise why it could not be.</summary>
        private async Task<string?> ForwardAsync(byte[] doc, JsonRpcRequest? request, CancellationToken ct)
        {
            // Twice at most: a send is how a connection that died while idle is found out, and the
            // request that found it deserves a fresh connection rather than the error.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var app = Volatile.Read(ref _app);
                if (app is null)
                {
                    var (connected, refusal) = await ConnectAsync(replay: request?.Method != "initialize", ct);
                    if (connected is null) return refusal;
                    app = connected;
                }

                JsonRpcId? tracked = request is { IsNotification: false } ? request.Id : null;
                if (tracked is { } id) app.Outstanding[id] = 0; // before sending: the answer can be quick

                try
                {
                    await app.Transport.SendAsync(doc, ct);
                    return null;
                }
                catch when (!ct.IsCancellationRequested)
                {
                    // Already answered by the drop means already answered: sending it again would
                    // give the client two replies to one request.
                    if (tracked is { } sent && !app.Outstanding.TryRemove(sent, out _)) return null;
                    await DropAsync(app);
                }
            }

            return NotRunning;
        }

        private async Task<(Connection? App, string? Refusal)> ConnectAsync(bool replay, CancellationToken ct)
        {
            IMcpTransport? transport;
            try { transport = await dial(ct); }
            catch when (!ct.IsCancellationRequested) { transport = null; }
            if (transport is null) return (null, NotRunning);

            if (replay && _initialize is not null && await ReplayAsync(transport, ct) is { } refused)
            {
                await transport.CloseAsync();
                return (null, refused);
            }

            var app = new Connection(transport);
            Volatile.Write(ref _app, app);
            app.Pump = PumpAsync(app);
            return (app, null);
        }

        /// <summary>
        /// Introduce the client to a new app instance with the initialize it sent the last one. Under an
        /// id of the bridge's own, so its answer can be told apart and kept from the client, which
        /// already had one. Null when the app accepted it, otherwise why not.
        /// </summary>
        private async Task<string?> ReplayAsync(IMcpTransport transport, CancellationToken ct)
        {
            var replayId = JsonRpcId.FromString($"verbinal-bridge-reconnect-{++_replays}");
            var original = (JsonObject)JsonValue.Parse((ReadOnlySpan<byte>)_initialize!);
            var members = new Dictionary<string, JsonValue>(original.Members) { ["id"] = replayId.ToJson() };
            var replayed = Encoding.UTF8.GetBytes(new JsonObject(members).ToJsonString());

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(handshakeTimeout);
            try
            {
                await transport.SendAsync(replayed, timeout.Token);

                // Read directly, before any pump starts: nothing else is on a fresh connection, and
                // no request may go ahead of this answer — the app refuses tools until it has it.
                await foreach (var reply in transport.Incoming.ReadAllAsync(timeout.Token))
                {
                    if (ResponseIn(reply) is not { } answer || !answer.Id.Equals(replayId)) continue;
                    if (answer.Error is { } error) return $"Verbinal did not accept the reconnect: {error}";

                    if (_initializedNotified) await transport.SendAsync(InitializedNotification, timeout.Token);
                    return null;
                }

                return NotRunning; // closed during the handshake
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return "Verbinal did not answer the reconnect in time. Check it is not waiting on you, then retry.";
            }
            catch when (!ct.IsCancellationRequested)
            {
                return NotRunning;
            }
        }

        /// <summary>App → client, until the app goes.</summary>
        private async Task PumpAsync(Connection app)
        {
            try
            {
                await foreach (var doc in app.Transport.Incoming.ReadAllAsync())
                {
                    if (ResponseIn(doc) is { } answer) app.Outstanding.TryRemove(answer.Id, out _);
                    await stdio.SendAsync(doc, CancellationToken.None);
                }
            }
            catch
            {
                // The client side is gone; the main loop ends on its own.
            }
            finally
            {
                await DropAsync(app);
            }
        }

        /// <summary>
        /// Let a connection go, and answer what was still waiting on it — otherwise the client waits
        /// out its own timeout for replies that are never coming.
        /// </summary>
        private async Task DropAsync(Connection app)
        {
            Interlocked.CompareExchange(ref _app, null, app);
            await app.Transport.CloseAsync();

            foreach (var id in app.Outstanding.Keys)
            {
                if (!app.Outstanding.TryRemove(id, out _)) continue;
                try { await stdio.SendAsync(Failure(id, ClosedMidRequest), CancellationToken.None); }
                catch { return; } // the client is gone too
            }
        }
    }
}
