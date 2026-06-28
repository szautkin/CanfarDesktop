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
    private readonly ObservationDownloadService _downloads;

    [ObservableProperty]
    private DownloadedObservation? _selectedObservation;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private List<DownloadedObservation> _filteredObservations = [];

    [ObservableProperty]
    private int _observationCount;

    public ResearchViewModel(ObservationStore store, DataLinkService dataLinkService, ObservationNoteStore noteStore, ObservationDownloadService downloads)
    {
        _store = store;
        _dataLinkService = dataLinkService;
        _noteStore = noteStore;
        _downloads = downloads;
        Refresh();
    }

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

        try
        {
            if (SelectedObservation.FileExists)
                File.Delete(SelectedObservation.LocalPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete file error: {ex.Message}");
        }

        _store.Remove(SelectedObservation);
        SelectedObservation = null;
        Refresh();
    }

    /// <summary>Raised when user wants to view a FITS file in the built-in viewer.</summary>
    public event Action<string>? ViewInFitsRequested;

    /// <summary>Raised when user wants to view a FITS spectral cube in the 3D Cube Viewer.</summary>
    public event Action<string>? ViewInCubeRequested;

    /// <summary>
    /// Download the FITS file for an observation that was saved without a file.
    /// The save path is provided by the caller (View handles file picker).
    /// </summary>
    public async Task DownloadObservationFileAsync(string savePath)
    {
        if (SelectedObservation is null || string.IsNullOrEmpty(SelectedObservation.PublisherID)) return;

        try
        {
            var url = await _downloads.ResolveUrlAsync(SelectedObservation.PublisherID);
            await _downloads.DownloadToPathAsync(url, savePath);

            // Update observation with the file path
            SelectedObservation.LocalPath = savePath;
            var fi = new FileInfo(savePath);
            if (fi.Exists) SelectedObservation.FileSize = fi.Length;
            _store.Save(SelectedObservation);
            OnPropertyChanged(nameof(SelectedObservation));
            Refresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Download error: {ex.Message}");
            try { if (File.Exists(savePath + ".tmp")) File.Delete(savePath + ".tmp"); } catch { }
            throw;
        }
    }

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
