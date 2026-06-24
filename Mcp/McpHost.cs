using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Listener;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp;

/// <summary>
/// App-side owner of the MCP server. Assembles the router from the live tool catalog, runs the named-pipe
/// <see cref="McpListenerService"/>, and gates the whole thing behind the opt-in
/// <see cref="McpSettingsService"/>. A single router (stateless, shared) backs a fresh
/// <see cref="McpServerService"/> per connection. Registered as a DI singleton; started on launch
/// (when enabled) and stopped on shutdown.
/// </summary>
public sealed class McpHost : IAsyncDisposable
{
    private const string ServerName = "verbinal-canfar";

    private readonly IServiceProvider _services;
    private readonly McpSettingsService _settings;
    private readonly string _appVersion;

    // Serializes write APPLIES — concurrent dispatch means two write tools can auto-apply at once, and
    // the backing stores (e.g. the file-based saved-query store) do unguarded read-modify-write.
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    private McpListenerService? _listener;

    /// <summary>Newest-first feed of agent writes (for the review-after UI under auto-apply).</summary>
    public AgentActivityLog Activity { get; } = new();

    public McpHost(IServiceProvider services, McpSettingsService settings, string appVersion)
    {
        _services = services;
        _settings = settings;
        _appVersion = appVersion;
    }

    public bool IsRunning => _listener?.IsRunning == true;

    /// <summary>The live pipe name, or null when not running. For the diagnostics/config UI.</summary>
    public string? PipeName => _listener?.PipeName;

    /// <summary>Start the server only if the user has opted in. Safe to call once on launch.</summary>
    public void StartIfEnabled()
    {
        if (_settings.Enabled) Start();
    }

    /// <summary>Start the named-pipe server (idempotent).</summary>
    public void Start()
    {
        if (_listener is not null) return;

        var tools = McpToolCatalog.Build(_services, _appVersion);
        var identity = new ServerIdentity(ServerName, _appVersion);

        // Shared write-surface state across connections.
        var proposals = new InMemoryProposalStore();
        var budget = new ProposalBudget();

        // Appliers bound to the real stores + the auto-apply hook (gated by the user's autonomy toggle).
        var registry = new ProposalApplierRegistry();
        registry.Register(McpToolCatalog.BuildAppliers(_services));
        var autoApply = new AutoApplyHook(
            (verb, proposal) => Task.FromResult(_settings.AutoApplyEnabled),
            async proposalId =>
            {
                var proposal = proposals.Get(proposalId)
                    ?? throw ProposalApplyException.BackendError("proposal no longer pending");
                var applier = registry.ApplierFor(proposal.Kind)
                    ?? throw ProposalApplyException.NoApplierForKind(proposal.Kind);

                await _applyGate.WaitAsync();
                try
                {
                    await applier.ApplyAsync(proposal);
                    proposals.MarkApplied(proposalId);
                }
                finally
                {
                    _applyGate.Release();
                }

                Activity.Append(AgentActivityEntry.Applied(proposal, autoApplied: true, DateTimeOffset.UtcNow));
                FollowActivity(proposal.Kind);
            });

        var router = new McpToolRouter(tools, autoApplyHook: autoApply); // default LoggingAuditSink

        // Write the sidecar to the REAL %LOCALAPPDATA% (un-redirected) so the UNPACKAGED bridge can find
        // it — a packaged app's default AppData is sandboxed to its package container (PackagePaths).
        var sidecar = new McpSidecar(Path.Combine(PackagePaths.RealLocalAppData(), McpConstants.SidecarFolderName));

        _listener = new McpListenerService(
            () => new McpServerService(router, identity, proposals: proposals, budget: budget),
            sidecar: sidecar,
            log: CrashLogger.Info);
        _listener.Start(Guid.NewGuid());
        CrashLogger.Info($"MCP host started; pipe={_listener.PipeName}");
    }

    /// <summary>After an applied write, send the user to the relevant view (when follow-activity is on).</summary>
    private void FollowActivity(string kind)
    {
        if (!_settings.FollowAgentActivityEnabled) return;
        var mode = kind switch
        {
            "save_query" or "delete_saved_query" => "search",
            "update_observation_note" or "bulk_update_observation_notes" => "research",
            _ => null,
        };
        if (mode is null) return;
        try { _ = _services.GetRequiredService<AppViewStateService>().NavigateAsync(mode); }
        catch { /* navigation is best-effort */ }
    }

    /// <summary>Stop the server and remove the sidecar (idempotent).</summary>
    public async Task StopAsync()
    {
        if (_listener is null) return;
        await _listener.DisposeAsync();
        _listener = null;
        CrashLogger.Info("MCP host stopped");
    }

    /// <summary>Persist the enable toggle and start/stop the server to match.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        _settings.Enabled = enabled;
        if (enabled) Start();
        else await StopAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
