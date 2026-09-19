using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The MCP server settings as a self-contained panel: toggle the local named-pipe server, agent autonomy
/// (auto-apply + follow-activity), the AI-Guide tile, connect Claude Desktop/Code, manage connected-client
/// approval, and review recent activity. Resolves its services from DI so it embeds both in the standalone
/// <see cref="McpServerDialog"/> and the unified Settings window. All settings persist live (no Save).
/// </summary>
public sealed partial class McpServerSettingsPanel : UserControl
{
    private readonly McpHost _host;
    private readonly McpSettingsService _settings;
    private readonly McpClientApprovalStore _approval;
    private readonly ClaudeConfigRepair _repair = new(ClaudeConfigLocator.Resolve());
    private readonly string? _bridgeCommand;
    private bool _suppressToggle;

    public McpServerSettingsPanel()
    {
        InitializeComponent();
        _host = App.Services.GetRequiredService<McpHost>();
        _settings = App.Services.GetRequiredService<McpSettingsService>();
        _approval = App.Services.GetRequiredService<McpClientApprovalStore>();
        _bridgeCommand = McpBridgeLocator.ResolveStable();

        _suppressToggle = true;
        EnableToggle.IsOn = _host.IsRunning;
        AutoApplyToggle.IsOn = _settings.AutoApplyEnabled;
        FollowActivityToggle.IsOn = _settings.FollowAgentActivityEnabled;
        ShowAiGuideToggle.IsOn = _settings.ShowAiGuideTile;
        RequireApprovalToggle.IsOn = _approval.RequireApproval;
        _suppressToggle = false;

        RefreshStatus();
        InitConnectSection();
        LoadActivity();
        LoadClients();
    }

