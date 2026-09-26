using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Database;

namespace CanfarDesktop.ViewModels;

public partial class ResearchViewModel : ObservableObject
{
    private readonly ObservationStore _store;
    private readonly DataLinkService _dataLinkService;
    private readonly ObservationNoteStore _noteStore;
    private readonly ObservationDownloader _downloader;

    [ObservableProperty]
    private DownloadedObservation? _selectedObservation;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private List<DownloadedObservation> _filteredObservations = [];

    [ObservableProperty]
    private int _observationCount;

    public ResearchViewModel(ObservationStore store, DataLinkService dataLinkService, ObservationNoteStore noteStore, ObservationDownloader downloader)
    {
        _store = store;
        _dataLinkService = dataLinkService;
        _noteStore = noteStore;
        _downloader = downloader;

        // Held for the app's life, as the one Research page that owns this view model is.
        _store.Changed += RaiseObservationsChanged;
        _downloader.Changed += RaiseObservationsChanged;
        Refresh();
    }

    /// <summary>
    /// Something about the observations changed — a download started, landed or failed, or a record was
    /// saved from elsewhere — on whatever thread it happened. The page marshals and refreshes.
    /// </summary>
    public event Action? ObservationsChanged;

    private void RaiseObservationsChanged() => ObservationsChanged?.Invoke();

    partial void OnFilterTextChanged(string value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        // Metadata match, then union with observations whose notes/tags match the FTS index.
        var result = _store.Filter(FilterText);
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var noteIds = _noteStore.SearchPublisherIds(FilterText).ToHashSet();
            if (noteIds.Count > 0)
            {
                var already = result.Select(o => o.PublisherID).ToHashSet();
                var byNote = _store.Observations
                    .Where(o => noteIds.Contains(o.PublisherID) && !already.Contains(o.PublisherID));
                result = result.Concat(byNote).ToList();
            }
        }

        FilteredObservations = result;
        ObservationCount = result.Count;
    }

    [RelayCommand]
    public void DeleteObservation()
    {
        if (SelectedObservation is null) return;

        if (ResearchRecords.DeleteLocalFiles(SelectedObservation) is { } why)
            System.Diagnostics.Debug.WriteLine($"Delete file error: {why}");

        _store.Remove(SelectedObservation);
        SelectedObservation = null;
        Refresh();
    }

    /// <summary>Raised when user wants to view a FITS file in the built-in viewer.</summary>
    public event Action<string>? ViewInFitsRequested;

    /// <summary>Raised when user wants to view a FITS spectral cube in the 3D Cube Viewer.</summary>
    public event Action<string>? ViewInCubeRequested;

    /// <summary>
    /// Start downloading the file for a record saved without one; the caller chose where it goes.
    ///
    /// <para>For THIS record, named here, and handed to the app. It used to await the bytes and then
    /// write the path onto whatever was selected by then: closing the detail mid-download threw and the
    /// file was never recorded, and picking another observation recorded it on the wrong one. Progress
    /// and failure are in the status bar; <see cref="ObservationsChanged"/> says when it lands.</para>
    /// </summary>
    public void StartDownload(DownloadedObservation observation, string savePath)
    {
        if (string.IsNullOrEmpty(observation.PublisherID)) return;
        _ = _downloader.Start(new ObservationDownloadRequest(observation.PublisherID, savePath, observation));
    }

    /// <summary>
    /// Delete the observation's file from this computer and keep it in Research — its details, notes,
    /// and for a cutout its region, so Download fetches it again as it was. Null when done, otherwise why not.
    /// </summary>
    public string? RemoveLocalFile(DownloadedObservation observation)
        => ResearchRecords.RemoveLocalFile(_store, observation);

    /// <summary>
    /// The ways this observation's files can be cut: CADC's, from its DataLink answer (none when it
    /// cannot be had), and the observation's file on this computer — for a cutout, the file it was cut
    /// from. Reads that file's headers, off the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<Services.Cutouts.ICutoutSource>> CutoutSourcesAsync(DownloadedObservation observation)
    {
        if (string.IsNullOrEmpty(observation.PublisherID)) return [];

        IReadOnlyList<Services.Cutouts.ICutoutSource> soda;
        try { soda = Services.Cutouts.CutoutSources.Soda(await _dataLinkService.GetLinksAsync(observation.PublisherID), null); }
        catch { soda = []; }

        string?[] named = [observation.Cutout?.ArtifactId, observation.ArtifactId];
        var local = await Task.Run(() => Services.Cutouts.CutoutSources.Local(
            _store.Observations, observation.PublisherID, named.OfType<string>().Where(a => a.Length > 0)));
        return [.. soda, .. local is null ? [] : new Services.Cutouts.ICutoutSource[] { local }];
    }

    /// <summary>Whether this observation's file is on its way right now.</summary>
    /// <summary>The complete observation a cutout was cut from, when Research has it — with its file or without.</summary>
    public DownloadedObservation? OriginalOf(DownloadedObservation cutout)
        => ResearchRecords.Complete(_store.Observations, cutout.PublisherID);

    public bool IsDownloading(DownloadedObservation observation)
        => _downloader.IsDownloading(observation.PublisherID, observation.ProductKey);

    [RelayCommand]
    public void OpenFile()
    {
        if (SelectedObservation is null || !SelectedObservation.FileExists) return;

        var ext = Path.GetExtension(SelectedObservation.LocalPath).ToLowerInvariant();
        if (ext is ".fits" or ".fit" or ".fts")
        {
            ViewInFitsRequested?.Invoke(SelectedObservation.LocalPath);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedObservation.LocalPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open file error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenInFitsViewer()
    {
        if (SelectedObservation is null || !SelectedObservation.FileExists) return;
        ViewInFitsRequested?.Invoke(SelectedObservation.LocalPath);
    }

    [RelayCommand]
    public void OpenInCubeViewer()
    {
        if (SelectedObservation is null || !SelectedObservation.FileExists) return;
        ViewInCubeRequested?.Invoke(SelectedObservation.LocalPath);
    }

    [RelayCommand]
    public void ShowInExplorer()
    {
        if (SelectedObservation is null) return;

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedObservation.LocalPath}\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Show in Explorer error: {ex.Message}");
        }
    }

    public DataLinkService DataLink => _dataLinkService;
}
