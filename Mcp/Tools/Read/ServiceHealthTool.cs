namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>
/// Health of one upstream service. <c>Reachable</c> is that the host answered; <c>Available</c> is
/// what the service's own availability document says — null when it publishes none, which is
/// "unknown" rather than "down". <c>RequiresAuth</c> says whether using it needs a signed-in session.
/// </summary>
public sealed record ServiceHealthEntry(
    string Service, string Url, bool Reachable, int? StatusCode, long LatencyMs, string? Error,
    bool? Available = null, string? Note = null, bool RequiresAuth = false);

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
        "auth) by reading each one's IVOA /availability document. Use it to tell whether a service is up " +
        "before you depend on it (e.g. before search_observations or launch_session). `healthyCount` is " +
        "how many are up; `usableCount` excludes the ones that need a signed-in session, so a signed-out " +
        "caller can see every service healthy and none of them usable. A service that is up but " +
        "announcing planned downtime reports available=false with its own `note`.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var services = await _probe();
        return new Output(
            services.Count,
            services.Count(s => s.Reachable),
            services.Count(s => s.Reachable && s.Available != false),
            services.Count(s => s.Reachable && s.Available != false && !s.RequiresAuth),
            services);
    }

    public sealed record Output(
        int Count, int ReachableCount, int HealthyCount, int UsableCount,
        IReadOnlyList<ServiceHealthEntry> Services);
}