    private void OnAutoApplyToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        _settings.AutoApplyEnabled = AutoApplyToggle.IsOn;
    }

    private void OnFollowActivityToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        _settings.FollowAgentActivityEnabled = FollowActivityToggle.IsOn;
    }

    private void OnShowAiGuideToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        _settings.ShowAiGuideTile = ShowAiGuideToggle.IsOn;
        // Landing tiles are built once; the change takes effect next launch / landing rebuild.
    }

    private void LoadActivity()
    {
        var rows = _host.Activity.Recent(25).Select(ActivityRow.From).ToList();
        ActivityList.ItemsSource = rows;
        NoActivityText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Display row for one agent-activity entry.</summary>
    public sealed class ActivityRow
    {
        public string Glyph { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;

        public static ActivityRow From(AgentActivityEntry e) => new()
        {
            Glyph = char.ConvertFromUtf32(e.Outcome switch
            {
                AgentActivityOutcome.Applied => 0xE73E,
                AgentActivityOutcome.Rejected => 0xE711,
                AgentActivityOutcome.Withdrawn => 0xE7A7,
                _ => 0xE946,
            }),
            Summary = e.Summary,
            Subtitle = string.Join(" · ",
                new[]
                {
                    e.Kind,
                    $"{e.OriginLabel} ({e.OriginFingerprint})",
                    e.AutoApplied ? Helpers.Loc.T("Mcp_AutoLabel") : null,
                    e.Timestamp.ToLocalTime().ToString("t"),
                }.Where(s => !string.IsNullOrEmpty(s))),
        };
    }

    private void RefreshStatus()
    {
        if (_host.IsRunning)
        {
            StatusText.Text = Helpers.Loc.T("Mcp_StatusRunning");
            PipeText.Text = Helpers.Loc.F("Mcp_PipeLabel", _host.PipeName);
            PipeText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Text = Helpers.Loc.T("Mcp_StatusStopped");
            PipeText.Visibility = Visibility.Collapsed;
        }
    }

    private void InitConnectSection()
    {
        if (_bridgeCommand is null)
        {
            ConnectButton.IsEnabled = false;
            CopyCommandButton.IsEnabled = false;
            ConnectIntro.Text = Helpers.Loc.T("Mcp_BridgeNotFound");
            ClaudeCodeBox.Text = string.Empty;
            return;
        }

        ClaudeCodeBox.Text = ClaudeConfigMerge.ClaudeCodeAddCommand(_bridgeCommand);
    }

    private async void OnEnableToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        try
        {
            await _host.SetEnabledAsync(EnableToggle.IsOn);
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, EnableToggle.IsOn
                ? Helpers.Loc.F("Mcp_CouldntStartServer", ex.Message)
                : Helpers.Loc.F("Mcp_CouldntStopServer", ex.Message));
        }
        RefreshStatus();
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_bridgeCommand is null) return;

        // MSIX virtualization would sandbox our write to a traditional-install config (Claude would
        // never see it) — be honest: hand the user the merged JSON and the path instead.
        if (_repair.RequiresManualEdit)
        {
            var data = new DataPackage();
            data.SetText(_repair.Preview(_bridgeCommand));
            Clipboard.SetContent(data);
            ShowResult(InfoBarSeverity.Warning, Helpers.Loc.F("Mcp_ManualConfigCopied", _repair.ConfigPath));
            return;
        }

        ConfirmTargetText.Text = Helpers.Loc.T("Mcp_ConfirmTarget") + "\n" + _repair.ConfigPath;
        ConfirmCommandText.Text = $"{_bridgeCommand} mcp";
        ResultBar.IsOpen = false;
        ConfirmPanel.Visibility = Visibility.Visible;
    }

    private void OnConfirmCancelClick(object sender, RoutedEventArgs e)
        => ConfirmPanel.Visibility = Visibility.Collapsed;

    private void OnConfirmWriteClick(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        try
        {
            _repair.Apply(_bridgeCommand!);
            ShowResult(InfoBarSeverity.Success, Helpers.Loc.T("Mcp_AddedToDesktop"));
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, Helpers.Loc.F("Mcp_ConfigWriteFailed", ex.Message));
        }
    }

    private void OnCopyCommandClick(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(ClaudeCodeBox.Text);
        Clipboard.SetContent(data);
        ShowResult(InfoBarSeverity.Informational, Helpers.Loc.T("Mcp_CommandCopied"));
    }

    private void OnRequireApprovalToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        _approval.RequireApproval = RequireApprovalToggle.IsOn;
    }

    private void LoadClients()
    {
        var rows = _approval.SeenClients().Select(c => ClientRow.From(c, _approval.IsApproved(c.ClientId))).ToList();
        ClientsList.ItemsSource = rows;
        NoClientsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClientActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string clientId } || string.IsNullOrEmpty(clientId)) return;
        if (_approval.IsApproved(clientId)) _approval.Revoke(clientId);
        else _approval.Approve(clientId);
        LoadClients();
    }

    /// <summary>Display row for one connected client.</summary>
    public sealed class ClientRow
    {
        public string ClientId { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string ActionLabel { get; init; } = string.Empty;

        public static ClientRow From(McpSeenClient c, bool approved) => new()
        {
            ClientId = c.ClientId,
            Subtitle = (c.ConnectCount == 1
                           ? Helpers.Loc.F("Mcp_ClientSubtitleOne", c.LastSeen.ToLocalTime().ToString("t"))
                           : Helpers.Loc.F("Mcp_ClientSubtitleMany", c.ConnectCount, c.LastSeen.ToLocalTime().ToString("t")))
                       + (approved ? " · " + Helpers.Loc.T("Mcp_ApprovedLabel") : ""),
            ActionLabel = approved ? Helpers.Loc.T("Mcp_RevokeAction") : Helpers.Loc.T("Mcp_ApproveAction"),
        };
    }

    // ── Diagnostics ──

    private McpDiagnosticsRunner? _diagnostics;

    private McpDiagnosticsRunner Diagnostics => _diagnostics ??= new McpDiagnosticsRunner
    {
        ServerEnabled = () => _settings.Enabled,
        ListenerRunning = () => _host.IsRunning,
        PipeName = () => _host.PipeName,
        SelfTest = McpSelfTest.RunAsync,
        BridgePath = () => _bridgeCommand,
        DetectClients = ClaudeClientDetection.Detect,
        ReadClaudeConfig = _repair.ReadExisting,
        ClaudeConfigPath = _repair.ConfigPath,
        EnableServerAsync = () => _host.SetEnabledAsync(true),
        RestartServerAsync = async () =>
        {
            await _host.SetEnabledAsync(false);
            await _host.SetEnabledAsync(true);
        },
        ApplyConfigRepair = _repair.Apply,
        RevealBridge = path => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true,
        }),
    };

    private async void OnRunDiagnosticsClick(object sender, RoutedEventArgs e) => await RunDiagnosticsAsync();

    private async Task RunDiagnosticsAsync()
    {
        RunDiagnosticsButton.IsEnabled = false;
        DiagnosticsList.ItemsSource = new[]
        {
            DiagnosticRow.From(new McpDiagnosticCheck(
                "running", Helpers.Loc.T("Mcp_DiagRunningTitle"), McpDiagnosticStatus.Running, Helpers.Loc.T("Mcp_DiagRunningMessage"))),
        };
        try
        {
            var checks = await Diagnostics.RunAsync();
            DiagnosticsList.ItemsSource = checks.Select(DiagnosticRow.From).ToList();
        }
        finally
        {
            RunDiagnosticsButton.IsEnabled = true;
        }
        // A restart/enable fix may have changed the server state shown above.
        _suppressToggle = true;
        EnableToggle.IsOn = _host.IsRunning;
        _suppressToggle = false;
        RefreshStatus();
    }

    private async void OnDiagnosticFixClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fixId } || string.IsNullOrEmpty(fixId)) return;
        try
        {
            await Diagnostics.ApplyFixAsync(fixId);
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, Helpers.Loc.F("Mcp_FixFailed", ex.Message));
        }
        if (fixId != McpDiagnosticFix.RevealBridge) await RunDiagnosticsAsync();
    }

    /// <summary>Display row for one diagnostic check.</summary>
    public sealed class DiagnosticRow
    {
        public string Glyph { get; init; } = string.Empty;
        public Brush? GlyphBrush { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string? FixId { get; init; }
        public string FixLabel { get; init; } = string.Empty;
        public Visibility FixVisibility => FixId is null ? Visibility.Collapsed : Visibility.Visible;

        public static DiagnosticRow From(McpDiagnosticCheck c) => new()
        {
            Glyph = char.ConvertFromUtf32(c.Status switch
            {
                McpDiagnosticStatus.Pass => 0xE73E,
                McpDiagnosticStatus.Warn => 0xE7BA,
                McpDiagnosticStatus.Fail => 0xE711,
                _ => 0xE895,
            }),
            GlyphBrush = ThemeBrush(c.Status switch
            {
                McpDiagnosticStatus.Pass => "SystemFillColorSuccessBrush",
                McpDiagnosticStatus.Warn => "SystemFillColorCautionBrush",
                McpDiagnosticStatus.Fail => "SystemFillColorCriticalBrush",
                _ => "TextFillColorSecondaryBrush",
            }),
            Title = c.Title,
            Message = c.Message,
            FixId = c.FixId,
            FixLabel = c.FixId is null ? string.Empty : McpDiagnosticFix.Title(c.FixId),
        };

        private static Brush? ThemeBrush(string key)
            => Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
    }

    private void ShowResult(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
