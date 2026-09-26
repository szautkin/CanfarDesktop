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

        stdio.Inject(Request(3));
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

        stdio.Inject(Request(6));
        await Read(second);
        second.Inject(@"{""jsonrpc"":""2.0"",""id"":""verbinal-bridge-reconnect-1"",""error"":{""code"":-32001,""message"":""Client not approved by user.""}}");

        var refused = Json(await Read(stdio));
        Assert.Equal(6, ((JsonInt)refused["id"]!).Value);
        Assert.Contains("Client not approved by user.", ((JsonString)((JsonObject)refused["error"]!)["message"]!).Value);

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

        var failed = await Read(stdio);
        Assert.Equal(4, ((JsonInt)Json(failed)["id"]!).Value);
        Assert.Equal(-32000, ErrorCode(failed));

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
}
