namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>Reachability + health of one upstream service. Reachable = the host answered (any HTTP
/// status); Ok = the endpoint answered sanely (not 404 / 5xx) — a 404 host is up but NOT healthy.</summary>
public sealed record ServiceHealthEntry(string Service, string Url, bool Reachable, bool Ok, int? StatusCode, long LatencyMs, string? Error);

/// <summary>
/// <c>get_service_health</c> — probe the upstream CADC/CANFAR services for reachability + latency so an
/// agent can tell whether a service is down before depending on it. The actual probing is injected
/// (the tool stays pure/testable); the host wires it to HTTP reachability checks.
/// </summary>
public sealed class GetServiceHealthTool : JsonReadTool<EmptyArgs, GetServiceHealthTool.Output>
{
    private readonly Func<Task<IReadOnlyList<ServiceHealthEntry>>> _probe;

    public GetServiceHealthTool(Func<Task<IReadOnlyList<ServiceHealthEntry>>> probe) => _probe = probe;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_service_health",
        "Probe the upstream CADC/CANFAR services (TAP search, Skaha sessions, ARC/VOSpace storage, CADC " +
        "auth) for reachability + round-trip latency. Use it to tell whether a service is up before you " +
        "depend on it (e.g. before search_observations or launch_session). Per service, `reachable` " +
        "means the HOST answered (any HTTP status) while `ok` means the service answered sanely (not " +
        "404/5xx) — trust `ok`/`healthyCount` for \"can I use it\", with `statusCode` as the detail.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var services = await _probe();
        return new Output(services.Count, services.Count(s => s.Reachable), services.Count(s => s.Ok), services);
    }

    public sealed record Output(int Count, int ReachableCount, int HealthyCount, IReadOnlyList<ServiceHealthEntry> Services);
}
