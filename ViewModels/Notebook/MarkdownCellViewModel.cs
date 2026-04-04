namespace CanfarDesktop.ViewModels.Notebook;

using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// ViewModel for markdown cells. Adds rendered/edit mode toggle.
/// </summary>
public partial class MarkdownCellViewModel : CellViewModel
{
    [ObservableProperty] private bool _isRendered = true;
    [ObservableProperty] private string _renderedHtml = string.Empty;

    public MarkdownCellViewModel(NotebookCell model) : base(model)
    {
    }

    public void ToggleEditMode()
    {
        IsRendered = !IsRendered;
        IsEditing = !IsRendered;
    }

    public void EnterEditMode()
    {
        IsRendered = false;
        IsEditing = true;
    }

    public void ExitEditMode()
    {
        IsRendered = true;
        IsEditing = false;
        RenderedHtml = Source; // placeholder until M5 markdown renderer
    }
}
