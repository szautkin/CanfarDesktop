using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>One session image from the Skaha catalog (id + the session types it supports).</summary>
public sealed record SessionImageView(string Id, IReadOnlyList<string> Types)
{
    public static SessionImageView From(RawImage i) => new(i.Id, i.Types);
}

/// <summary><c>list_session_images</c> — the container images in the Skaha catalog, optionally filtered by type.</summary>
public sealed class ListSessionImagesTool : JsonReadTool<ListSessionImagesTool.Args, ListSessionImagesTool.Output>
{
    private readonly Func<Task<List<RawImage>>> _images;

    public ListSessionImagesTool(Func<Task<List<RawImage>>> images) => _images = images;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_session_images",
        "List the container images in the Skaha catalog (id + supported session types), optionally filtered to one type.",
        """{"type":"object","properties":{"type":{"type":"string","description":"Optional: only images supporting this session type (e.g. notebook, desktop, headless)"}},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var images = (await _images()).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(args.Type))
            images = images.Where(i => i.Types.Any(t => string.Equals(t, args.Type, StringComparison.OrdinalIgnoreCase)));

        var items = images.Select(SessionImageView.From).ToList();
        return new Output(items.Count, items);
    }

    public sealed record Args
    {
        public string? Type { get; init; }
    }

    public sealed record Output(int Count, IReadOnlyList<SessionImageView> Images);
}

/// <summary>One previously launched session remembered locally.</summary>
public sealed record RecentLaunchView(
    string Name, string Type, string Image, string ImageLabel, string Project,
    string ResourceType, int Cores, int Ram, int Gpus, DateTime LaunchedAt)
{
    public static RecentLaunchView From(RecentLaunch r) => new(
        r.Name, r.Type, r.Image, r.ImageLabel, r.Project,
        r.ResourceType, r.Cores, r.Ram, r.Gpus, r.LaunchedAt);
}

/// <summary><c>list_recent_launches</c> — the user's most recently launched sessions (locally remembered).</summary>
public sealed class ListRecentLaunchesTool : JsonReadTool<ListRecentLaunchesTool.Args, ListRecentLaunchesTool.Output>
{
    private readonly Func<IReadOnlyList<RecentLaunch>> _recent;

    public ListRecentLaunchesTool(Func<IReadOnlyList<RecentLaunch>> recent) => _recent = recent;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_recent_launches",
        "List the user's recently launched sessions remembered locally (name, type, image, resources, when).",
        """{"type":"object","properties":{"limit":{"type":"integer","minimum":1,"description":"Optional: cap the number returned (most recent first)"}},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Limit is <= 0)
            throw new McpToolException(new InvalidArgument("limit must be a positive integer"));

        IEnumerable<RecentLaunch> launches = _recent().OrderByDescending(l => l.LaunchedAt);
        if (args.Limit is int limit)
            launches = launches.Take(limit);

        var items = launches.Select(RecentLaunchView.From).ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record Args
    {
        public int? Limit { get; init; }
    }

    public sealed record Output(int Count, IReadOnlyList<RecentLaunchView> Launches);
}

/// <summary><c>list_headless_jobs</c> — the user's headless (batch) sessions.</summary>
public sealed class ListHeadlessJobsTool : JsonReadTool<EmptyArgs, ListHeadlessJobsTool.Output>
{
    private readonly Func<Task<IReadOnlyList<Session>>> _sessions;

    public ListHeadlessJobsTool(Func<Task<IReadOnlyList<Session>>> sessions) => _sessions = sessions;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_headless_jobs",
        "List the user's headless (batch) jobs — sessions of type \"headless\" (id, name, status, image, when).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var items = (await _sessions())
            .Where(s => string.Equals(s.SessionType, "headless", StringComparison.OrdinalIgnoreCase))
            .Select(s => new HeadlessJobView(s.Id, s.SessionName, s.Status, s.ContainerImage, s.StartedTime, s.ExpiresTime))
            .ToList();
        return new Output(items.Count, items);
    }

    public sealed record HeadlessJobView(string Id, string Name, string Status, string Image, string StartedTime, string ExpiresTime);
    public sealed record Output(int Count, IReadOnlyList<HeadlessJobView> Jobs);
}

/// <summary><c>get_headless_job_logs</c> — the stdout/stderr logs of one headless job.</summary>
public sealed class GetHeadlessJobLogsTool : JsonReadTool<GetHeadlessJobLogsTool.Args, GetHeadlessJobLogsTool.Output>
{
    private readonly Func<string, Task<string?>> _logs;

    public GetHeadlessJobLogsTool(Func<string, Task<string?>> logs) => _logs = logs;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_headless_job_logs",
        "Get the logs (stdout/stderr) of one headless job by its session id.",
        """{"type":"object","properties":{"id":{"type":"string","description":"Headless session id"}},"required":["id"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
            throw new McpToolException(new InvalidArgument("id is required"));
        var logs = await _logs(args.Id);
        if (logs is null)
            throw new McpToolException(new UnknownTarget(args.Id));
        return new Output(args.Id, logs);
    }

    public sealed record Args
    {
        public string Id { get; init; } = string.Empty;
    }

    public sealed record Output(string Id, string Logs);
}

/// <summary><c>get_headless_job_events</c> — the Kubernetes events of one headless job.</summary>
public sealed class GetHeadlessJobEventsTool : JsonReadTool<GetHeadlessJobEventsTool.Args, GetHeadlessJobEventsTool.Output>
{
    private readonly Func<string, Task<string?>> _events;

    public GetHeadlessJobEventsTool(Func<string, Task<string?>> events) => _events = events;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_headless_job_events",
        "Get the events (scheduling/lifecycle) of one headless job by its session id.",
        """{"type":"object","properties":{"id":{"type":"string","description":"Headless session id"}},"required":["id"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
            throw new McpToolException(new InvalidArgument("id is required"));
        var events = await _events(args.Id);
        if (events is null)
            throw new McpToolException(new UnknownTarget(args.Id));
        return new Output(args.Id, events);
    }

    public sealed record Args
    {
        public string Id { get; init; } = string.Empty;
    }

    public sealed record Output(string Id, string Events);
}
