namespace CanfarDesktop.ViewModels.Notebook;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Wraps a NotebookViewModel for use as a tab item. Exposes a computed
/// Header string that updates when the title or dirty state changes.
/// </summary>
public partial class NotebookTabItem : ObservableObject
{
    public NotebookViewModel ViewModel { get; }

    [ObservableProperty] private string _header;

    public NotebookTabItem(NotebookViewModel viewModel)
    {
        ViewModel = viewModel;
        _header = viewModel.Title;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NotebookViewModel.Title) or nameof(NotebookViewModel.IsDirty))
                Header = viewModel.IsDirty ? $"{viewModel.Title} *" : viewModel.Title;
        };
    }

    public void Close() => ViewModel.Close();
}
