using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The opt-in MCP server panel: toggle the local named-pipe server on/off, see its run status, and
/// (on explicit confirmation) add the stdio bridge to the Claude Desktop config — or copy the
/// <c>claude mcp add</c> command for Claude Code. Resolves <see cref="McpHost"/> from DI.
/// </summary>
public sealed partial class McpServerDialog : ContentDialog
{
    private readonly McpHost _host;
    private readonly ClaudeConfigRepair _repair = new();
    private readonly string? _bridgeCommand;
    private bool _suppressToggle;

    public McpServerDialog()
    {
        InitializeComponent();
        _host = App.Services.GetRequiredService<McpHost>();
        _bridgeCommand = McpBridgeLocator.Resolve();

        _suppressToggle = true;
        EnableToggle.IsOn = _host.IsRunning;
        _suppressToggle = false;

        RefreshStatus();
        InitConnectSection();
    }

    public static Task ShowAsync(XamlRoot root)
        => new McpServerDialog { XamlRoot = root }.ShowAsync().AsTask();

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
