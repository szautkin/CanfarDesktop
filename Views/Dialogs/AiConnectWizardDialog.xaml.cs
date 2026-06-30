using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Config;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Guided "Connect an AI agent" wizard: Enable → Pick your client → Configure → Verify. A WinUI port of
/// the macOS MCPSetupWizard, built entirely over the existing Windows plumbing (<see cref="McpHost"/>,
/// <see cref="ClaudeConfigRepair"/>, <see cref="ClaudeConfigMerge"/>, <see cref="McpBridgeLocator"/>) plus
/// the new <see cref="ClaudeClientDetection"/> and <see cref="McpSelfTest"/>. Steps are panels toggled by
/// visibility; the footer drives Back/Next.
/// </summary>
public sealed partial class AiConnectWizardDialog : ContentDialog
{
    private static readonly string[] StepTitles = { "Enable", "Pick your client", "Configure", "Verify" };

    private readonly McpHost _host;
    // Target the config Claude Desktop actually reads (Store container or traditional install), on the
    // real un-redirected AppData — never our own sandboxed copy.
    private readonly ClaudeConfigRepair _repair = new(ClaudeConfigLocator.Resolve());
    private readonly string? _bridgeCommand;
    private int _step;
    private bool _suppressToggle;

    public AiConnectWizardDialog()
    {
        InitializeComponent();
        _host = App.Services.GetRequiredService<McpHost>();
        _bridgeCommand = McpBridgeLocator.Resolve();

        var clients = ClaudeClientDetection.Detect();
        DesktopRadio.Content = clients.DesktopInstalled ? "Claude Desktop (detected)" : "Claude Desktop";
        CodeRadio.Content = clients.CodeInstalled ? "Claude Code (detected)" : "Claude Code";
        // Default the selection to the detected client; prefer Desktop on a tie / nothing detected.
        if (clients.CodeInstalled && !clients.DesktopInstalled) CodeRadio.IsChecked = true;
        else DesktopRadio.IsChecked = true;

        _suppressToggle = true;
        EnableToggle.IsOn = _host.IsRunning;
        _suppressToggle = false;

        if (_bridgeCommand is not null)
            ClaudeCodeBox.Text = ClaudeConfigMerge.ClaudeCodeAddCommand(_bridgeCommand);

        RefreshStatus();
        ShowStep(0);
    }

    public static Task ShowAsync(XamlRoot root)
        => new AiConnectWizardDialog { XamlRoot = root }.ShowAsync().AsTask();

    private static Visibility Vis(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepTitles.Length - 1);
        StepHeader.Text = $"Step {_step + 1} of {StepTitles.Length} · {StepTitles[_step]}";

        EnablePanel.Visibility = Vis(_step == 0);
        ClientPanel.Visibility = Vis(_step == 1);
        ConfigurePanel.Visibility = Vis(_step == 2);
        VerifyPanel.Visibility = Vis(_step == 3);

        if (_step == 2) UpdateConfigurePanel();

        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == StepTitles.Length - 1 ? "Done" : "Next";
        ResultBar.IsOpen = false;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        // Gate the first step on the server actually being on — the rest of the flow needs it.
        if (_step == 0 && !_host.IsRunning)
        {
            ShowResult(InfoBarSeverity.Warning, "Turn on “Allow external AI agents” to continue.");
            return;
        }
        if (_step == StepTitles.Length - 1) { Hide(); return; }
        ShowStep(_step + 1);
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

    private void RefreshStatus()
    {
        StatusText.Text = _host.IsRunning ? "Running" : "Stopped";
        if (_host.IsRunning)
        {
            PipeText.Text = $"pipe: {_host.PipeName}";
            PipeText.Visibility = Visibility.Visible;
        }
        else
        {
            PipeText.Visibility = Visibility.Collapsed;
        }
    }

    private bool DesktopChosen => DesktopRadio.IsChecked == true;

    private void UpdateConfigurePanel()
    {
        DesktopConfig.Visibility = Vis(DesktopChosen);
        CodeConfig.Visibility = Vis(!DesktopChosen);

        if (_bridgeCommand is null)
        {
            ConfigIntro.Text = "The MCP bridge (CanfarDesktop.McpBridge.exe) wasn't found. Build the bridge project, then reopen this wizard.";
            AddToDesktopButton.IsEnabled = false;
            CopyCommandButton.IsEnabled = false;
            return;
        }

        ConfigIntro.Text = DesktopChosen
            ? $"Add Verbinal to Claude Desktop's config:\n{_repair.ConfigPath}\nYour other MCP servers are preserved and a .bak is kept. Restart Claude Desktop afterwards."
            : "Run this in a terminal to register Verbinal with Claude Code, then restart the CLI.";
    }

    private void OnAddToDesktopClick(object sender, RoutedEventArgs e)
    {
        if (_bridgeCommand is null) return;
        try
        {
            _repair.Apply(_bridgeCommand);
            ShowResult(InfoBarSeverity.Success, "Added to Claude Desktop. Restart it, then run the self-test on the next step.");
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

    private async void OnSelfTestClick(object sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        SelfTestProgress.IsActive = true;
        SelfTestProgress.Visibility = Visibility.Visible;
        try
        {
            var result = await McpSelfTest.RunAsync();
            if (result.Reachable)
            {
                var tools = result.ToolCount is int n ? $" {n} tool{(n == 1 ? "" : "s")} available." : "";
                ShowResult(InfoBarSeverity.Success,
                    $"Connected to the MCP server.{tools} Fully quit and reopen your AI client to finish.");
            }
            else
            {
                ShowResult(InfoBarSeverity.Error, result.Error ?? "Couldn't reach the MCP server.");
            }
        }
        finally
        {
            SelfTestProgress.IsActive = false;
            SelfTestProgress.Visibility = Visibility.Collapsed;
            SelfTestButton.IsEnabled = true;
        }
    }

    private void ShowResult(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
