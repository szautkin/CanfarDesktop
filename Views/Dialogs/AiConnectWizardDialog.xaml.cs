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
    // Instance, not static: Loc.T at type-init would freeze the titles for the process lifetime,
    // outliving a mid-session language override.
    private readonly string[] StepTitles =
    {
        Helpers.Loc.T("Wizard_StepEnable"),
        Helpers.Loc.T("Wizard_StepClient"),
        Helpers.Loc.T("Wizard_StepConfigure"),
        Helpers.Loc.T("Wizard_StepVerify"),
    };

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
        _bridgeCommand = McpBridgeLocator.ResolveStable();

        var clients = ClaudeClientDetection.Detect();
        DesktopRadio.Content = Helpers.Loc.T(clients.DesktopInstalled ? "Wizard_ClaudeDesktopDetected" : "Wizard_ClaudeDesktop");
        CodeRadio.Content = Helpers.Loc.T(clients.CodeInstalled ? "Wizard_ClaudeCodeDetected" : "Wizard_ClaudeCode");
        // Default the selection to the detected client; prefer Desktop on a tie / nothing detected.
        if (clients.CodeInstalled && !clients.DesktopInstalled) CodeRadio.IsChecked = true;
        else DesktopRadio.IsChecked = true;

        _suppressToggle = true;
        EnableToggle.IsOn = _host.IsRunning;
        _suppressToggle = false;

        if (_bridgeCommand is not null)
            ClaudeCodeBox.Text = ClaudeConfigMerge.ClaudeCodeAddCommand(_bridgeCommand);

        RefreshStatus();

        // Resume where the user left off: skip Enable when the server is already
        // on, and jump straight to Verify when the client config already
        // references the bridge.
        if (_host.IsRunning)
        {
            ShowStep(_repair.IsBridgeRegistered() ? 3 : 1);
        }
        else
        {
            ShowStep(0);
        }
    }

    public static async Task ShowAsync(XamlRoot root)
    {
        var wizard = new AiConnectWizardDialog { XamlRoot = root };
        await wizard.ShowAsync().AsTask();

        // Once the wizard is gone, not from inside it: signed out, Remote Compute asks the person to sign
        // in first, and only one dialog can be open at a time. The same navigation navigate_to uses, since
        // three different places open that screen.
        if (wizard._openRemoteCompute)
            await App.Services.GetRequiredService<Mcp.AppViewStateService>().NavigateAsync("remoteCompute");
    }

    /// <summary>The person chose to open Remote Compute from the wizard.</summary>
    private bool _openRemoteCompute;

    private static Visibility Vis(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepTitles.Length - 1);
        StepHeader.Text = Helpers.Loc.F("Wizard_StepHeader", _step + 1, StepTitles.Length, StepTitles[_step]);

        EnablePanel.Visibility = Vis(_step == 0);
        ClientPanel.Visibility = Vis(_step == 1);
        ConfigurePanel.Visibility = Vis(_step == 2);
        VerifyPanel.Visibility = Vis(_step == 3);

        if (_step == 2) UpdateConfigurePanel();

        BackButton.IsEnabled = _step > 0;
        NextButton.Content = Helpers.Loc.T(_step == StepTitles.Length - 1 ? "Wizard_Done" : "Wizard_Next");
        ResultBar.IsOpen = false;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    // The last enable/disable request, so Next can await it instead of reading
    // IsRunning while the toggle is still being applied.
    private Task _pendingEnable = Task.CompletedTask;

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        // Gate the first step on the server actually being on — the rest of the flow needs it.
        if (_step == 0)
        {
            NextButton.IsEnabled = false;
            try { await _pendingEnable; }
            catch { /* surfaced by the toggle handler */ }
            finally { NextButton.IsEnabled = true; }

            if (!_host.IsRunning)
            {
                ShowResult(InfoBarSeverity.Warning, Helpers.Loc.T("Wizard_TurnOnToContinue"));
                return;
            }
        }
        if (_step == StepTitles.Length - 1) { Hide(); return; }
        ShowStep(_step + 1);
    }

    private async void OnEnableToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        var pending = _host.SetEnabledAsync(EnableToggle.IsOn);
        _pendingEnable = pending;
        try
        {
            await pending;
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error,
                Helpers.Loc.F(EnableToggle.IsOn ? "Wizard_CouldntStart" : "Wizard_CouldntStop", ex.Message));
        }
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        StatusText.Text = Helpers.Loc.T(_host.IsRunning ? "Wizard_Running" : "Wizard_Stopped");
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
            ConfigIntro.Text = Helpers.Loc.T("Wizard_BridgeMissing");
            AddToDesktopButton.IsEnabled = false;
            CopyCommandButton.IsEnabled = false;
            return;
        }

        ConfigIntro.Text = DesktopChosen
            ? Helpers.Loc.F("Wizard_DesktopIntro", _repair.ConfigPath)
            : Helpers.Loc.T("Wizard_CodeIntro");
    }

    private void OnAddToDesktopClick(object sender, RoutedEventArgs e)
    {
        if (_bridgeCommand is null) return;

        // MSIX virtualization would sandbox our write to a traditional-install config (Claude would
        // never see it) — be honest: hand the user the merged JSON and the path instead.
        if (_repair.RequiresManualEdit)
        {
            if (Helpers.ClipboardText.Copy(_repair.Preview(_bridgeCommand)))
                ShowResult(InfoBarSeverity.Warning, Helpers.Loc.F("Mcp_ManualConfigCopied", _repair.ConfigPath));
            else
                ShowResult(InfoBarSeverity.Error, Helpers.Loc.T("Clipboard_Failed"));
            return;
        }

        try
        {
            _repair.Apply(_bridgeCommand);
            ShowResult(InfoBarSeverity.Success, Helpers.Loc.T("Wizard_AddedToDesktop"));
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, Helpers.Loc.F("Wizard_ConfigWriteFailed", ex.Message));
        }
    }

    private void OnCopyCommandClick(object sender, RoutedEventArgs e)
    {
        if (Helpers.ClipboardText.Copy(ClaudeCodeBox.Text))
            ShowResult(InfoBarSeverity.Informational, Helpers.Loc.T("Wizard_CommandCopied"));
        else
            ShowResult(InfoBarSeverity.Error, Helpers.Loc.T("Clipboard_Failed"));
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
                var tools = result.ToolCount is int n
                    ? Helpers.Loc.F(n == 1 ? "Wizard_ToolsOne" : "Wizard_ToolsMany", n)
                    : "";
                ShowResult(InfoBarSeverity.Success, Helpers.Loc.F("Wizard_Connected", tools));
                OfferRemoteCompute();
            }
            else
            {
                ShowResult(InfoBarSeverity.Error, result.Error ?? Helpers.Loc.T("Wizard_Unreachable"));
            }
        }
        finally
        {
            SelfTestProgress.IsActive = false;
            SelfTestProgress.Visibility = Visibility.Collapsed;
            SelfTestButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Once the assistant is connected, say that it can also run code on CANFAR — worded for whether that
    /// is already set up. A line, not a step: most people connecting an assistant do not need it.
    /// </summary>
    private void OfferRemoteCompute()
    {
        var ready = App.Services.GetRequiredService<Services.AICompute.AIComputeService>().IsConfigured;
        ComputeHintText.Text = Helpers.Loc.T(ready ? "Wizard_ComputeHintReady" : "Wizard_ComputeHintSetUp");
        ComputeLink.Content = Helpers.Loc.T(ready ? "Wizard_ComputeLinkReady" : "Wizard_ComputeLinkSetUp");
        ComputeHint.Visibility = Visibility.Visible;
    }

    /// <summary>Close the wizard, and open Remote Compute once it has closed (see <see cref="ShowAsync(XamlRoot)"/>).</summary>
    private void OnComputeLinkClick(object sender, RoutedEventArgs e)
    {
        _openRemoteCompute = true;
        Hide();
    }

    private void ShowResult(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
