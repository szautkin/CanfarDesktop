using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>Compact view of a Skaha session for MCP output.</summary>
/// <summary>
/// One session as an agent sees it.
///
/// <para>Both halves of the resource picture, because Skaha reports only one of them for some session
/// types: it leaves the REQUESTED figures empty for notebook sessions while still reporting usage, so
/// a payload carrying only <c>cpuAllocated</c> carried an empty string and nothing else. "How much is
/// this session using" and "how much did it ask for" are different questions and a caller may only be
/// able to get an answer to one.</para>
/// </summary>
public sealed record SessionSummary(
    string Id, string Name, string Type, string Status, string Image,
    string StartedTime, string ExpiresTime, string CpuAllocated, string MemoryAllocated, string? GpuAllocated,
    string? ConnectUrl = null, string? CpuInUse = null, string? MemoryInUse = null)
{
    public static SessionSummary From(Session s) => new(
        s.Id, s.SessionName, s.SessionType, s.Status, s.ContainerImage,
        s.StartedTime, s.ExpiresTime, s.CpuAllocated, s.MemoryAllocated, s.GpuAllocated,
        s.ConnectUrl, s.CpuUsage, s.MemoryUsage);
}

/// <summary><c>list_sessions</c> — the user's active Skaha sessions.</summary>
public sealed class ListSessionsTool : JsonReadTool<EmptyArgs, ListSessionsTool.Output>
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<Session>>> _sessions;

    public ListSessionsTool(Func<CancellationToken, Task<IReadOnlyList<Session>>> sessions) => _sessions = sessions;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_sessions",
        "List the user's active Skaha sessions (id, name, type, status, image, resources, and the connectUrl to " +
        "open one). Resources come in two halves: `cpuAllocated`/`memoryAllocated` are what the session " +
        "REQUESTED and `cpuInUse`/`memoryInUse` what it is actually using. Skaha leaves the requested " +
        "figures empty for notebook sessions, so read the in-use pair when the allocated one is blank. " +
        "open an interactive session in the browser).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var items = (await _sessions(ct)).Select(SessionSummary.From).ToList();
        return new Output(items.Count, items);
    }

    public sealed record Output(int Count, IReadOnlyList<SessionSummary> Sessions);
}

/// <summary><c>get_session</c> — one session by id.</summary>
public sealed class GetSessionTool : JsonReadTool<GetSessionTool.Args, SessionSummary>
{
    private readonly Func<string, CancellationToken, Task<Session?>> _get;

    public GetSessionTool(Func<string, CancellationToken, Task<Session?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_session",
        "Get one Skaha session by its id, including its connectUrl (the link to open an interactive session " +
        "in the browser).",
        """{"type":"object","properties":{"id":{"type":"string","description":"Session id"}},"required":["id"],"additionalProperties":false}""");

    protected override async Task<SessionSummary> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
            throw new McpToolException(new InvalidArgument("id is required"));
        var session = await _get(args.Id, ct);
        if (session is null)
            throw new McpToolException(new UnknownTarget(args.Id));
        return SessionSummary.From(session);
    }

    public sealed record Args
    {
        public string Id { get; init; } = string.Empty;
    }
}

/// <summary><c>list_session_types</c> — the interactive + headless session types Skaha supports.</summary>
public sealed class ListSessionTypesTool : JsonReadTool<EmptyArgs, ListSessionTypesTool.Output>
{
    private static readonly string[] Types = { "notebook", "desktop", "carta", "contributed", "firefly", "headless" };

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_session_types",
        "List the session types that can be launched (notebook, desktop, carta, contributed, firefly, headless).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => Task.FromResult(new Output(Types));

    public sealed record Output(IReadOnlyList<string> Types);
}
