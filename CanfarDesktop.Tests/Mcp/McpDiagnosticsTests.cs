using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The MCP diagnostics runner behind the settings panel's Diagnostics expander: per-check
/// pass/warn/fail classification over fake probes, plus fix dispatch plumbing.
/// </summary>
public class McpDiagnosticsTests
{
    private const string BridgeExe = @"C:\app\CanfarDesktop.McpBridge.exe";
    private const string ConfigPath = @"C:\roaming\Claude\claude_desktop_config.json";

    /// <summary>A runner over an all-healthy fake environment; tests override the probe under test.</summary>
    private static McpDiagnosticsRunner Healthy(
        bool enabled = true,
        bool running = true,
        string? pipe = "canfar-mcp-test",
        McpSelfTestResult? selfTest = null,
        Func<CancellationToken, Task<McpSelfTestResult>>? selfTestFunc = null,
        string? bridge = BridgeExe,
        ClaudeClients? clients = null,
        string? config = "{\"mcpServers\":{\"verbinal-canfar\":{\"command\":\"C:\\\\app\\\\CanfarDesktop.McpBridge.exe\"}}}",
        Func<Task>? enableAsync = null,
        Func<Task>? restartAsync = null,
        Action<string>? applyRepair = null,
        Action<string>? reveal = null) => new()
    {
        ServerEnabled = () => enabled,
        ListenerRunning = () => running,
        PipeName = () => pipe,
        SelfTest = selfTestFunc ?? (_ => Task.FromResult(selfTest ?? new McpSelfTestResult(true, 42, "verbinal-canfar", null))),
        BridgePath = () => bridge,
        DetectClients = () => clients ?? new ClaudeClients(DesktopInstalled: true, CodeInstalled: true),
        ReadClaudeConfig = () => config,
        ClaudeConfigPath = ConfigPath,
        EnableServerAsync = enableAsync ?? (() => Task.CompletedTask),
        RestartServerAsync = restartAsync ?? (() => Task.CompletedTask),
        ApplyConfigRepair = applyRepair ?? (_ => { }),
        RevealBridge = reveal ?? (_ => { }),
    };

    private static async Task<McpDiagnosticCheck> RunOne(McpDiagnosticsRunner runner, string id)
        => Assert.Single(await runner.RunAsync(), c => c.Id == id);

    // ── Check battery shape ──

    [Fact]
    public async Task RunAsync_EmitsAllChecksInOrder()
    {
        var checks = await Healthy().RunAsync();
        Assert.Equal(
            new[] { "serverEnabled", "listenerRunning", "listenerHealth", "bridgePresent", "claudeDesktop", "claudeCode", "claudeConfig" },
            checks.Select(c => c.Id));
    }

    [Fact]
    public async Task RunAsync_AllHealthy_AllPassAndOnlyBridgeOffersAFix()
    {
        var checks = await Healthy().RunAsync();
        Assert.All(checks, c => Assert.Equal(McpDiagnosticStatus.Pass, c.Status));
        // The Reveal action on the bridge row is the only "fix" a healthy system shows.
        Assert.Equal(
            new[] { McpDiagnosticFix.RevealBridge },
            checks.Where(c => c.FixId is not null).Select(c => c.FixId));
    }

    // ── 1. Server enabled ──

    [Fact]
    public async Task ServerDisabled_FailsWithEnableFix()
    {
        var check = await RunOne(Healthy(enabled: false, running: false), "serverEnabled");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Equal(McpDiagnosticFix.EnableServer, check.FixId);
    }

    // ── 2. Listener running ──

    [Fact]
    public async Task ListenerRunning_Disabled_WarnsSkippedWithEnableFix()
    {
        var check = await RunOne(Healthy(enabled: false, running: false), "listenerRunning");
        Assert.Equal(McpDiagnosticStatus.Warn, check.Status);
        Assert.Contains("Skipped", check.Message);
        Assert.Equal(McpDiagnosticFix.EnableServer, check.FixId);
    }

    [Fact]
    public async Task ListenerRunning_EnabledButDown_FailsWithRestartFix()
    {
        var check = await RunOne(Healthy(running: false), "listenerRunning");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Equal(McpDiagnosticFix.RestartServer, check.FixId);
    }

