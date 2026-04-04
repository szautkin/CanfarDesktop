using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class ResearchViewModel : ObservableObject
{
    private readonly ObservationStore _store;
    private readonly DataLinkService _dataLinkService;

    [ObservableProperty]
    private DownloadedObservation? _selectedObservation;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private List<DownloadedObservation> _filteredObservations = [];

    [ObservableProperty]
    private int _observationCount;

    public ResearchViewModel(ObservationStore store, DataLinkService dataLinkService)
    {
        _store = store;
        _dataLinkService = dataLinkService;
        Refresh();
    }

    partial void OnFilterTextChanged(string value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        FilteredObservations = _store.Filter(FilterText);
        ObservationCount = FilteredObservations.Count;
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

    [RelayCommand]
    public void OpenFile()
    {
        if (SelectedObservation is null || !SelectedObservation.FileExists) return;

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
