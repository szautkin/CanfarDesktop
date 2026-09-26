using System.Collections.Concurrent;
using System.Text;
using CanfarDesktop.Mcp.Transport;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Bridge;

/// <summary>How the bridge relays, and what it keeps to answer for the app while the app is away.</summary>
public sealed record BridgeOptions
{
    /// <summary>
    /// How long a replayed initialize may take. Long, because the app's approval gate can be waiting
    /// on the person; bounded, because a request is held until it answers.
    /// </summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to look for the app while the client's tools are the bridge's stand-in.</summary>
    public TimeSpan WatchInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>What the app last said about itself; in memory only unless given a file.</summary>
    public AppSnapshot Snapshot { get; init; } = new();
}

/// <summary>
/// Pure relay logic for the console bridge exe: pump complete documents between the MCP client's stdio
/// and the running app's named pipe, for as long as the CLIENT is there. Operates only on
/// <see cref="IMcpTransport"/> so it's testable with InMemoryTransport.
///
/// <para>The client owns this process and never restarts it — Claude Desktop and Claude Code both
/// treat a stdio server that exits as failed until the person reconnects it by hand. So the app
/// closing is not a reason to stop. It used to be a reason to hang: the relay shut down, waited on a
/// stdin read that cannot be cancelled, and sat there answering nothing and holding its exe locked.
/// Now the app coming and going is just a connection that comes and goes. While it is away the bridge
/// answers for it (<see cref="BridgeStandIn"/>), the next request after it comes back is relayed to
/// it, with the client's initialize replayed first so the new app instance knows the client, and the
/// assistant carries on as if nothing had happened.</para>
///
/// <para>A client can also start before the app. Its initialize used to be refused, and clients give
/// up on a server whose initialize fails. Now the bridge answers it, with the tools the app listed
/// last time, looks for the app every few seconds, and once it is there tells the client its tool
/// list has changed.</para>
/// </summary>
public static class BridgeRelay
{
    private static readonly Refusal Away = new(BridgeStandIn.NotRunning, AppIsThere: false);

    private static readonly Refusal ClosedMidRequest =
        new("Verbinal closed before answering. Ask the person to start it again, then try again.", AppIsThere: false);

    /// <summary>
    /// Relay until the client closes stdio. <paramref name="dial"/> connects to the app, or answers
    /// null when it is not there; it is called whenever a request arrives with no connection, and
    /// every so often while the bridge is standing in for the app.
    /// </summary>
    public static Task RunAsync(
        IMcpTransport stdio,
        Func<CancellationToken, Task<IMcpTransport?>> dial,
        BridgeOptions? options = null,
        CancellationToken cancellationToken = default)
        => new Session(stdio, dial, options ?? new BridgeOptions()).RunAsync(cancellationToken);

    private static byte[] Encode(JsonRpcResponse response) => Encoding.UTF8.GetBytes(response.ToJsonString());

    private static JsonRpcRequest? RequestIn(byte[] doc)
    {
        try { return JsonRpcRequest.Parse(JsonValue.Parse((ReadOnlySpan<byte>)doc)); }
        catch { return null; }
    }

