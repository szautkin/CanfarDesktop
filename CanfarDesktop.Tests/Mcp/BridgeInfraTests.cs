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
    public async Task Run_WhileAway_AnswersRequests_NotNotifications_EvenWithALeadingBom()
    {
        var stdio = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial());

        stdio.Inject(Initialized);
        // A client may newline-frame a message that carries a UTF-8 BOM; it must still be answered.
        stdio.Inject("\uFEFF" + Request(9, "ping"));

        Assert.Equal(9, ((JsonInt)Json(await Read(stdio))["id"]!).Value); // the notification had no answer
        Assert.False(stdio.HasPendingResponse);

        stdio.CompleteIncoming();
        await Ends(run);
    }

    private const string Initialize =
        @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2025-06-18"",""clientInfo"":{""name"":""test"",""version"":""1""}}}";

    private const string Initialized = @"{""jsonrpc"":""2.0"",""method"":""notifications/initialized""}";

    private static string Request(int id, string method = "tools/list")
        => $@"{{""jsonrpc"":""2.0"",""id"":{id},""method"":""{method}""}}";

    private static string Answer(string id) => $@"{{""jsonrpc"":""2.0"",""id"":{id},""result"":{{}}}}";

    /// <summary>The app, as the bridge dials it: each call takes the next connection, or null for none.</summary>
    private static Func<CancellationToken, Task<IMcpTransport?>> Dial(params InMemoryTransport?[] connections)
    {
        var queue = new Queue<InMemoryTransport?>(connections);
        return _ => Task.FromResult<IMcpTransport?>(queue.Count > 0 ? queue.Dequeue() : null);
    }

    private static JsonObject Json(string doc) => (JsonObject)JsonValue.Parse(doc);

    private static long ErrorCode(string doc) => ((JsonInt)((JsonObject)Json(doc)["error"]!)["code"]!).Value;

    /// <summary>The text of a tool call's answer, which must be a tool error.</summary>
    private static string ToolError(JsonObject answer)
    {
        var result = answer["result"]!;
        Assert.Equal(new JsonBool(true), result["isError"]);
        return ((JsonString)((JsonArray)result["content"]!).Items[0]["text"]!).Value;
    }

    /// <summary>
    /// The app has gone: its side of the connection ends, and the bridge closes its end in turn. Waiting
    /// for that close is what makes the next request certain to find no connection.
    /// </summary>
    private static async Task AppQuits(InMemoryTransport app)
    {
        app.CompleteIncoming();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await app.ReadResponseAsync(cts.Token) is not null) { }
    }

    private static async Task Ends(Task run)
    {
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))));
        await run;
    }

    [Fact]
    public async Task Run_RelaysBothWays()
    {
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(app));

        stdio.Inject(Initialize);
        Assert.Equal(Initialize, await Read(app)); // the client's own initialize, not a replay

        app.Inject(Answer("1"));
        Assert.Equal(Answer("1"), await Read(stdio));

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_AnswersServiceUnavailable_WhileTheAppIsAway_AndReachesItOnceItStarts()
    {
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(null, app));

        stdio.Inject(Request(3, "prompts/list")); // not one the bridge answers for the app
        var refused = await Read(stdio);
        Assert.Equal(-32000, ErrorCode(refused));
        Assert.Equal(3, ((JsonInt)Json(refused)["id"]!).Value);

        // It used to stay refusing for good once it had failed to connect at startup.
        stdio.Inject(Initialize);
        Assert.Equal(Initialize, await Read(app));

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_Reconnects_WhenTheAppComesBack_ReplayingTheClientsInitialize()
    {
        var stdio = new InMemoryTransport();
        var first = new InMemoryTransport();
        var second = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(first, second));

        stdio.Inject(Initialize);
        await Read(first);
        first.Inject(Answer("1"));
        await Read(stdio);
        stdio.Inject(Initialized);
        await Read(first);

        await AppQuits(first);

        stdio.Inject(Request(5));

        // The new app instance hears initialize first, under the bridge's own id...
        var replay = Json(await Read(second));
        Assert.Equal("initialize", ((JsonString)replay["method"]!).Value);
        Assert.Equal("verbinal-bridge-reconnect-1", ((JsonString)replay["id"]!).Value);
        Assert.Equal(Json(Initialize)["params"]!.ToJsonString(), replay["params"]!.ToJsonString());
        second.Inject(Answer(@"""verbinal-bridge-reconnect-1"""));

        // ...then the notification the client sent, then the request that found it gone.
        Assert.Equal("notifications/initialized", ((JsonString)Json(await Read(second))["method"]!).Value);
        Assert.Equal(Request(5), await Read(second));

        // The client hears only the answer to what it asked; the replay's answer was the bridge's.
        second.Inject(Answer("5"));
        Assert.Equal(Answer("5"), await Read(stdio));
        Assert.False(stdio.HasPendingResponse);

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_PassesOnWhyTheAppRefusedTheReconnect()
    {
        var stdio = new InMemoryTransport();
        var first = new InMemoryTransport();
        var second = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(first, second));

        stdio.Inject(Initialize);
        await Read(first);
        first.Inject(Answer("1"));
        await Read(stdio);
        await AppQuits(first);

        stdio.Inject(Request(6, "tools/call"));
        await Read(second);
        second.Inject(@"{""jsonrpc"":""2.0"",""id"":""verbinal-bridge-reconnect-1"",""error"":{""code"":-32001,""message"":""Client not approved by user.""}}");

        var refused = Json(await Read(stdio));
        Assert.Equal(6, ((JsonInt)refused["id"]!).Value);
        Assert.Contains("Client not approved by user.", ToolError(refused));

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_AnswersWhatWasStillWaiting_WhenTheAppCloses()
    {
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(app));

        stdio.Inject(Initialize);
        await Read(app);
        app.Inject(Answer("1"));
        await Read(stdio);

        stdio.Inject(Request(4, "tools/call"));
        await Read(app);
        app.CompleteIncoming(); // gone without answering 4

        var failed = Json(await Read(stdio));
        Assert.Equal(4, ((JsonInt)failed["id"]!).Value);
        Assert.Contains("closed before answering", ToolError(failed));

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_Ends_AndLetsTheAppGo_WhenTheClientCloses()
    {
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(app));

        stdio.Inject(Initialize);
        await Read(app);

        stdio.CompleteIncoming();
        await Ends(run);
        Assert.Null(await app.ReadResponseAsync()); // its connection was closed, not left open
    }

    // ── Standing in for an app that is not there (D10) ────────────────────────

    /// <summary>The app, as the bridge dials it, counting the calls: each takes the next connection, or none.</summary>
    private sealed class Dialer(params InMemoryTransport?[] connections)
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<InMemoryTransport?> _queue = new(connections);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IMcpTransport?> DialAsync(CancellationToken _)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IMcpTransport?>(_queue.TryDequeue(out var next) ? next : null);
        }
    }

    private static BridgeOptions Watching(AppSnapshot? snapshot = null)
        => new() { WatchInterval = TimeSpan.FromMilliseconds(20), Snapshot = snapshot ?? new AppSnapshot() };

    private const string Tools =
        @"{""tools"":[{""name"":""describe_app"",""description"":""d"",""inputSchema"":{""type"":""object""}}]}";

    [Fact]
    public async Task Run_AnswersTheInitialize_WhenTheClientStartsBeforeTheApp()
    {
        // It used to be refused, and clients give up on a server whose initialize fails.
        var stdio = new InMemoryTransport();
        var run = BridgeRelay.RunAsync(stdio, Dial(), Watching());

        stdio.Inject(Initialize);
        var result = Json(await Read(stdio))["result"]!;

        Assert.Equal("2025-06-18", ((JsonString)result["protocolVersion"]!).Value); // the client's own, echoed
        Assert.Equal(McpConstants.ServerName, ((JsonString)result["serverInfo"]!["name"]!).Value);
        Assert.Equal(new JsonBool(true), result["capabilities"]!["tools"]!["listChanged"]);
        Assert.Contains("not running", ((JsonString)result["instructions"]!).Value);

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_WhileTheAppIsAway_ListsItsLastTools_AndEachCallSaysWhy()
    {
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var options = Watching();
        var run = BridgeRelay.RunAsync(stdio, Dial(app), options);

        stdio.Inject(Initialize);
        await Read(app);
        app.Inject(@"{""jsonrpc"":""2.0"",""id"":1,""result"":{""protocolVersion"":""2025-06-18"",""serverInfo"":{""name"":""verbinal-canfar"",""version"":""1.4.1.0""}}}");
        await Read(stdio);
        stdio.Inject(Request(2));
        await Read(app);
        app.Inject($@"{{""jsonrpc"":""2.0"",""id"":2,""result"":{Tools}}}");
        await Read(stdio);

        await AppQuits(app);

        stdio.Inject(Request(3));
        Assert.Equal(Json(Tools).ToJsonString(), Json(await Read(stdio))["result"]!.ToJsonString());

        stdio.Inject(@"{""jsonrpc"":""2.0"",""id"":4,""method"":""tools/call"",""params"":{""name"":""describe_app""}}");
        Assert.Contains("not running", ToolError(Json(await Read(stdio))));

        stdio.Inject(Request(5, "ping"));
        Assert.Equal("{}", Json(await Read(stdio))["result"]!.ToJsonString());

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_RemembersTheApp_FromOneRunOfTheBridgeToTheNext()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bridge-{Guid.NewGuid():N}.json");
        try
        {
            var stdio = new InMemoryTransport();
            var app = new InMemoryTransport();
            var run = BridgeRelay.RunAsync(stdio, Dial(app), Watching(new AppSnapshot(file)));
            stdio.Inject(Initialize);
            await Read(app);
            app.Inject(@"{""jsonrpc"":""2.0"",""id"":1,""result"":{""protocolVersion"":""2025-06-18"",""serverInfo"":{""name"":""verbinal-canfar"",""version"":""1.4.1.0""}}}");
            await Read(stdio);
            stdio.Inject(Request(2));
            await Read(app);
            app.Inject($@"{{""jsonrpc"":""2.0"",""id"":2,""result"":{Tools}}}");
            await Read(stdio);
            stdio.CompleteIncoming();
            await Ends(run);

            // The next time the client starts, the app is not there yet.
            stdio = new InMemoryTransport();
            run = BridgeRelay.RunAsync(stdio, Dial(), Watching(new AppSnapshot(file)));
            stdio.Inject(Initialize);
            Assert.Equal("1.4.1.0", ((JsonString)Json(await Read(stdio))["result"]!["serverInfo"]!["version"]!).Value);
            stdio.Inject(Request(2));
            Assert.Equal(Json(Tools).ToJsonString(), Json(await Read(stdio))["result"]!.ToJsonString());
            stdio.CompleteIncoming();
            await Ends(run);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Run_FindsTheAppOnItsOwn_AndTellsTheClientItsToolsChanged()
    {
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var options = Watching();
        var run = BridgeRelay.RunAsync(stdio, Dial(null, app), options);

        stdio.Inject(Initialize);
        Assert.Equal(new JsonBool(true), Json(await Read(stdio))["result"]!["capabilities"]!["tools"]!["listChanged"]);

        // No request from the client: the bridge looks for the app, and introduces the client to it.
        var replay = Json(await Read(app));
        Assert.Equal("verbinal-bridge-reconnect-1", ((JsonString)replay["id"]!).Value);
        app.Inject(Answer(@"""verbinal-bridge-reconnect-1"""));

        Assert.Equal("notifications/tools/list_changed", ((JsonString)Json(await Read(stdio))["method"]!).Value);

        // The client lists again, and the app's own answer is what it gets — and what is kept.
        stdio.Inject(Request(2));
        Assert.Equal(Request(2), await Read(app));
        app.Inject($@"{{""jsonrpc"":""2.0"",""id"":2,""result"":{Tools}}}");
        await Read(stdio);
        Assert.Equal(Json(Tools).ToJsonString(), options.Snapshot[AppSnapshot.ToolsKey]!.ToJsonString());

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_StopsLooking_WhenTheAppSaysNo()
    {
        // Looking again would put the app's approval prompt in front of the person every few seconds.
        var stdio = new InMemoryTransport();
        var refusing = new InMemoryTransport();
        var dialer = new Dialer(null, refusing);
        var run = BridgeRelay.RunAsync(stdio, dialer.DialAsync, Watching());

        stdio.Inject(Initialize);
        await Read(stdio);
        await Read(refusing);
        refusing.Inject(@"{""jsonrpc"":""2.0"",""id"":""verbinal-bridge-reconnect-1"",""error"":{""code"":-32001,""message"":""Client not approved by user.""}}");

        await Task.Delay(TimeSpan.FromMilliseconds(300)); // fifteen intervals
        Assert.Equal(2, dialer.Calls);
        Assert.False(stdio.HasPendingResponse); // and no word that the tools changed

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_DoesNotLook_AfterListingForAnAppThatSaidNo()
    {
        // The app is there and refused: the stand-in list is answered, but looking again would only ask again.
        var stdio = new InMemoryTransport();
        var first = new InMemoryTransport();
        var refusing = new InMemoryTransport();
        var dialer = new Dialer(first, refusing);
        var run = BridgeRelay.RunAsync(stdio, dialer.DialAsync, Watching());

        stdio.Inject(Initialize);
        await Read(first);
        first.Inject(Answer("1"));
        await Read(stdio);
        await AppQuits(first);

        stdio.Inject(Request(2));
        await Read(refusing);
        refusing.Inject(@"{""jsonrpc"":""2.0"",""id"":""verbinal-bridge-reconnect-1"",""error"":{""code"":-32001,""message"":""Client not approved by user.""}}");
        Assert.Equal(@"{""tools"":[]}", Json(await Read(stdio))["result"]!.ToJsonString());

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.Equal(2, dialer.Calls);

        stdio.CompleteIncoming();
        await Ends(run);
    }

    [Fact]
    public async Task Run_DoesNotLook_WhenTheClientHasTheAppsOwnTools()
    {
        // Gone after a real session, the app is reached again by the next request, as before.
        var stdio = new InMemoryTransport();
        var app = new InMemoryTransport();
        var dialer = new Dialer(app);
        var run = BridgeRelay.RunAsync(stdio, dialer.DialAsync, Watching());

        stdio.Inject(Initialize);
        await Read(app);
        app.Inject(Answer("1"));
        await Read(stdio);
        await AppQuits(app);

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, dialer.Calls);

        stdio.CompleteIncoming();
        await Ends(run);
    }
}
