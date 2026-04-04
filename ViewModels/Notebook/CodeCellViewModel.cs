namespace CanfarDesktop.ViewModels.Notebook;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// ViewModel for code cells. Adds execution count, outputs, and execution state.
/// </summary>
public partial class CodeCellViewModel : CellViewModel
{
    [ObservableProperty] private int? _executionCount;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _executionStatus = string.Empty;

    public ObservableCollection<CellOutputViewModel> Outputs { get; } = [];

    public CodeCellViewModel(NotebookCell model) : base(model)
    {
        ExecutionCount = model.ExecutionCount;
        UpdateExecutionStatus();

        if (model.Outputs is not null)
        {
            foreach (var output in model.Outputs)
                Outputs.Add(new CellOutputViewModel(output));
        }
    }

    partial void OnExecutionCountChanged(int? value)
    {
        Model.ExecutionCount = value;
        UpdateExecutionStatus();
    }

    partial void OnIsExecutingChanged(bool value)
    {
        UpdateExecutionStatus();
    }

    private void UpdateExecutionStatus()
    {
        ExecutionStatus = IsExecuting ? "[*]"
            : ExecutionCount.HasValue ? $"[{ExecutionCount}]"
            : "[ ]";
    }

    public void ClearOutputs()
    {
        Outputs.Clear();
        Model.Outputs?.Clear();
        ExecutionCount = null;
    }

    public override void SyncToModel()
    {
        base.SyncToModel();
        Model.ExecutionCount = ExecutionCount;
    }
}
