using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Mcp;

/// <summary>Pass / warn / fail / in-progress state for one diagnostic row.</summary>
public enum McpDiagnosticStatus { Pass, Warn, Fail, Running }

/// <summary>
/// One row in the MCP diagnostics list. <paramref name="FixId"/> is one of the
/// <see cref="McpDiagnosticFix"/> ids when the row offers a one-click repair, else null.
/// </summary>
public sealed record McpDiagnosticCheck(
    string Id, string Title, McpDiagnosticStatus Status, string Message, string? FixId = null);

/// <summary>The repair actions a diagnostic row can offer, dispatched by <see cref="McpDiagnosticsRunner.ApplyFixAsync"/>.</summary>
public static class McpDiagnosticFix
{
    public const string EnableServer = "enableServer";
    public const string RestartServer = "restartServer";
    public const string UpdateConfig = "updateConfig";
    public const string RevealBridge = "revealBridge";

    public static string Title(string fixId) => fixId switch
    {
        EnableServer => "Enable",
        RestartServer => "Restart",
        UpdateConfig => "Update Config",
        RevealBridge => "Reveal",
        _ => "Fix",
    };
}

/// <summary>
/// Runs the MCP integration diagnostics — a battery of fast local checks plus the live pipe
/// round-trip self-test — and dispatches the per-row Fix actions. WinUI-free port of the macOS
/// MCPDiagnosticsModel: every probe and repair is an injected delegate so each check is
/// unit-testable without the app, the named pipe, or the real filesystem.
/// </summary>
public sealed class McpDiagnosticsRunner
{
    // ── Probes ──
    public required Func<bool> ServerEnabled { get; init; }
    public required Func<bool> ListenerRunning { get; init; }
    public required Func<string?> PipeName { get; init; }
    public required Func<CancellationToken, Task<McpSelfTestResult>> SelfTest { get; init; }
    public required Func<string?> BridgePath { get; init; }
    public required Func<ClaudeClients> DetectClients { get; init; }
    public required Func<string?> ReadClaudeConfig { get; init; }
    public required string ClaudeConfigPath { get; init; }

    // ── Repairs ──
    public required Func<Task> EnableServerAsync { get; init; }
    public required Func<Task> RestartServerAsync { get; init; }
    /// <summary>Merge the Verbinal entry into the Claude Desktop config; receives the bridge exe path.</summary>
    public required Action<string> ApplyConfigRepair { get; init; }
    /// <summary>Show the bridge exe in the file manager; receives the bridge exe path.</summary>
    public required Action<string> RevealBridge { get; init; }

    public async Task<IReadOnlyList<McpDiagnosticCheck>> RunAsync(CancellationToken ct = default)
    {
        var enabled = ServerEnabled();
        var running = ListenerRunning();
        var bridge = BridgePath();
        var clients = DetectClients();

        var checks = new List<McpDiagnosticCheck>
        {
            ServerEnabledCheck(enabled),
            ListenerRunningCheck(enabled, running),
            await ListenerHealthCheckAsync(enabled, running, ct),
            BridgeCheck(bridge),
            clients.DesktopInstalled
                ? new("claudeDesktop", "Claude Desktop installed", McpDiagnosticStatus.Pass, "Detected.")
                : new("claudeDesktop", "Claude Desktop installed", McpDiagnosticStatus.Warn,
                    "Not found (other MCP clients still work)."),
            clients.CodeInstalled
                ? new("claudeCode", "Claude Code installed", McpDiagnosticStatus.Pass, "The claude CLI is on PATH.")
                : new("claudeCode", "Claude Code installed", McpDiagnosticStatus.Warn,
                    "No claude launcher on PATH (optional)."),
            ConfigCheck(bridge, clients.DesktopInstalled),
        };
        return checks;
    }

