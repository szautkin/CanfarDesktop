namespace CanfarDesktop.Services.Notebook;

using CanfarDesktop.ViewModels.Notebook;

/// <summary>
/// Factory for creating independent notebook tab instances.
/// Each tab gets its own ViewModel, kernel, dirty tracker, and autosave.
/// </summary>
public interface INotebookTabFactory
{
    NotebookTabItem CreateTab();
}
