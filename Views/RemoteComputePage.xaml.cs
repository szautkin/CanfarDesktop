using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.AICompute;

namespace CanfarDesktop.Views;

/// <summary>
/// Remote Compute: the person's side of <c>run_code</c>.
///
/// <para>An assistant could start a session on somebody's CANFAR account and run code in it, and the
/// only trace was a session card named verbinal-compute and files in a hidden folder. This shows the
/// session and every run — who sent it, the code, and what came back — lets the person start and stop
/// the session and run code themselves, and, until an image is configured, says what it takes.</para>
///
/// <para>The rules — which state a session status means, what may be done in each — live in
/// <see cref="ComputeStatus"/>, and the history in <see cref="IComputeRunStore"/>; this page only shows
/// them.</para>
/// </summary>
public sealed partial class RemoteComputePage : UserControl
{
    private readonly AIComputeService _compute;

    /// <summary>
    /// Re-reads the session while something is changing. Ticks cost nothing when the page is hidden or
    /// nothing is in flight — see <see cref="OnPollTick"/>.
    /// </summary>
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(10) };

    private ComputeState _state = ComputeState.NotSetUp;
    private bool _configured;

    /// <summary>The run on the detail tab, and the status its output was read at.</summary>
    private (string Id, string? Status)? _shown;

    /// <summary>Asks the window to open the Storage screen at a folder in the person's home.</summary>
    public event Action<string>? OpenFolderRequested;

    public RemoteComputePage(AIComputeService compute)
    {
        _compute = compute;
        InitializeComponent();

        var repository = new Uri(RunCodeContract.WatcherRepository);
        RepoLink.NavigateUri = repository;
        SetupRepoLink.NavigateUri = repository;

        _compute.Runs.Changed += () => DispatcherQueue.TryEnqueue(ShowRuns);
        _poll.Tick += OnPollTick;
        _poll.Start();
    }

    /// <summary>Read the session and the runs again, saying so if it fails.</summary>
    public Task RefreshAsync() => RefreshCoreAsync(quiet: false);

    private async Task RefreshCoreAsync(bool quiet)
    {
        if (!quiet) SetBusy(true);
        try
        {
            ShowSnapshot(await _compute.SnapshotAsync());
        }
        catch (Exception ex)
        {
            if (!quiet) ShowMessage(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            if (!quiet) SetBusy(false);
        }

        ShowRuns();
    }

    /// <summary>
    /// Only while the page is on screen, and only while something is changing — the session starting
    /// or stopping, or a run still out. A settled page does not ask the platform every ten seconds.
    /// </summary>
    private async void OnPollTick(object? sender, object e)
    {
        if (Parent is FrameworkElement { Visibility: not Visibility.Visible }) return;

        var changing = _state is ComputeState.Starting or ComputeState.Stopping
                       || _compute.Runs.All().Any(r => !r.IsFinished);
        if (changing) await RefreshCoreAsync(quiet: true);
    }

    // ── Where it stands ─────────────────────────────────────────────────────────────────────────

    private void ShowSnapshot(ComputeSnapshot snapshot)
    {
        _state = snapshot.State;
        _configured = snapshot.Configured;

        // A session can be on the account with nothing set up here — left from another install, or
        // from before a reinstall. It is shown with Stop, so its cores can be let go; starting and
        // running code still need the setup.
        var session = snapshot.Session is not null;

        SetupPanel.Visibility = _configured ? Visibility.Collapsed : Visibility.Visible;
        MainPanel.Visibility = _configured ? Visibility.Visible : Visibility.Collapsed;

        StartButton.Visibility = _configured ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = _configured || session ? Visibility.Visible : Visibility.Collapsed;
        OpenFolderButton.Visibility = _configured || session ? Visibility.Visible : Visibility.Collapsed;

        StartButton.IsEnabled = ComputeStatus.CanStart(_state, _configured);
        StopButton.IsEnabled = ComputeStatus.CanStop(_state);
        RunButton.IsEnabled = ComputeStatus.CanRun(_state, _configured);

        // Said once, and never over another message: an error the person has not read yet matters more.
        if (!_configured && session && !MessageBar.IsOpen)
            ShowMessage(InfoBarSeverity.Informational, Loc.T("Compute_SessionWithoutSetup"));

        StatusText.Text = Describe(snapshot);

        // Named by what it says, so a screen reader reads the state and an agent can point at it.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StatusText, StatusText.Text);
    }

    private static string Describe(ComputeSnapshot s)
    {
        var state = Loc.T($"Compute_State_{s.State}");
        if (s.State != ComputeState.Running) return state;

        var up = ComputeStatus.Uptime(s.Session?.StartedTime, DateTimeOffset.UtcNow);
        return up is { } u
            ? Loc.F("Compute_StatusRunning", state, Cores(s.Cores), s.Ram, Uptime(u))
            : Loc.F("Compute_StatusRunningNoUptime", state, Cores(s.Cores), s.Ram);
    }

    private static string Cores(int n) => Loc.F(n == 1 ? "Compute_CoreCountOne" : "Compute_CoreCountMany", n);

    private static string Uptime(TimeSpan up)
        => up.TotalMinutes < 60
            ? Loc.F("Compute_UptimeMinutes", (int)up.TotalMinutes)
            : Loc.F("Compute_UptimeHours", (int)up.TotalHours, up.Minutes);

    // ── The runs ────────────────────────────────────────────────────────────────────────────────

    private void ShowRuns()
    {
        var runs = _compute.Runs.All();
        var selected = (RunList.SelectedItem as ListViewItem)?.Tag as string;

        RunList.SelectionChanged -= OnRunSelected;
        RunList.Items.Clear();
        foreach (var run in runs) RunList.Items.Add(Row(run));
        RunList.SelectedItem = RunList.Items.OfType<ListViewItem>().FirstOrDefault(i => Equals(i.Tag, selected));
        RunList.SelectionChanged += OnRunSelected;

        EmptyHistory.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The run on screen may just have finished: its output is there to read now.
        if (selected is not null && runs.FirstOrDefault(r => r.Id == selected) is { } current
            && _shown is { } shown && shown.Id == current.Id && shown.Status != current.Status)
            _ = ShowRunAsync(current);
    }

    private static ListViewItem Row(ComputeRun run)
    {
        var who = Loc.T(run.Author == ComputeRunAuthor.Agent ? "Compute_ByAssistant" : "Compute_ByYou");
        var title = $"{who} · {run.Language} · {RunState(run)}";
        var firstLine = run.Code.Split('\n', 2)[0].Trim();
        if (firstLine.Length > 60) firstLine = firstLine[..60] + "…";
        var when = DateTimeOffset.TryParse(run.SubmittedAt, out var at) ? at.ToLocalTime().ToString("g") : run.SubmittedAt;

        var panel = new StackPanel { Spacing = 2, Padding = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        panel.Children.Add(new TextBlock
        {
            Text = $"{when} · {firstLine}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var item = new ListViewItem { Tag = run.Id, Content = panel };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, $"{title}, {when}");
        return item;
    }

    private static string RunState(ComputeRun run)
        => run.State switch
        {
            ComputeRun.Running => Loc.T("Compute_RunState_running"),
            "ok" => Loc.T("Compute_RunState_ok"),
            "error" => Loc.T("Compute_RunState_error"),
            "timeout" => Loc.T("Compute_RunState_timeout"),
            ComputeRun.NoResult => Loc.T("Compute_RunState_noResult"),
            ComputeRun.NotSent => Loc.T("Compute_RunState_notSent"),
            var other => other,
        };

    private async void OnRunSelected(object sender, SelectionChangedEventArgs e)
    {
        if ((RunList.SelectedItem as ListViewItem)?.Tag is string id && _compute.Runs.Find(id) is { } run)
        {
            ComputePivot.SelectedItem = DetailTab;
            await ShowRunAsync(run);
        }
    }

    /// <summary>The run's code at once, and its output as soon as it can be read from storage.</summary>
    private async Task ShowRunAsync(ComputeRun run)
    {
        _shown = (run.Id, run.Status);
        CodeView.Text = run.Code;
        CopyCodeButton.IsEnabled = true;
        RunAgainButton.IsEnabled = ComputeStatus.CanRun(_state, _configured);
        RunMeta.Text = Meta(run, truncated: false);
        ErrorView.Text = string.Empty;

        switch (run.Status)
        {
            case null:
                OutputView.Text = Loc.T("Compute_Waiting");
                return;
            case ComputeRun.NoResult:
                OutputView.Text = Loc.T("Compute_NoResultHelp");
                return;
            case ComputeRun.NotSent:
                OutputView.Text = Loc.T("Compute_NotSentHelp");
                return;
        }

        OutputView.Text = Loc.T("Compute_LoadingOutput");
        try
        {
            var result = await _compute.FetchOutAsync(run.Id);
            if (_shown?.Id != run.Id) return;   // somebody picked another run meanwhile

            OutputView.Text = result?.DecodedStdout() ?? string.Empty;
            ErrorView.Text = result?.DecodedStderr() ?? string.Empty;
            RunMeta.Text = Meta(run, result?.Truncated == true);
        }
        catch (Exception ex)
        {
            if (_shown?.Id == run.Id) OutputView.Text = ex.Message;
        }
    }

    private static string Meta(ComputeRun run, bool truncated)
    {
        var parts = new List<string> { RunState(run) };
        if (run.ExitCode is { } code) parts.Add(Loc.F("Compute_ExitCode", code));
        if (run.DurationMs is { } ms) parts.Add(Loc.F("Compute_Duration", (ms / 1000.0).ToString("0.#")));
        if (truncated) parts.Add(Loc.T("Compute_Truncated"));
        return string.Join(" · ", parts);
    }

    private void OnCopyCodeClick(object sender, RoutedEventArgs e)
    {
        ClipboardText.Copy(CodeView.Text, Loc.T("RemoteCompute_CodeCopied"));
    }

    private async void OnRunAgainClick(object sender, RoutedEventArgs e)
    {
        if (_shown is { } shown && _compute.Runs.Find(shown.Id) is { } run)
            await SubmitAsync(run.Language, run.Code, run.TimeoutSeconds);
    }

    // ── Running code ────────────────────────────────────────────────────────────────────────────

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        var code = SnippetBox.Text;
        if (string.IsNullOrWhiteSpace(code)) return;

        await SubmitAsync(SnippetLanguage(), code, SnippetTimeout());
    }

    /// <summary>
    /// Send code as the person — the same path an assistant's run_code takes once applied, recorded
    /// as theirs rather than the assistant's.
    /// </summary>
    private async Task SubmitAsync(string? language, string code, int timeout)
    {
        if (!_compute.IsSignedIn)
        {
            ShowMessage(InfoBarSeverity.Warning, Loc.T("Compute_SignInFirst"));
            return;
        }

        var request = new RunCodeRequest(
            Guid.NewGuid().ToString("N"),
            RunCodeContract.NormalizeLanguage(language),
            code,
            RunCodeContract.ClampTimeout(timeout));

        SetBusy(true);
        try
        {
            await _compute.SubmitAsync(request, ComputeRunAuthor.User);
            ShowRuns();
            RunList.SelectedItem = RunList.Items.OfType<ListViewItem>().FirstOrDefault(i => Equals(i.Tag, request.Id));
            await RefreshCoreAsync(quiet: true);
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── The session ─────────────────────────────────────────────────────────────────────────────

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (!_compute.IsSignedIn)
        {
            ShowMessage(InfoBarSeverity.Warning, Loc.T("Compute_SignInFirst"));
            return;
        }

        SetBusy(true);
        try
        {
            await _compute.EnsureSessionAsync();
            ShowMessage(InfoBarSeverity.Informational, Loc.T("Compute_Starting"));
            await RefreshCoreAsync(quiet: true);
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Stopping deletes the session, and anything still running with it — so it asks first.</summary>
    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Compute_StopConfirmTitle"),
            Content = Loc.T("Compute_StopConfirmBody"),
            PrimaryButtonText = Loc.T("Compute_StopConfirmYes"),
            CloseButtonText = Loc.T("Compute_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            await _compute.StopAsync();
            await RefreshCoreAsync(quiet: true);
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        await Dialogs.AIComputeSettingsDialog.ShowAsync(XamlRoot);
        await RefreshAsync();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(RunCodeContract.ExecDir);

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    // ── For an agent: the same screen, read and set ─────────────────────────────────────────────

    /// <summary>What the screen shows, for get_compute_view and as every agent action's answer.</summary>
    public ComputeScreenView Capture(bool shown, string? message = null)
    {
        var selected = _shown is { } s ? _compute.Runs.Find(s.Id) : null;

        return new ComputeScreenView(
            shown,
            ComputeStatus.Name(_state),
            ReferenceEquals(ComputePivot.SelectedItem, RunTab) ? "code" : "run",
            selected is null ? null : ComputeRunDetail.From(selected),
            new ComputeSnippetView(SnippetLanguage(), SnippetTimeout(), RunCodeContract.NormalizeNewlines(SnippetBox.Text)),
            message);
    }

    /// <summary>The Run code tab's language and timeout, as the person has them set.</summary>
    private string SnippetLanguage()
        => (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? RunCodeContract.DefaultLanguage;

    private int SnippetTimeout()
        => double.IsNaN(TimeoutBox.Value) ? RunCodeContract.DefaultTimeoutSeconds : (int)TimeoutBox.Value;

    /// <summary>Select a run — the newest when none is named — and show its details, as a click would.</summary>
    public async Task<ComputeScreenView> ShowRunAsync(string? executionId)
    {
        var run = executionId is null ? _compute.Runs.All().FirstOrDefault() : _compute.Runs.Find(executionId);
        if (run is null)
            return Capture(true, executionId is null ? "nothing has run yet" : $"no run {executionId} — see list_compute_runs");

        RunList.SelectionChanged -= OnRunSelected;
        RunList.SelectedItem = RunList.Items.OfType<ListViewItem>().FirstOrDefault(i => Equals(i.Tag, run.Id));
        RunList.SelectionChanged += OnRunSelected;
        if (RunList.SelectedItem is not null) RunList.ScrollIntoView(RunList.SelectedItem);

        ComputePivot.SelectedItem = DetailTab;
        await ShowRunAsync(run);
        return Capture(true);
    }

    /// <summary>Put code in the Run code box for the person to run, and show it. Never runs it.</summary>
    public ComputeScreenView SetSnippet(ComputeSnippetRequest request)
    {
        LanguageCombo.SelectedItem = LanguageCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => Equals(i.Tag, request.Language)) ?? LanguageCombo.Items[0];
        TimeoutBox.Value = request.TimeoutSeconds;
        SnippetBox.Text = request.Code;
        ComputePivot.SelectedItem = RunTab;

        return Capture(true, _configured
            ? null
            : "remote compute is not set up, so the person sees the setup steps rather than the box");
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────────────────────

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        RefreshButton.IsEnabled = !busy;
    }

    private void ShowMessage(InfoBarSeverity severity, string message)
    {
        MessageBar.Severity = severity;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }
}