    public async Task ApplyFixAsync(string fixId)
    {
        switch (fixId)
        {
            case McpDiagnosticFix.EnableServer:
                await EnableServerAsync();
                break;
            case McpDiagnosticFix.RestartServer:
                await RestartServerAsync();
                break;
            case McpDiagnosticFix.UpdateConfig:
                ApplyConfigRepair(BridgePath()
                    ?? throw new InvalidOperationException("The MCP bridge exe wasn't found — build it first."));
                break;
            case McpDiagnosticFix.RevealBridge:
                if (BridgePath() is { } path) RevealBridge(path);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixId), fixId, "Unknown diagnostic fix.");
        }
    }

    private static McpDiagnosticCheck ServerEnabledCheck(bool enabled) => enabled
        ? new("serverEnabled", "Server enabled", McpDiagnosticStatus.Pass, "External AI agents are allowed.")
        : new("serverEnabled", "Server enabled", McpDiagnosticStatus.Fail,
            "Turn on “Enable MCP server”.", McpDiagnosticFix.EnableServer);

    private McpDiagnosticCheck ListenerRunningCheck(bool enabled, bool running)
    {
        if (!enabled)
            return new("listenerRunning", "Listener running", McpDiagnosticStatus.Warn,
                "Skipped — server disabled.", McpDiagnosticFix.EnableServer);
        if (!running)
            return new("listenerRunning", "Listener running", McpDiagnosticStatus.Fail,
                "Server is enabled but the listener didn't come up.", McpDiagnosticFix.RestartServer);
        return PipeName() is { } pipe
            ? new("listenerRunning", "Listener running", McpDiagnosticStatus.Pass, $"Listening on pipe: {pipe}")
            : new("listenerRunning", "Listener running", McpDiagnosticStatus.Warn,
                "Running but no pipe name published.", McpDiagnosticFix.RestartServer);
    }

    private async Task<McpDiagnosticCheck> ListenerHealthCheckAsync(bool enabled, bool running, CancellationToken ct)
    {
        if (!enabled || !running)
            return new("listenerHealth", "Listener health", McpDiagnosticStatus.Warn, "Skipped — listener not running.");

        McpSelfTestResult result;
        try { result = await SelfTest(ct); }
        catch (Exception ex)
        {
            return new("listenerHealth", "Listener health", McpDiagnosticStatus.Fail,
                $"Self-test failed: {ex.Message}", McpDiagnosticFix.RestartServer);
        }

        if (!result.Reachable)
            return new("listenerHealth", "Listener health", McpDiagnosticStatus.Fail,
                result.Error ?? "The self-test couldn't reach the server.", McpDiagnosticFix.RestartServer);
        if (result.ToolCount is not > 0)
            return new("listenerHealth", "Listener health", McpDiagnosticStatus.Warn,
                "Connected, but the server reported no tools — restart the listener.", McpDiagnosticFix.RestartServer);

        var server = result.ServerName is { } name ? $"{name}, " : string.Empty;
        return new("listenerHealth", "Listener health", McpDiagnosticStatus.Pass,
            $"initialize round-trip OK — {server}{result.ToolCount} tool{(result.ToolCount == 1 ? "" : "s")}.");
    }

    private static McpDiagnosticCheck BridgeCheck(string? bridge) => bridge is not null
        ? new("bridgePresent", "Bridge executable", McpDiagnosticStatus.Pass, bridge, McpDiagnosticFix.RevealBridge)
        : new("bridgePresent", "Bridge executable", McpDiagnosticStatus.Fail,
            $"{McpBridgeLocator.BridgeExeName} wasn't found — build the bridge project.");

    private McpDiagnosticCheck ConfigCheck(string? bridge, bool desktopInstalled)
    {
        if (!desktopInstalled)
            return new("claudeConfig", "Claude Desktop config", McpDiagnosticStatus.Warn,
                "Skipped — Claude Desktop not detected.");

        // Shared heuristic (ClaudeConfigRepair owns it) so this row and the wizard's resume logic agree.
        var registered = Config.ClaudeConfigRepair.ContainsBridgeReference(ReadClaudeConfig());
        if (registered)
            return new("claudeConfig", "Claude Desktop config", McpDiagnosticStatus.Pass,
                "References the Verbinal bridge.");
        if (bridge is null)
            return new("claudeConfig", "Claude Desktop config", McpDiagnosticStatus.Fail,
                "Not registered, and the bridge exe is missing — build the bridge project first.");
        return new("claudeConfig", "Claude Desktop config", McpDiagnosticStatus.Fail,
            $"No Verbinal entry in {ClaudeConfigPath}.", McpDiagnosticFix.UpdateConfig);
    }
}
