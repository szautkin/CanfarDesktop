namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>
/// Health of one upstream service, in the three senses that differ.
///
/// <para><c>Reachable</c> — the HOST answered, any status. <c>Ok</c> — the endpoint answered SANELY,
/// so not 404 (not there) or 5xx (failing); a 404 host is up but not healthy. <c>Available</c> — what
/// the service's own IVOA availability document says, which is null when it publishes none. A service
/// can be reachable, ok, and still say it should not be used right now, and only the document can
/// tell you that — <c>Note</c> carries its reason in its own words.</para>
///
/// <para><c>RequiresAuth</c> is whether using it needs a signed-in session, which is a different
/// question from whether it is up.</para>
/// </summary>
public sealed record ServiceHealthEntry(
    string Service, string Url, bool Reachable, bool Ok, int? StatusCode, long LatencyMs, string? Error,
    bool? Available = null, string? Note = null, bool RequiresAuth = false);

/// <summary>
/// <c>get_service_health</c> — probe the upstream CADC/CANFAR services so an agent can tell whether a
/// service is down before depending on it. The actual probing is injected (the tool stays
/// pure/testable); the host wires it to the real checks.
/// </summary>
public sealed class GetServiceHealthTool : JsonReadTool<EmptyArgs, GetServiceHealthTool.Output>
{
    private readonly Func<Task<IReadOnlyList<ServiceHealthEntry>>> _probe;
    private readonly Func<bool> _signedIn;

    /// <param name="signedIn">Whether somebody is signed in now — read per call, since usable depends on it.</param>
    public GetServiceHealthTool(Func<Task<IReadOnlyList<ServiceHealthEntry>>> probe, Func<bool> signedIn)
    {
        _probe = probe;
        _signedIn = signedIn;
    }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_service_health",
        "Probe the upstream CADC/CANFAR services (TAP search, Skaha sessions, ARC/VOSpace storage, CADC " +
        "auth) so you can tell whether one is down before depending on it — e.g. before " +
        "search_observations or launch_session. Each service reports `reachable` (the HOST answered, " +
        "any status), `ok` (the endpoint answered sanely, not 404/5xx) and, where the service publishes " +
        "an IVOA availability document, `available` plus its own `note` — a service can be up and still " +
        "say it should not be used, which no status code expresses. Trust `healthyCount` for \"is it " +
        "up\" and `usableCount` for \"can I use it right now\": a service needing a signed-in session " +
        "counts as usable only while the person is signed in, so a signed-out caller can see everything " +
        "healthy and little usable.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var services = await _probe();

        // Healthy needs all three to agree; usable also needs the caller not to be locked out of it —
        // which a service needing sign-in is only while nobody is signed in. It used to count those as
        // locked out regardless: two of four usable, for a signed-in person with all four healthy.
        var healthy = services.Where(s => s.Reachable && s.Ok && s.Available != false).ToList();
        var signedIn = _signedIn();

        return new Output(
            services.Count,
            services.Count(s => s.Reachable),
            healthy.Count,
            healthy.Count(s => signedIn || !s.RequiresAuth),
            services);
    }

    public sealed record Output(
        int Count, int ReachableCount, int HealthyCount, int UsableCount,
        IReadOnlyList<ServiceHealthEntry> Services);
}
