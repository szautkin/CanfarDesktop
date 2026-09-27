using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views.Controls;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views;

public sealed partial class DashboardPage : Page
{
    private readonly SessionListControl _sessionList;
    private readonly LaunchFormControl _launchForm;
    private readonly PlatformLoadControl _platformLoad;
    private readonly StorageQuotaControl _storageQuota;
    private readonly BatchJobsControl _batchJobs;
    private readonly RecentLaunchesControl _recentLaunches;
    private readonly CanfarImagesControl _canfarImages;
    private readonly SessionListViewModel _sessionListVm;
    private readonly SessionLaunchViewModel _sessionLaunchVm;

    /// <summary>The launch form's dialog, made the first time it opens and kept, with what was typed in it.</summary>
    private LaunchFormDialog? _launchDialog;

    /// <summary>The arrangement the cards are in now.</summary>
    private IReadOnlyDictionary<PortalCard, PortalCell>? _layout;

    public DashboardPage(
        SessionListViewModel sessionListVm,
        SessionLaunchViewModel sessionLaunchVm,
        PlatformLoadViewModel platformLoadVm,
        StorageViewModel storageVm,
        IRecentLaunchService recentLaunchService,
        ISessionService sessionService,
        IImageService imageService,
        ImageDiscoveryCoordinator imageDiscoveryCoordinator,
        ImageDiscoverySettingsService imageDiscoverySettings,
        IUserImageStore userImages,
        IJobHistoryStore? jobHistory = null)
    {
        InitializeComponent();

        _sessionListVm = sessionListVm;
        _sessionLaunchVm = sessionLaunchVm;
        _sessionList = new SessionListControl(sessionListVm);
        _launchForm = new LaunchFormControl(sessionLaunchVm);
        _platformLoad = new PlatformLoadControl(platformLoadVm);
        _storageQuota = new StorageQuotaControl(storageVm);
        _batchJobs = new BatchJobsControl(sessionService, jobHistory);
        _recentLaunches = new RecentLaunchesControl(recentLaunchService);
        _canfarImages = new CanfarImagesControl(imageService, imageDiscoveryCoordinator, imageDiscoverySettings, userImages);

        SessionListContainer.Child = _sessionList;
        PlatformLoadContainer.Child = _platformLoad;
        StorageContainer.Child = _storageQuota;
        BatchJobsContainer.Child = _batchJobs;
        RecentLaunchesContainer.Child = _recentLaunches;
        CanfarImagesContainer.Child = _canfarImages;
        Arrange(PortalLayout.Wide);

        // Wire up session counter for name generation
        sessionLaunchVm.SetSessionCounter(type =>
            _sessionListVm.Sessions.Count(s => s.SessionType == type));

        // Wire up total session counter for launch limit
        sessionLaunchVm.SetTotalSessionCounter(() => _sessionListVm.Sessions.Count);

        // Wire up events
        _sessionList.SessionOpenRequested += OnSessionOpen;
        _sessionList.SessionDeleteRequested += OnSessionDelete;
        _sessionList.SessionRenewRequested += OnSessionRenew;
        _sessionList.SessionEventsRequested += OnSessionEvents;
        _sessionList.LaunchSessionRequested += (_, _) => ShowLaunchForm(null);
        _launchForm.LaunchRequested += OnLaunchRequested;
        _launchForm.HeadlessLaunchRequested += OnHeadlessLaunchRequested;
        _recentLaunches.RelaunchRequested += OnRelaunchRequested;
        _canfarImages.UseImageRequested += OnUseImageRequested;

        // Update session limit whenever sessions are refreshed (including by polling)
        _sessionListVm.SessionsRefreshed += (_, _) =>
        {
            _sessionLaunchVm.UpdateSessionLimit();
            _recentLaunches.UpdateSessionLimit(_sessionLaunchVm.IsAtSessionLimit);
        };
    }

    // ── Where the cards go ──────────────────────────────────────────────────────────────────────

    private void OnPortalSizeChanged(object sender, SizeChangedEventArgs e) => Arrange(PortalLayout.For(e.NewSize.Width));

