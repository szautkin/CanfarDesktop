using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Listener;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
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
    private InMemoryProposalStore? _proposals;
    private ProposalApplierRegistry? _registry;

    /// <summary>Newest-first feed of agent writes (for the review-after UI under auto-apply).</summary>
    public AgentActivityLog Activity { get; } = new();

    /// <summary>Token-cursor proposal-lifecycle event buffer backing the <c>list_events</c> tool.</summary>
    public AgentEventLog EventLog { get; } = new();

    /// <summary>Raised whenever the pending-proposal set changes (any thread).</summary>
    public event Action? ProposalsChanged;

    /// <summary>Raised after the server starts or stops (for the title-bar entry point).</summary>
    public event Action? RunningChanged;

    /// <summary>Pending agent proposals awaiting review, FIFO (empty when the server never started).</summary>
    public IReadOnlyList<PendingProposal> PendingProposals => _proposals?.List() ?? [];

    /// <summary>Pending count without materializing a snapshot (for the title-bar badge).</summary>
    public int PendingProposalCount => _proposals?.PendingCount ?? 0;

    /// <summary>
    /// User-initiated apply from the proposal strip. Runs the same gated applier path as auto-apply and
    /// records the outcome in the activity feed. Throws <see cref="ProposalApplyException"/> on failure.
    /// </summary>
    public Task ApplyProposalAsync(Guid proposalId) => ApplyProposalCoreAsync(proposalId, autoApplied: false);

    /// <summary>Ids whose applier is currently running — rejects are refused for these so a
    /// "Rejected" entry can't be recorded for a write that is about to land anyway.</summary>
    private readonly HashSet<Guid> _applying = new();
    private readonly object _applyingGate = new();

    /// <summary>Where an apply that outlives <see cref="ApplyTimeout"/> is reported from then on.</summary>
    private JobRegistry? _jobs;

    /// <summary>
    /// Whether this proposal's write is running right now — including one that outlived the backstop
    /// and carries on in the background. The proposal strip shows such a row as busy rather than
    /// offering Apply on something already applying.
    /// </summary>
    public bool IsApplying(Guid proposalId)
    {
        lock (_applyingGate) return _applying.Contains(proposalId);
    }

    /// <summary>
    /// What holds the apply gate, quoted, for the "queue busy" message — so it says which write is in
    /// the way rather than that one is. Null when none is recorded.
    /// </summary>
    private string? Applying()
    {
        Guid[] ids;
        lock (_applyingGate) ids = _applying.ToArray();
        return ids.Select(id => _proposals?.Get(id)?.Summary).FirstOrDefault(s => s is not null) is { } summary
            ? $"\"{summary}\""
            : null;
    }

    /// <summary>User-initiated reject from the proposal strip. Returns false when the proposal is no
    /// longer pending or its apply is already in flight (the write can't be stopped midway).</summary>
    public bool RejectProposal(Guid proposalId)
    {
        var store = _proposals;
        if (store is null) return false;
        lock (_applyingGate)
        {
            if (_applying.Contains(proposalId)) return false;
        }
        var proposal = store.Get(proposalId);
        if (proposal is null || !store.MarkRejected(proposalId)) return false;
        Activity.Append(AgentActivityEntry.Rejected(proposal, DateTimeOffset.UtcNow));
        return true;
    }

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

        var tools = McpToolCatalog.Build(_services, _appVersion, EventLog);
        var identity = new ServerIdentity(ServerName, _appVersion);

        // Shared write-surface state across connections. Journalled, because a restart used to destroy
        // the review queue in silence — proposals awaiting a human vanished, and one already approved
        // was voided. The host owns that decision; the store itself keeps nothing.
        var proposals = new InMemoryProposalStore(journal: new JsonProposalJournal());
        proposals.Changed += () => ProposalsChanged?.Invoke();
        proposals.EventOccurred += e => EventLog.Append(e.Kind, e.Proposal, DateTimeOffset.UtcNow);

        // Not a proposal, but something an agent waits on: the person has closed the last hint it
        // put up, which is a guided tour's cue to move to the next screen.
        try
        {
            _services.GetRequiredService<AppViewStateService>().HintsDismissed +=
                () => EventLog.Append("hintsDismissed", "uiHints", DateTimeOffset.UtcNow);
        }
        catch { /* no view state in a headless host; the log simply never sees these */ }
        _proposals = proposals;
        var budget = new ProposalBudget();

        // Appliers bound to the real stores + the auto-apply hook (gated by the user's autonomy toggle).
        var registry = new ProposalApplierRegistry();
        registry.Register(McpToolCatalog.BuildAppliers(_services));
        _registry = registry;
        // start_background_apply needs the store and the appliers, which are the host's rather than the
        // catalogue's — so it is added here, where both exist, rather than taking them through DI and
        // having two ideas of which store is the live one.
        _jobs = _services.GetService<JobRegistry>();
        if (tools is List<IMcpTool> mutable)
        {
            var runner = new BackgroundApplyRunner(proposals, registry, _services.GetRequiredService<JobRegistry>());
            mutable.Add(new StartBackgroundApplyTool(runner.StartAsync));
        }

        var autoApply = new AutoApplyHook(
            // Destructive writes (deletes, etc.) NEVER auto-apply — they always queue for explicit
            // approval, even with auto-apply on. Auto-apply only fast-paths reversible SemanticWrite.
            (verb, proposal) => Task.FromResult(AutoApplyPolicy.ShouldAutoApply(_settings.AutoApplyEnabled, verb)),
            proposalId => ApplyProposalCoreAsync(proposalId, autoApplied: true));

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

        // The wired approval gate: records connecting clients and (when the user requires approval) admits
        // only allow-listed ones. Shared across connections + the settings UI. Absent → allow-all fallback.
        var approvalGate = _services.GetService<McpClientApprovalStore>();

        // Default sidecar: a diagnostic breadcrumb at a location whose writes land at their literal
        // path even under MSIX virtualization (see McpSidecar / PackagePaths.WritableInteropRoot).
        _listener = new McpListenerService(
            () => new McpServerService(router, identity, gate: approvalGate, proposals: proposals, budget: budget, aiGuide: aiGuideSnapshot),
            log: CrashLogger.Info);
        _listener.Start(Guid.NewGuid());
        CrashLogger.Info($"MCP host started; pipe={_listener.PipeName}");
        RunningChanged?.Invoke();

        // Put the bridge where AGENTS.md tells every assistant to find it, whenever the server runs —
        // not only once the connect wizard or the settings panel has been opened, which is the Claude
        // path; an agent following the instructions on its own would find nothing there. Off the UI
        // thread: after an update it copies the new bridge out of the package.
        _ = Task.Run(() =>
        {
            try { CrashLogger.Info($"MCP bridge at {Config.McpBridgeLocator.ResolveStable() ?? "(not found)"}"); }
            catch (Exception ex) { CrashLogger.Info($"MCP bridge copy failed: {ex.Message}"); }
        });
    }

    /// <summary>
    /// The single gated apply path shared by auto-apply and the user's proposal strip: resolve the
    /// applier, serialize on the apply gate with the timeout backstop, mark applied, record activity.
    /// </summary>
    private async Task ApplyProposalCoreAsync(Guid proposalId, bool autoApplied)
    {
        var proposals = _proposals ?? throw ProposalApplyException.BackendError("the MCP server has not started");
        var proposal = proposals.Get(proposalId)
            ?? throw ProposalApplyException.BackendError("proposal no longer pending");
        var applier = _registry?.ApplierFor(proposal.Kind)
            ?? throw ProposalApplyException.NoApplierForKind(proposal.Kind);

        // Already applying means waiting for the gate is waiting for ITSELF. An Apply clicked on a
        // download still running past the backstop sat on a spinner for the whole gate wait, then
        // reported the queue busy — busy with that very download — and left the row looking refused.
        if (IsApplying(proposalId))
            throw ProposalApplyException.BackendError(
                "it is already applying; it leaves this list by itself when it finishes");

        if (!await _applyGate.WaitAsync(ApplyGateWait))
            throw ProposalApplyException.BackendError(
                $"the apply queue is busy ({Applying() ?? "another write"} is still applying); try again shortly");

        // Re-check under the gate: a user Apply click can queue behind an auto-apply of the SAME
        // proposal (or a reject) — running the applier again would duplicate the write.
        if (proposals.Get(proposalId) is null)
        {
            _applyGate.Release();
            throw ProposalApplyException.BackendError("proposal no longer pending (already applied or rejected)");
        }
        lock (_applyingGate) _applying.Add(proposalId);
        ProposalsChanged?.Invoke(); // the strip shows the row as applying, whoever started it

        // Not cancelled at the backstop: past it, the apply carries on as a background job, as the agent
        // is told. Cancelling it there stopped any applier that honours a token — a bulk download after
        // its first file — while the agent was told it would finish and not to apply it again. What
        // bounds an apply is its own work's limits (a download's stall timeout, a request's timeout).
        var applyTask = applier.ApplyAsync(proposal, CancellationToken.None);
        using (var delayCts = new CancellationTokenSource())
        {
            var completed = await Task.WhenAny(applyTask, Task.Delay(ApplyTimeout, delayCts.Token));
            delayCts.Cancel(); // stop the timer regardless of which finished first

            if (completed != applyTask)
            {
                // The apply blew the backstop. Hold the gate until it finishes, so the next apply can't
                // race the same store. It is still doing the write it was asked for — a large
                // observation download, say — so from here it is a background job: get_job_status
                // follows it, and when it lands the proposal is marked applied. It used to stay Pending
                // looking refused while the file sat in Research, inviting a second Apply that would
                // fetch the whole thing again.
                var jobId = proposalId.ToString();
                _jobs?.Start(jobId, proposal.Kind, proposal.Summary);
                _ = applyTask.ContinueWith(
                    t =>
                    {
                        // Marked before it leaves _applying, so a reject cannot slip in between.
                        var landed = t.IsCompletedSuccessfully && proposals.MarkApplied(proposalId);
                        _jobs?.Finish(jobId, t.IsCompletedSuccessfully,
                            t.Exception?.GetBaseException().Message ?? (t.IsCanceled ? "cancelled" : null));

                        lock (_applyingGate) _applying.Remove(proposalId);
                        _applyGate.Release();

                        // No FollowActivity: navigating the person minutes after the fact, away from
                        // whatever they have moved on to, is not following the agent.
                        if (landed) Activity.Append(AgentActivityEntry.Applied(proposal, autoApplied, DateTimeOffset.UtcNow));
                        ProposalsChanged?.Invoke();
                    }, TaskScheduler.Default);
                throw ProposalApplyException.BackendError(
                    $"still applying after {ApplyTimeout.TotalSeconds:0}s. It carries on and is marked applied " +
                    $"if it finishes — get_job_status with id {jobId} follows it. Do not apply it again.");
            }
        }

        bool applied;
        try
        {
            await applyTask; // surface the applier's own success / failure
            applied = proposals.MarkApplied(proposalId);
        }
        finally
        {
            lock (_applyingGate) _applying.Remove(proposalId);
            _applyGate.Release();
            ProposalsChanged?.Invoke();
        }

        // MarkApplied returns false when the proposal resolved some other way while the applier
        // ran — don't record a second, contradictory activity entry for it.
        if (applied)
        {
            Activity.Append(AgentActivityEntry.Applied(proposal, autoApplied, DateTimeOffset.UtcNow));
            FollowActivity(proposal.Kind);
        }
    }

    /// <summary>After an applied write, send the user to the relevant view (when follow-activity is on).</summary>
    private void FollowActivity(string kind)
    {
        if (_settings.FollowAgentActivityEnabled) NavigateBestEffort(AgentScreens.For(kind));
    }

    /// <summary>
    /// After a successful agent read, follow it to the module it concerns so the user can see the agent
    /// working (search/portal/storage/research). Invoked fire-and-forget by the router; never throws.
    /// </summary>
    private Task FollowToolActivity(string toolName)
    {
        if (_settings.FollowAgentActivityEnabled) NavigateBestEffort(AgentScreens.For(toolName));
        return Task.CompletedTask;
    }

    /// <summary>Pulse the "agent is working" indicator for any agent tool call (independent of the
    /// follow-activity navigation toggle — the indicator always reflects that the agent is active).</summary>
    private void NotifyAgentWorking(string toolName)
    {
        try { _services.GetRequiredService<AppViewStateService>().NotifyAgentActivity(toolName, AgentScreens.For(toolName)); }
        catch { /* indicator is best-effort */ }
    }

    private void NavigateBestEffort(string? mode)
    {
        if (mode is null) return;

        // Following is the app keeping up with an agent, never a request of the person: signed out, an
        // agent's work on a screen that needs sign-in is not followed there, rather than putting a
        // sign-in dialog in front of somebody who asked for nothing. navigate_to still asks.
        if (AccountScreens.Contains(mode) && !_services.GetRequiredService<Services.IAuthService>().IsAuthenticated) return;
        try
        {
            var nav = _services.GetRequiredService<AppViewStateService>().NavigateAsync(mode);
            _ = nav.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default); // observe, swallow
        }
        catch { /* navigation is best-effort */ }
    }

    /// <summary>Stop the server and remove the sidecar (idempotent).</summary>
    public async Task StopAsync()
    {
        if (_listener is null) return;
        await _listener.DisposeAsync();
        _listener = null;
        CrashLogger.Info("MCP host stopped");
        RunningChanged?.Invoke();
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
