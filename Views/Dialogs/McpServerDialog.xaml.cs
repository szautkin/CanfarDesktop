using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The opt-in MCP server panel: toggle the local named-pipe server on/off, see its run status, manage
/// agent autonomy (auto-apply + follow-activity), review recent agent activity, and (on explicit
/// confirmation) add the stdio bridge to the Claude Desktop config. Resolves <see cref="McpHost"/> from DI.
/// </summary>
public sealed partial class McpServerDialog : ContentDialog
{
    private readonly McpHost _host;
    private readonly McpSettingsService _settings;
    // Target the config Claude Desktop actually reads (its Store container or a traditional install),
    // resolved on the real un-redirected AppData — never our own sandboxed copy.
    private readonly ClaudeConfigRepair _repair = new(ClaudeConfigLocator.Resolve());
    private readonly string? _bridgeCommand;
    private bool _suppressToggle;

    public McpServerDialog()
    {
        InitializeComponent();
        _host = App.Services.GetRequiredService<McpHost>();
        _settings = App.Services.GetRequiredService<McpSettingsService>();
        _bridgeCommand = McpBridgeLocator.Resolve();

        _suppressToggle = true;
        EnableToggle.IsOn = _host.IsRunning;
        AutoApplyToggle.IsOn = _settings.AutoApplyEnabled;
        FollowActivityToggle.IsOn = _settings.FollowAgentActivityEnabled;
        _suppressToggle = false;

        RefreshStatus();
        InitConnectSection();
        LoadActivity();
    }

    public static Task ShowAsync(XamlRoot root)
        => new McpServerDialog { XamlRoot = root }.ShowAsync().AsTask();

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
                AgentActivityOutcome.Applied => 0xE73E,    // CheckMark
                AgentActivityOutcome.Rejected => 0xE711,   // Cancel
                AgentActivityOutcome.Withdrawn => 0xE7A7,  // Undo
                _ => 0xE946,                               // Info (live op)
            }),
            Summary = e.Summary,
            Subtitle = string.Join(" · ",
                new[]
                {
                    e.Kind,
                    $"{e.OriginLabel} ({e.OriginFingerprint})",
                    e.AutoApplied ? "auto" : null,
                    e.Timestamp.ToLocalTime().ToString("t"),
                }.Where(s => !string.IsNullOrEmpty(s))),
        };
    }

    private void RefreshStatus()
    {
        if (_host.IsRunning)
        {
            StatusText.Text = "Running";
            PipeText.Text = $"pipe: {_host.PipeName}";
            PipeText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Text = "Stopped";
            PipeText.Visibility = Visibility.Collapsed;
        }
    }

    private void InitConnectSection()
    {
        if (_bridgeCommand is null)
        {
            ConnectButton.IsEnabled = false;
            CopyCommandButton.IsEnabled = false;
            ConnectIntro.Text = "The MCP bridge (CanfarDesktop.McpBridge.exe) wasn't found. Build the bridge project, then reopen this dialog.";
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
            ShowResult(InfoBarSeverity.Error, $"Couldn't {(EnableToggle.IsOn ? "start" : "stop")} the server: {ex.Message}");
        }
        RefreshStatus();
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_bridgeCommand is null) return;
        ConfirmTargetText.Text = $"Verbinal will be added to:\n{_repair.ConfigPath}";
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
            ShowResult(InfoBarSeverity.Success, "Added to Claude Desktop. Restart Claude Desktop to pick it up.");
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, $"Couldn't write the config: {ex.Message}");
        }
    }

    private void OnCopyCommandClick(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(ClaudeCodeBox.Text);
        Clipboard.SetContent(data);
        ShowResult(InfoBarSeverity.Informational, "Command copied.");
    }

    private void ShowResult(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