    /// <summary>Put each card in its cell — only when the arrangement changes, not on every resize.</summary>
    private void Arrange(IReadOnlyDictionary<PortalCard, PortalCell> layout)
    {
        if (ReferenceEquals(layout, _layout)) return;
        _layout = layout;

        // As many rows as the arrangement uses: an empty row still takes the grid's spacing.
        PortalGrid.RowDefinitions.Clear();
        for (var row = 0; row < PortalLayout.Rows(layout); row++)
            PortalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var (card, cell) in layout)
        {
            var container = ContainerOf(card);
            Grid.SetColumn(container, cell.Column);
            Grid.SetRow(container, cell.Row);
            Grid.SetColumnSpan(container, cell.ColumnSpan);
        }
    }

    private Border ContainerOf(PortalCard card) => card switch
    {
        PortalCard.PlatformLoad => PlatformLoadContainer,
        PortalCard.Storage => StorageContainer,
        PortalCard.BatchJobs => BatchJobsContainer,
        PortalCard.Sessions => SessionListContainer,
        PortalCard.Images => CanfarImagesContainer,
        _ => RecentLaunchesContainer,
    };

    // ── The launch form ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open the launch form, on <paramref name="tab"/> when one is named. Not while another dialog is up:
    /// WinUI shows one at a time, and the one open is the person's business.
    /// </summary>
    private LaunchFormShown ShowLaunchForm(string? tab)
    {
        _launchDialog ??= new LaunchFormDialog(_launchForm);
        if (tab is not null) _launchForm.ShowTab(tab);
        if (_launchDialog.IsOpen) return new LaunchFormShown(true, _launchForm.Tab);

        if (AgentPointer.OpenDialog(XamlRoot) is { } other)
            return new LaunchFormShown(false, _launchForm.Tab,
                $"another dialog is open (\"{other.Title as string ?? "a dialog"}\") — only one can be open at a time, " +
                "so the person needs to close it first");

        _launchDialog.Open(XamlRoot);
        return new LaunchFormShown(true, _launchForm.Tab);
    }

    /// <summary>
    /// For show_launch_form: the form on a tab, an image chosen in it, or the form closed. An image the
    /// catalogue offers goes on the Standard tab; any other, typed in as the Advanced tab's own image.
    /// </summary>
    public async Task<LaunchFormShown> ShowLaunchFormForAgentAsync(LaunchFormRequest request)
    {
        if (request.Close)
        {
            var wasOpen = _launchDialog is not null && await _launchDialog.CloseAsync();
            return new LaunchFormShown(false, _launchForm.Tab, wasOpen ? null : "the launch form was not open");
        }

        var tab = request.Tab;
        if (string.IsNullOrWhiteSpace(request.Image)) return ShowLaunchForm(tab);

        var image = request.Image.Trim();
        var catalogued = _sessionLaunchVm.SelectImageById(image);
        return ShowLaunchForm(tab ?? (catalogued ? LaunchFormTabs.Standard : LaunchFormTabs.Advanced)) with { Image = image };
    }

    public async Task LoadDataAsync(string? username)
    {
        var tasks = new List<Task>
        {
            SafeLoadAsync(_sessionList.LoadAsync(), "sessions"),
            SafeLoadAsync(_launchForm.LoadAsync(), "launch form"),
            SafeLoadAsync(_platformLoad.LoadAsync(), "platform stats"),
            SafeLoadAsync(_batchJobs.LoadAsync(), "batch jobs"),
            SafeLoadAsync(_canfarImages.LoadAsync(), "canfar images")
        };

        if (!string.IsNullOrEmpty(username))
            tasks.Add(SafeLoadAsync(_storageQuota.LoadAsync(username), "storage"));

        await Task.WhenAll(tasks);
    }

    private static async Task SafeLoadAsync(Task task, string name)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load {name}: {ex}");
        }
    }

    private void OnSessionOpen(object? sender, string sessionId)
    {
        var session = _sessionListVm.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is not null)
            _sessionListVm.OpenSessionInBrowser(session);
    }

    private async void OnSessionDelete(object? sender, string sessionId)
    {
        var session = _sessionListVm.Sessions.FirstOrDefault(s => s.Id == sessionId);
        var dialog = new DeleteSessionDialog { XamlRoot = XamlRoot };
        dialog.SetSessionName(session?.SessionName ?? sessionId);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await _sessionListVm.DeleteSessionCommand.ExecuteAsync(sessionId);
    }

    private async void OnSessionRenew(object? sender, string sessionId)
    {
        var session = _sessionListVm.Sessions.FirstOrDefault(s => s.Id == sessionId);
        var sessionName = session?.SessionName ?? sessionId;

        await ShowRenewDialogAsync(sessionName, sessionId);
    }

    private async void OnSessionEvents(object? sender, string sessionId)
    {
        var dialog = new SessionEventsDialog { XamlRoot = XamlRoot };
        var loadTask = dialog.LoadAsync(sessionId, _sessionListVm);
        _ = dialog.ShowAsync();
        await loadTask;
    }

    /// <summary>
    /// Close the form to show a launch's progress — one dialog at a time — and bring it back when the
    /// launch fails, with what was typed and the reason, to put right.
    /// </summary>
    private async Task LaunchFromFormAsync(Func<Task<bool>> launch)
    {
        var fromForm = _launchDialog is not null && await _launchDialog.CloseAsync();
        if (!await launch() && fromForm) ShowLaunchForm(null);
    }

    private async void OnLaunchRequested(object? sender, EventArgs e)
        => await LaunchFromFormAsync(LaunchInteractiveAsync);

    private Task<bool> LaunchInteractiveAsync()
    {
        var name = _sessionLaunchVm.SessionName;
        var imageLabel = _sessionLaunchVm.UseCustomImage
            ? _sessionLaunchVm.CustomImageUrl
            : _sessionLaunchVm.SelectedImage?.Label ?? "";
        var resourceType = _sessionLaunchVm.ResourceType;
        var cores = _sessionLaunchVm.Cores;
        var ram = _sessionLaunchVm.Ram;
        var gpus = _sessionLaunchVm.Gpus;

        return ShowLaunchDialogAsync(
            title: Helpers.Loc.T("Launch_DialogTitle"),
            name: name,
            imageLabel: imageLabel,
            resourceType: resourceType,
            cores: cores,
            ram: ram,
            gpus: gpus,
            launchFunc: async () =>
            {
                await _sessionLaunchVm.LaunchCommand.ExecuteAsync(null);
                return _sessionLaunchVm.LaunchSuccess;
            });
    }

    private async void OnHeadlessLaunchRequested(object? sender, EventArgs e)
        => await LaunchFromFormAsync(LaunchHeadlessAsync);

    private async Task<bool> LaunchHeadlessAsync()
    {
        var replicas = Math.Clamp(_sessionLaunchVm.HeadlessReplicas, 1, 20);
        var launched = await ShowLaunchDialogAsync(
            title: replicas > 1 ? Helpers.Loc.F("Launch_LaunchReplicas", replicas) : Helpers.Loc.T("Launch_DialogTitleBatch"),
            name: _sessionLaunchVm.HeadlessSessionName,
            imageLabel: _sessionLaunchVm.HeadlessSelectedImage?.Label ?? "",
            resourceType: _sessionLaunchVm.HeadlessResourceType,
            cores: _sessionLaunchVm.Cores,
            ram: _sessionLaunchVm.Ram,
            gpus: _sessionLaunchVm.Gpus,
            launchFunc: async () =>
            {
                await _sessionLaunchVm.LaunchHeadlessCommand.ExecuteAsync(null);
                return _sessionLaunchVm.LaunchSuccess;
            });
        await _batchJobs.LoadAsync(); // headless jobs surface in the Batch Jobs panel, not Active Sessions
        return launched;
    }

    /// <summary>"Use this image": the launch form, opened with it chosen.</summary>
    private async void OnUseImageRequested(object? sender, string imageId)
        => await ShowLaunchFormForAgentAsync(new LaunchFormRequest(null, imageId, false));

    private async void OnRelaunchRequested(object? sender, RecentLaunch launch)
    {
        await ShowLaunchDialogAsync(
            title: Helpers.Loc.T("Launch_DialogTitleRelaunch"),
            name: launch.Name,
            imageLabel: launch.ImageLabel,
            resourceType: launch.ResourceType,
            cores: launch.Cores,
            ram: launch.Ram,
            gpus: launch.Gpus,
            launchFunc: () => _sessionLaunchVm.RelaunchAsync(launch));
    }

    /// <summary>A launch, with its progress and outcome in a dialog. True when it launched; a failure waits for the dialog to close.</summary>
    private async Task<bool> ShowLaunchDialogAsync(
        string title, string name, string imageLabel,
        string resourceType, int cores, int ram, int gpus,
        Func<Task<bool>> launchFunc)
    {
        var statusText = new TextBlock
        {
            Text = Helpers.Loc.F("Launch_Launching", name),
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        statusRow.Children.Add(progressRing);
        statusRow.Children.Add(statusText);

        var resourceSummary = resourceType == "fixed"
            ? Helpers.Loc.F("Portal_CpuCount", cores) + "  \u00B7  " + Helpers.Loc.F("Portal_RamGb", ram)
              + (gpus > 0 ? "  \u00B7  " + Helpers.Loc.F("Portal_GpuCount", gpus) : "")
            : Helpers.Loc.T("Launch_FlexibleResources");
        var detailText = new TextBlock
        {
            Text = $"{imageLabel}  \u00B7  {resourceSummary}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6
        };

        var resultBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(statusRow);
        panel.Children.Add(detailText);
        panel.Children.Add(resultBar);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            XamlRoot = XamlRoot,
            CloseButtonText = Helpers.Loc.T("Common_Close")
        };

        var launchTask = launchFunc();
        var dialogTask = dialog.ShowAsync();

        var success = await launchTask;

        progressRing.IsActive = false;
        statusRow.Children.Remove(progressRing);

        if (success)
        {
            statusText.Text = Helpers.Loc.F("Launch_Success", name);
            resultBar.Severity = InfoBarSeverity.Success;
            resultBar.Title = Helpers.Loc.T("Launch_SessionStartingTitle");
            resultBar.Message = Helpers.Loc.T("Launch_SessionStartingBody");
            resultBar.IsOpen = true;

            _recentLaunches.Refresh();

            await Task.Delay(2000);
            dialog.Hide();

            await Task.Delay(1000);
            await _sessionList.LoadAsync();
            return true;
        }

        statusText.Text = Helpers.Loc.T("Launch_Failed");
        resultBar.Severity = InfoBarSeverity.Error;
        resultBar.Title = Helpers.Loc.T("Portal_Error");
        resultBar.Message = _sessionLaunchVm.ErrorMessage;
        resultBar.IsOpen = true;
        await dialogTask;
        return false;
    }

    private async Task ShowRenewDialogAsync(string sessionName, string sessionId)
    {
        var statusText = new TextBlock
        {
            Text = Helpers.Loc.F("Sessions_Renewing", sessionName),
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        statusRow.Children.Add(progressRing);
        statusRow.Children.Add(statusText);

        var resultBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(statusRow);
        panel.Children.Add(resultBar);

        var dialog = new ContentDialog
        {
            Title = Helpers.Loc.T("Sessions_RenewDialogTitle"),
            Content = panel,
            XamlRoot = XamlRoot,
            CloseButtonText = Helpers.Loc.T("Common_Close")
        };

        var renewTask = _sessionListVm.TryRenewSessionAsync(sessionId);
        var dialogTask = dialog.ShowAsync();

        var (success, errorMessage) = await renewTask;

        progressRing.IsActive = false;
        statusRow.Children.Remove(progressRing);

        if (success)
        {
            statusText.Text = Helpers.Loc.F("Sessions_RenewSuccess", sessionName);
            resultBar.Severity = InfoBarSeverity.Success;
            resultBar.Title = Helpers.Loc.T("Sessions_RenewedTitle");
            resultBar.Message = Helpers.Loc.T("Sessions_RenewedBody");
            resultBar.IsOpen = true;

            await Task.Delay(2000);
            dialog.Hide();
        }
        else
        {
            statusText.Text = Helpers.Loc.T("Sessions_RenewFailed");
            resultBar.Severity = InfoBarSeverity.Error;
            resultBar.Title = Helpers.Loc.T("Portal_Error");
            resultBar.Message = errorMessage ?? Helpers.Loc.T("Portal_UnknownError");
            resultBar.IsOpen = true;
        }
    }
}
