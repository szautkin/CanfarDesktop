using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.ViewModels.ImageDiscovery;

/// <summary>
/// The inline detail panel for one image — either the manifest breakdown (OS + capabilities +
/// ecosystem sections, with Copy-as-JSON) or the failure detail (category + message + lazily-loaded
/// probe events/logs). Shown in-place inside the discovery dialog (no nested dialogs). WinUI-free;
/// clipboard copy stays in the code-behind via <see cref="Json"/>.
/// </summary>
public partial class ManifestDetailViewModel : ObservableObject
{
    public string ImageId { get; }
    public string Label { get; }
    public bool IsFailure { get; }
    public bool HasManifest => !IsFailure;

    // Manifest detail
    public string OsLine { get; } = string.Empty;
    public string KernelLine { get; } = string.Empty;
    public bool HasKernel => KernelLine.Length > 0;
    public IReadOnlyList<string> Capabilities { get; } = Array.Empty<string>();
    public bool HasCapabilities => Capabilities.Count > 0;
    public IReadOnlyList<EcosystemSection> Sections { get; } = Array.Empty<EcosystemSection>();
    public string? ProbeNotes { get; }
    public bool HasProbeNotes => !string.IsNullOrEmpty(ProbeNotes);
    public string Json { get; } = string.Empty;

    // Failure detail
    public string FailureTitle { get; } = string.Empty;
    public string FailureMessage { get; } = string.Empty;
    public bool HasJob { get; }
    public string JobLine { get; } = string.Empty;

    // Probe logs (lazy)
    [ObservableProperty] private string _logsText = string.Empty;
    [ObservableProperty] private bool _isLoadingLogs;
    [ObservableProperty] private bool _logsLoaded;

    private readonly string? _jobId;
    private readonly ImageDiscoveryCoordinator _coordinator;
    private readonly Action _close;

    public ManifestDetailViewModel(ImageRowViewModel row, ImageDiscoveryCoordinator coordinator, Action close)
    {
        _coordinator = coordinator;
        _close = close;
        ImageId = row.Id;
        Label = row.Label;

        var state = row.State;
        if (state.Kind == RowStateKind.Discovered && state.Manifest is { } m)
        {
            var d = ManifestDetailBuilder.Build(m);
            OsLine = d.OsLine;
            KernelLine = d.KernelLine;
            Capabilities = d.Capabilities;
            Sections = d.Sections;
            ProbeNotes = d.ProbeNotes;
            Json = ManifestDetailBuilder.ToJson(m);
        }
        else
        {
            IsFailure = true;
            FailureTitle = DiscoveryFormatting.CategoryLabel(state.Category ?? FailureCategory.Unknown);
            FailureMessage = state.Message ?? "(no message)";
            _jobId = state.JobID;
            HasJob = !string.IsNullOrEmpty(_jobId);
            JobLine = HasJob ? $"Job {_jobId}" : string.Empty;
        }
    }

    [RelayCommand]
    private void Close() => _close();

    [RelayCommand]
    private async Task LoadLogs()
    {
        if (_jobId is null || IsLoadingLogs) return;
        IsLoadingLogs = true;
        try
        {
            var events = await _coordinator.FetchEventsAsync(_jobId);
            var logs = await _coordinator.FetchLogsAsync(_jobId);
            LogsText = $"=== EVENTS ===\n{events}\n\n=== LOGS ===\n{logs}";
            LogsLoaded = true;
        }
        catch (Exception ex)
        {
            LogsText = $"Failed to load logs: {ex.Message}";
            LogsLoaded = true;
        }
        finally
        {
            IsLoadingLogs = false;
        }
    }
}
