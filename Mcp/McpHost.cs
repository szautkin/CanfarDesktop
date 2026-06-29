using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Listener;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Services.AiGuide;

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

    // A queued apply waits at most this long for the gate, then fails fast — so a slow/stuck apply can't
    // make later writes hang indefinitely behind it (the "bulk-note-write stuck behind a hung download"
    // cascade the smoke tests hit). Reads are never gated.
    private static readonly TimeSpan ApplyGateWait = TimeSpan.FromSeconds(45);
    // Backstop: a single apply running longer than this is reported to the agent as a timeout. Set above
    // the longest legitimate applier (the observation download) so genuine work isn't false-failed.
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(150);

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

                if (!await _applyGate.WaitAsync(ApplyGateWait))
                    throw ProposalApplyException.BackendError(
                        "the apply queue is busy (another write is still applying); try again shortly");

                var applyCts = new CancellationTokenSource(ApplyTimeout);
                var applyTask = applier.ApplyAsync(proposal, applyCts.Token);
                using (var delayCts = new CancellationTokenSource())
                {
                    var completed = await Task.WhenAny(applyTask, Task.Delay(ApplyTimeout, delayCts.Token));
                    delayCts.Cancel(); // stop the timer regardless of which finished first

                    if (completed != applyTask)
                    {
                        // The apply blew the backstop. Cancel it and hold the gate until it actually
                        // unwinds, so the next apply can't race the same store; report a typed timeout now.
                        applyCts.Cancel();
                        _ = applyTask.ContinueWith(
                            _ => { applyCts.Dispose(); _applyGate.Release(); }, TaskScheduler.Default);
                        throw ProposalApplyException.BackendError(
                            $"apply timed out after {ApplyTimeout.TotalSeconds:0}s");
                    }
                }

                try
                {
                    await applyTask; // surface the applier's own success / failure
                    proposals.MarkApplied(proposalId);
                }
                finally
                {
                    applyCts.Dispose();
                    _applyGate.Release();
                }

                Activity.Append(AgentActivityEntry.Applied(proposal, autoApplied: true, DateTimeOffset.UtcNow));
                FollowActivity(proposal.Kind);
            });

        // onAgentActivity makes the app follow the agent's reads (navigate to the module it's working in);
        // writes follow on the apply path (FollowActivity). Both gated by FollowAgentActivityEnabled.
        // onAgentDispatchStart pulses the transient "agent is working" indicator for every agent call.
        var router = new McpToolRouter(
            tools,
            autoApplyHook: autoApply,
            onAgentActivity: FollowToolActivity,
            onAgentDispatchStart: NotifyAgentWorking);

        // AI Guide (optional): description overrides + user guide tools, read live per tools/list call.
        // Tell it the real tool names so a guide can't shadow a built-in. Absent → un-tuned manifest.
        var aiGuide = _services.GetService<AiGuideService>();
        if (aiGuide is not null)
            aiGuide.KnownToolNames = router.ToolNames.ToHashSet(StringComparer.Ordinal);
        Func<AiGuideSnapshot>? aiGuideSnapshot = aiGuide is null ? null : aiGuide.Snapshot;

        // Write the sidecar to the REAL %LOCALAPPDATA% (un-redirected) so the UNPACKAGED bridge can find
        // it — a packaged app's default AppData is sandboxed to its package container (PackagePaths).
        var sidecar = new McpSidecar(Path.Combine(PackagePaths.RealLocalAppData(), McpConstants.SidecarFolderName));

        _listener = new McpListenerService(
            () => new McpServerService(router, identity, proposals: proposals, budget: budget, aiGuide: aiGuideSnapshot),
            sidecar: sidecar,
            log: CrashLogger.Info);
        _listener.Start(Guid.NewGuid());
        CrashLogger.Info($"MCP host started; pipe={_listener.PipeName}");
    }

    /// <summary>After an applied write, send the user to the relevant view (when follow-activity is on).</summary>
    private void FollowActivity(string kind)
    {
        if (_settings.FollowAgentActivityEnabled) NavigateBestEffort(ModeForTool(kind));
    }

    /// <summary>
    /// After a successful agent read, follow it to the module it concerns so the user can see the agent
    /// working (search/portal/storage/research). Invoked fire-and-forget by the router; never throws.
    /// </summary>
    private Task FollowToolActivity(string toolName)
    {
        if (_settings.FollowAgentActivityEnabled) NavigateBestEffort(ModeForTool(toolName));
        return Task.CompletedTask;
    }

    /// <summary>Pulse the "agent is working" indicator for any agent tool call (independent of the
    /// follow-activity navigation toggle — the indicator always reflects that the agent is active).</summary>
    private void NotifyAgentWorking(string toolName)
    {
        try { _services.GetRequiredService<AppViewStateService>().NotifyAgentActivity(toolName, ModeForTool(toolName)); }
        catch { /* indicator is best-effort */ }
    }

    private void NavigateBestEffort(string? mode)
    {
        if (mode is null) return;
        try
        {
            var nav = _services.GetRequiredService<AppViewStateService>().NavigateAsync(mode);
            _ = nav.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default); // observe, swallow
        }
        catch { /* navigation is best-effort */ }
    }

    /// <summary>
    /// Map a tool name (read) or applied write kind to the app module to navigate to. Returns null for
    /// tools that should not move the view: foundational/meta tools (describe_app, get_current_view,
    /// get_auth_state, …), the view-state writes that navigate themselves (navigate_to, open_fits_file),
    /// and the local FITS readers / preview fetch.
    /// </summary>
    private static string? ModeForTool(string name) => name switch
    {
        "search_observations" or "resolve_target" or "list_saved_queries" or "get_saved_query"
            or "list_recent_searches" or "save_query" or "delete_saved_query" => "search",
        "list_downloaded_observations" or "get_downloaded_observation" or "get_observation_notes"
            or "get_observation_caom2" or "get_data_links" or "update_observation_note"
            or "bulk_update_observation_notes" or "download_observation" or "delete_downloaded_observation" => "research",
        "list_sessions" or "get_session" or "list_session_types" or "list_headless_jobs"
            or "get_headless_job_logs" or "get_headless_job_events" or "list_session_images"
            or "list_recent_launches" or "find_images_with_packages" or "get_platform_load"
            or "launch_session" or "launch_headless_job" or "delete_session" or "renew_session" => "portal",
        "get_storage_quota" or "list_vospace_path" or "read_vospace_file"
            or "upload_text_to_vospace" or "create_vospace_folder" or "delete_vospace_node" => "storage",
        _ => null,
    };

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