    /// <summary>The id a response answers, its error message if it failed, and its result; null for anything else.</summary>
    private static (JsonRpcId Id, string? Error, JsonValue? Result)? ResponseIn(byte[] doc)
    {
        try
        {
            if (JsonValue.Parse((ReadOnlySpan<byte>)doc) is not JsonObject obj
                || obj.Members.ContainsKey("method")
                || !obj.Members.TryGetValue("id", out var id)) return null;

            var error = obj.Members.GetValueOrDefault("error") is JsonObject e
                ? (e.Members.GetValueOrDefault("message") as JsonString)?.Value ?? "error"
                : null;
            return (JsonRpcId.FromJson(id), error, obj.Members.GetValueOrDefault("result"));
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
        public ConcurrentDictionary<JsonRpcId, JsonRpcRequest> Outstanding { get; } = new();
        public Task Pump { get; set; } = Task.CompletedTask;
    }

    /// <summary>Why the app could not be reached, and whether it was there to say no.</summary>
    private sealed record Refusal(string Why, bool AppIsThere);

    private sealed class Session(
        IMcpTransport stdio, Func<CancellationToken, Task<IMcpTransport?>> dial, BridgeOptions options)
    {
        private static readonly byte[] InitializedNotification =
            Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        private static readonly byte[] ToolsChanged =
            Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/tools/list_changed"}""");

        private readonly SemaphoreSlim _connecting = new(1, 1);
        private readonly object _watchGate = new();
        private readonly CancellationTokenSource _life = new();
        private Connection? _app;
        private byte[]? _initialize;
        private bool _initializedNotified;
        private int _replays;
        private int _standingIn; // 1 while what the client knows of the tools came from the bridge
        private Task _watch = Task.CompletedTask;

        public async Task RunAsync(CancellationToken ct)
        {
            await using var stop = ct.Register(_life.Cancel);
            try
            {
                await foreach (var doc in stdio.Incoming.ReadAllAsync(ct))
                {
                    if (doc.Length == 0) continue; // keep-alive

                    var request = RequestIn(doc);
                    if (request is { Method: "initialize", IsNotification: false })
                    {
                        Volatile.Write(ref _initialize, doc);
                        _initializedNotified = false;
                    }
                    else if (request is { Method: "notifications/initialized", IsNotification: true })
                    {
                        _initializedNotified = true;
                    }

                    if (await ForwardAsync(doc, request, ct) is { } refusal && request is { IsNotification: false })
                        await StandInAsync(request, refusal, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutting down
            }
            finally
            {
                _life.Cancel(); // no watch starts after this
                Task watch;
                lock (_watchGate) watch = _watch;
                try { await watch; } catch { /* stopped */ }
                if (Interlocked.Exchange(ref _app, null) is { } app)
                {
                    await app.Transport.CloseAsync();
                    try { await app.Pump; } catch { /* the client is gone */ }
                }
            }
        }

        /// <summary>Send it to the app; null when sent, otherwise why it could not be.</summary>
        private async Task<Refusal?> ForwardAsync(byte[] doc, JsonRpcRequest? request, CancellationToken ct)
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

                var tracked = request is { IsNotification: false } ? request : null;
                if (tracked is not null) app.Outstanding[tracked.Id] = tracked; // before sending: the answer can be quick

                try
                {
                    await app.Transport.SendAsync(doc, ct);
                    return null;
                }
                catch when (!ct.IsCancellationRequested)
                {
                    // Already answered by the drop means already answered: sending it again would
                    // give the client two replies to one request.
                    if (tracked is not null && !app.Outstanding.TryRemove(tracked.Id, out _)) return null;
                    await DropAsync(app);
                }
            }

            return Away;
        }

        /// <summary>
        /// Answer for the app. When the answer tells the client what the tools are, they are the bridge's
        /// word rather than the app's: the client is told when that changes, and while the app is away
        /// the bridge looks for it — not when it is there and said no, which looking would only ask again.
        /// </summary>
        private async Task StandInAsync(JsonRpcRequest request, Refusal refusal, CancellationToken ct)
        {
            var answer = BridgeStandIn.Answer(request, refusal.Why, options.Snapshot);
            var lists = answer.Error is null && BridgeStandIn.Lists(request.Method);
            // Before the answer goes: a connection made meanwhile must know to tell the client.
            if (lists) Volatile.Write(ref _standingIn, 1);
            await stdio.SendAsync(Encode(answer), ct);
            if (!lists || refusal.AppIsThere) return;

            lock (_watchGate)
            {
                if (_watch.IsCompleted && !_life.IsCancellationRequested) _watch = WatchAsync(_life.Token);
            }
        }

        /// <summary>
        /// Look for the app every so often while the bridge stands in for it, so the client hears that
        /// the tools have changed when the app starts, not only when it next asks for something. Stops
        /// once connected — by this or by a request — and when the app is there but says no: asking
        /// again would put its approval prompt in front of the person every few seconds.
        /// </summary>
        private async Task WatchAsync(CancellationToken ct)
        {
            try
            {
                while (Volatile.Read(ref _standingIn) == 1 && Volatile.Read(ref _app) is null
                       && Volatile.Read(ref _initialize) is not null)
                {
                    await Task.Delay(options.WatchInterval, ct);
                    var (app, refusal) = await ConnectAsync(replay: true, ct);
                    if (app is not null || refusal!.AppIsThere) return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // the client has gone
            }
        }

        /// <summary>
        /// Connect to the app, one at a time: the watch and a request can both want to. Tells the client
        /// its tools have changed when what it knows of them came from the bridge.
        /// </summary>
        private async Task<(Connection? App, Refusal? Refusal)> ConnectAsync(bool replay, CancellationToken ct)
        {
            await _connecting.WaitAsync(ct);
            try
            {
                if (Volatile.Read(ref _app) is { } already) return (already, null);

                IMcpTransport? transport;
                try { transport = await dial(ct); }
                catch when (!ct.IsCancellationRequested) { transport = null; }
                if (transport is null) return (null, Away);

                if (replay && Volatile.Read(ref _initialize) is not null && await ReplayAsync(transport, ct) is { } refused)
                {
                    await transport.CloseAsync();
                    return (null, refused);
                }

                var app = new Connection(transport);
                Volatile.Write(ref _app, app);
                app.Pump = PumpAsync(app);

                if (Interlocked.Exchange(ref _standingIn, 0) == 1)
                {
                    try { await stdio.SendAsync(ToolsChanged, ct); }
                    catch when (!ct.IsCancellationRequested) { /* the client is gone; the main loop ends on its own */ }
                }
                return (app, null);
            }
            finally
            {
                _connecting.Release();
            }
        }

        /// <summary>
        /// Introduce the client to a new app instance with the initialize it sent the last one. Under an
        /// id of the bridge's own, so its answer can be told apart and kept from the client, which
        /// already had one. Null when the app accepted it, otherwise why not.
        /// </summary>
        private async Task<Refusal?> ReplayAsync(IMcpTransport transport, CancellationToken ct)
        {
            var replayId = JsonRpcId.FromString($"verbinal-bridge-reconnect-{++_replays}");
            var original = (JsonObject)JsonValue.Parse((ReadOnlySpan<byte>)Volatile.Read(ref _initialize)!);
            var members = new Dictionary<string, JsonValue>(original.Members) { ["id"] = replayId.ToJson() };
            var replayed = Encoding.UTF8.GetBytes(new JsonObject(members).ToJsonString());

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.HandshakeTimeout);
            try
            {
                await transport.SendAsync(replayed, timeout.Token);

                // Read directly, before any pump starts: nothing else is on a fresh connection, and
                // no request may go ahead of this answer — the app refuses tools until it has it.
                await foreach (var reply in transport.Incoming.ReadAllAsync(timeout.Token))
                {
                    if (ResponseIn(reply) is not { } answer || !answer.Id.Equals(replayId)) continue;
                    if (answer.Error is { } error) return new Refusal($"Verbinal did not accept the reconnect: {error}", AppIsThere: true);

                    Learn("initialize", null, answer.Result);
                    if (_initializedNotified) await transport.SendAsync(InitializedNotification, timeout.Token);
                    return null;
                }

                return Away; // closed during the handshake
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new Refusal(
                    "Verbinal did not answer the reconnect in time. Ask the person to check it is not waiting on them, then try again.",
                    AppIsThere: true);
            }
            catch when (!ct.IsCancellationRequested)
            {
                return Away;
            }
        }

        /// <summary>App → client, until the app goes.</summary>
        private async Task PumpAsync(Connection app)
        {
            try
            {
                await foreach (var doc in app.Transport.Incoming.ReadAllAsync())
                {
                    if (ResponseIn(doc) is { } answer && app.Outstanding.TryRemove(answer.Id, out var asked))
                        Learn(asked.Method, asked.Params, answer.Result);
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

        /// <summary>Remember what the app said about itself, to answer for it the next time it is away.</summary>
        private void Learn(string method, JsonValue? asked, JsonValue? result)
        {
            if (method == "initialize" && result?["serverInfo"] is JsonObject info)
                options.Snapshot.Remember(AppSnapshot.ServerInfoKey, info);
            // The whole list only: a page of it is not what the app has.
            else if (method == "tools/list" && result is JsonObject { } list && list["tools"] is JsonArray
                     && list["nextCursor"] is null && asked?["cursor"] is null)
                options.Snapshot.Remember(AppSnapshot.ToolsKey, list);
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
                if (!app.Outstanding.TryRemove(id, out var asked)) continue;
                try { await StandInAsync(asked, ClosedMidRequest, CancellationToken.None); }
                catch { return; } // the client is gone too
            }
        }
    }
}
