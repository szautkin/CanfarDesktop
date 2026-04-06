namespace CanfarDesktop.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

/// <summary>
/// ViewModel for the FITS tab host. Owns the tab collection, tracks the active tab,
/// and manages the shared saved-coordinates list.
/// </summary>
public partial class FitsTabHostViewModel : ObservableObject
{
    private readonly IFitsTabFactory _tabFactory;
    private readonly ICoordinateStoreService _coordStore;

    [ObservableProperty] private FitsViewerTabItem? _activeTab;
    [ObservableProperty] private bool _isLinkedCrosshairEnabled = true;
    [ObservableProperty] private WorldCoordinate? _linkedCrosshairPosition;
    [ObservableProperty] private bool _isSyncZoomEnabled;

    /// <summary>
    /// Shared angular zoom: arcsec per screen pixel. When sync zoom is enabled,
    /// all tabs compute their local zoom from this value and their own pixel scale.
    /// localZoom = pixelScaleArcsec / sharedAngularZoom
    /// </summary>
    public double? SharedAngularZoom { get; set; }

    public ObservableCollection<FitsViewerTabItem> Tabs { get; } = [];
    public ObservableCollection<SavedCoordinate> SavedCoordinates { get; } = [];

    public FitsViewerViewModel? ActiveViewModel => ActiveTab?.ViewModel;

    public FitsTabHostViewModel(IFitsTabFactory tabFactory, ICoordinateStoreService coordStore)
    {
        _tabFactory = tabFactory;
        _coordStore = coordStore;
        LoadCoordinates();
    }

    partial void OnActiveTabChanged(FitsViewerTabItem? value)
    {
        OnPropertyChanged(nameof(ActiveViewModel));
    }

    public FitsViewerTabItem AddNewTab()
    {
        var tab = _tabFactory.CreateTab();
        Tabs.Add(tab);
        ActiveTab = tab;
        OnPropertyChanged(nameof(HasTabs));
        return tab;
    }

    public void CloseTab(FitsViewerTabItem tab)
    {
        tab.Close();
        Tabs.Remove(tab);

        if (Tabs.Count > 0)
            ActiveTab = Tabs[^1];
        else
            ActiveTab = null;
        OnPropertyChanged(nameof(HasTabs));
    }

    public bool HasTabs => Tabs.Count > 0;

    public void SaveCoordinate(string label, double ra, double dec, string? sourceFile)
    {
        var coord = new SavedCoordinate
        {
            Label = string.IsNullOrWhiteSpace(label)
                ? $"{WcsInfo.FormatRa(ra)} {WcsInfo.FormatDec(dec)}"
                : label,
            Ra = ra,
            Dec = dec,
            SourceFile = sourceFile,
            SavedAt = DateTime.UtcNow
        };
        _coordStore.Save(coord);
        SavedCoordinates.Insert(0, coord);
        if (SavedCoordinates.Count > 50)
            SavedCoordinates.RemoveAt(SavedCoordinates.Count - 1);
    }

    public void DeleteCoordinate(SavedCoordinate coord)
    {
        _coordStore.Delete(coord);
        SavedCoordinates.Remove(coord);
    }

    private void LoadCoordinates()
    {
        SavedCoordinates.Clear();
        foreach (var c in _coordStore.Load())
            SavedCoordinates.Add(c);
    }
}
