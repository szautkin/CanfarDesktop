namespace CanfarDesktop.ViewModels.Notebook;

using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// Base ViewModel for all notebook cell types. Wraps a NotebookCell model.
/// </summary>
public abstract partial class CellViewModel : ObservableObject
{
    private readonly NotebookCell _model;

    public NotebookCell Model => _model;
    public string CellId => _model.Id ?? string.Empty;
    public string CellType => _model.CellType;

    [ObservableProperty] private string _source;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isFocused;

    public event Action? ContentChanged;

    protected CellViewModel(NotebookCell model)
    {
        _model = model;
        _source = model.SourceText;
    }

    partial void OnSourceChanged(string value)
    {
        _model.SourceText = value;
        ContentChanged?.Invoke();
    }

    public virtual void SyncToModel()
    {
        _model.SourceText = Source;
    }
}
