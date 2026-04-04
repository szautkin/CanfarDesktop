namespace CanfarDesktop.ViewModels.Notebook;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Services.Notebook;

/// <summary>
/// ViewModel for the notebook tab host. Owns the tab collection and
/// delegates commands to the active tab's ViewModel.
/// </summary>
public partial class NotebookTabHostViewModel : ObservableObject
{
    private readonly INotebookTabFactory _tabFactory;

    [ObservableProperty] private NotebookTabItem? _activeTab;

    public ObservableCollection<NotebookTabItem> Tabs { get; } = [];

    /// <summary>The active tab's ViewModel, for toolbar command binding.</summary>
    public NotebookViewModel? ActiveViewModel => ActiveTab?.ViewModel;

    public NotebookTabHostViewModel(INotebookTabFactory tabFactory)
    {
        _tabFactory = tabFactory;
    }

    partial void OnActiveTabChanged(NotebookTabItem? value)
    {
        OnPropertyChanged(nameof(ActiveViewModel));
    }

    public NotebookTabItem AddNewTab()
    {
        var tab = _tabFactory.CreateTab();
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    public NotebookTabItem AddTabForFile(string filePath)
    {
        var tab = _tabFactory.CreateTab();
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    public void CloseTab(NotebookTabItem tab)
    {
        tab.Close();
        Tabs.Remove(tab);

        if (Tabs.Count > 0)
            ActiveTab = Tabs[^1];
        else
            ActiveTab = null;
    }

    public bool HasTabs => Tabs.Count > 0;
}