    [Fact]
    public async Task ListenerRunning_NoPipeName_WarnsWithRestartFix()
    {
        var check = await RunOne(Healthy(pipe: null), "listenerRunning");
        Assert.Equal(McpDiagnosticStatus.Warn, check.Status);
        Assert.Equal(McpDiagnosticFix.RestartServer, check.FixId);
    }

    [Fact]
    public async Task ListenerRunning_Up_PassReportsPipeName()
    {
        var check = await RunOne(Healthy(), "listenerRunning");
        Assert.Equal(McpDiagnosticStatus.Pass, check.Status);
        Assert.Contains("canfar-mcp-test", check.Message);
    }

    // ── 3. Listener health (self-test) ──

    [Fact]
    public async Task Health_ListenerDown_WarnsSkipped_AndSelfTestNotInvoked()
    {
        var invoked = false;
        var runner = Healthy(running: false, selfTestFunc: _ => { invoked = true; return Task.FromResult(McpSelfTestResult.Unreachable("x")); });
        var check = await RunOne(runner, "listenerHealth");
        Assert.Equal(McpDiagnosticStatus.Warn, check.Status);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Health_RoundTripOk_PassReportsToolCountAndServerName()
    {
        var check = await RunOne(Healthy(selfTest: new McpSelfTestResult(true, 42, "verbinal-canfar", null)), "listenerHealth");
        Assert.Equal(McpDiagnosticStatus.Pass, check.Status);
        Assert.Contains("42 tools", check.Message);
        Assert.Contains("verbinal-canfar", check.Message);
        Assert.Null(check.FixId);
    }

    [Fact]
    public async Task Health_Unreachable_FailsWithServerErrorAndRestartFix()
    {
        var check = await RunOne(Healthy(selfTest: McpSelfTestResult.Unreachable("Couldn't reach the MCP server.")), "listenerHealth");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Contains("Couldn't reach", check.Message);
        Assert.Equal(McpDiagnosticFix.RestartServer, check.FixId);
    }

    [Fact]
    public async Task Health_ReachableButNoTools_WarnsWithRestartFix()
    {
        var check = await RunOne(Healthy(selfTest: new McpSelfTestResult(true, 0, "s", null)), "listenerHealth");
        Assert.Equal(McpDiagnosticStatus.Warn, check.Status);
        Assert.Equal(McpDiagnosticFix.RestartServer, check.FixId);
    }

    [Fact]
    public async Task Health_SelfTestThrows_FailsWithRestartFix()
    {
        var check = await RunOne(Healthy(selfTestFunc: _ => throw new IOException("pipe broke")), "listenerHealth");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Contains("pipe broke", check.Message);
        Assert.Equal(McpDiagnosticFix.RestartServer, check.FixId);
    }

    // ── 4. Bridge executable ──

    [Fact]
    public async Task Bridge_Found_PassShowsPathWithRevealAction()
    {
        var check = await RunOne(Healthy(), "bridgePresent");
        Assert.Equal(McpDiagnosticStatus.Pass, check.Status);
        Assert.Equal(BridgeExe, check.Message);
        Assert.Equal(McpDiagnosticFix.RevealBridge, check.FixId);
    }

    [Fact]
    public async Task Bridge_Missing_FailsWithNoFix()
    {
        var check = await RunOne(Healthy(bridge: null), "bridgePresent");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Contains("CanfarDesktop.McpBridge.exe", check.Message);
        Assert.Null(check.FixId);
    }

    // ── 5. Client detection ──

    [Fact]
    public async Task Clients_NeitherInstalled_BothWarn()
    {
        var checks = await Healthy(clients: new ClaudeClients(false, false)).RunAsync();
        Assert.Equal(McpDiagnosticStatus.Warn, checks.Single(c => c.Id == "claudeDesktop").Status);
        Assert.Equal(McpDiagnosticStatus.Warn, checks.Single(c => c.Id == "claudeCode").Status);
    }

    // ── 6. Claude Desktop config ──

    [Fact]
    public async Task Config_ReferencesBridge_Pass()
    {
        var check = await RunOne(Healthy(), "claudeConfig");
        Assert.Equal(McpDiagnosticStatus.Pass, check.Status);
    }

    [Fact]
    public async Task Config_BridgeMentionMatchesCaseInsensitively()
    {
        var check = await RunOne(Healthy(config: "{\"command\":\"c:\\\\x\\\\MCPBRIDGE.exe\"}"), "claudeConfig");
        Assert.Equal(McpDiagnosticStatus.Pass, check.Status);
    }

    [Fact]
    public async Task Config_Missing_FailsWithUpdateFixAndConfigPath()
    {
        var check = await RunOne(Healthy(config: null), "claudeConfig");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Contains(ConfigPath, check.Message);
        Assert.Equal(McpDiagnosticFix.UpdateConfig, check.FixId);
    }

    [Fact]
    public async Task Config_NoBridgeEntry_FailsWithUpdateFix()
    {
        var check = await RunOne(Healthy(config: "{\"mcpServers\":{\"other\":{}}}"), "claudeConfig");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Equal(McpDiagnosticFix.UpdateConfig, check.FixId);
    }

    [Fact]
    public async Task Config_NotRegisteredAndBridgeMissing_FailsWithoutFix()
    {
        var check = await RunOne(Healthy(bridge: null, config: null), "claudeConfig");
        Assert.Equal(McpDiagnosticStatus.Fail, check.Status);
        Assert.Null(check.FixId);
    }

    [Fact]
    public async Task Config_DesktopNotInstalled_WarnsSkipped()
    {
        var check = await RunOne(Healthy(clients: new ClaudeClients(false, true), config: null), "claudeConfig");
        Assert.Equal(McpDiagnosticStatus.Warn, check.Status);
        Assert.Contains("Skipped", check.Message);
        Assert.Null(check.FixId);
    }

    // ── Fix dispatch ──

    [Fact]
    public async Task Fix_EnableServer_InvokesEnableDelegate()
    {
        var enabled = false;
        await Healthy(enableAsync: () => { enabled = true; return Task.CompletedTask; })
            .ApplyFixAsync(McpDiagnosticFix.EnableServer);
        Assert.True(enabled);
    }

    [Fact]
    public async Task Fix_RestartServer_InvokesRestartDelegate()
    {
        var restarted = false;
        await Healthy(restartAsync: () => { restarted = true; return Task.CompletedTask; })
            .ApplyFixAsync(McpDiagnosticFix.RestartServer);
        Assert.True(restarted);
    }

    [Fact]
    public async Task Fix_UpdateConfig_AppliesRepairWithBridgeCommand()
    {
        string? applied = null;
        await Healthy(applyRepair: cmd => applied = cmd).ApplyFixAsync(McpDiagnosticFix.UpdateConfig);
        Assert.Equal(BridgeExe, applied);
    }

    [Fact]
    public async Task Fix_UpdateConfig_BridgeMissing_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Healthy(bridge: null).ApplyFixAsync(McpDiagnosticFix.UpdateConfig));
    }

    [Fact]
    public async Task Fix_RevealBridge_PassesBridgePath_AndSkipsWhenMissing()
    {
        string? revealed = null;
        await Healthy(reveal: p => revealed = p).ApplyFixAsync(McpDiagnosticFix.RevealBridge);
        Assert.Equal(BridgeExe, revealed);

        revealed = null;
        await Healthy(bridge: null, reveal: p => revealed = p).ApplyFixAsync(McpDiagnosticFix.RevealBridge);
        Assert.Null(revealed);
    }

    [Fact]
    public async Task Fix_UnknownId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Healthy().ApplyFixAsync("nope"));
    }

    [Fact]
    public void FixTitles_MatchTheButtonsTheRowsRender()
    {
        Assert.Equal("Enable", McpDiagnosticFix.Title(McpDiagnosticFix.EnableServer));
        Assert.Equal("Restart", McpDiagnosticFix.Title(McpDiagnosticFix.RestartServer));
        Assert.Equal("Update Config", McpDiagnosticFix.Title(McpDiagnosticFix.UpdateConfig));
        Assert.Equal("Reveal", McpDiagnosticFix.Title(McpDiagnosticFix.RevealBridge));
    }
}
