namespace CanfarDesktop.ViewModels.Notebook;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Models.Notebook;
using CanfarDesktop.Services.Notebook;

/// <summary>
/// Top-level ViewModel for a single open notebook. Owns the cell list,
/// dirty tracking, autosave lifecycle, and cell manipulation commands.
/// </summary>
public partial class NotebookViewModel : ObservableObject
{
    private readonly IDirtyTracker _dirtyTracker;
    private readonly IAutoSaveService _autoSaveService;
    private readonly IKernelService _kernelService;
    private NotebookDocument _document;
    private string? _filePath;

    [ObservableProperty] private string _title = "Untitled";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _kernelDisplayName = string.Empty;
    [ObservableProperty] private string _kernelStatusText = "Dead";
    [ObservableProperty] private KernelState _kernelState = KernelState.Dead;
    [ObservableProperty] private CellViewModel? _selectedCell;
    [ObservableProperty] private int _selectedCellIndex = -1;

    public ObservableCollection<CellViewModel> Cells { get; } = [];
    public NotebookDocument Document => _document;
    public string? FilePath => _filePath;

    public NotebookViewModel(IDirtyTracker dirtyTracker, IAutoSaveService autoSaveService, IKernelService kernelService)
    {
        _dirtyTracker = dirtyTracker;
        _autoSaveService = autoSaveService;
        _kernelService = kernelService;
        _document = NotebookParser.CreateEmpty();

        _dirtyTracker.DirtyChanged += OnDirtyChanged;
        _kernelService.StateChanged += HandleKernelStateChanged;

        HydrateFromDocument();
        StartAutoSave();
    }

    #region Document loading

    public void LoadFromFile(string filePath, NotebookDocument document)
    {
        _filePath = filePath;
        _document = document;
        Title = Path.GetFileName(filePath);
        KernelDisplayName = document.Metadata.KernelSpec?.DisplayName ?? "Unknown kernel";

        _dirtyTracker.Reset();
        HydrateFromDocument();

        _autoSaveService.StopAndCleanup();
        StartAutoSave();
    }

    public void LoadNew()
    {
        _filePath = null;
        _document = NotebookParser.CreateEmpty();
        Title = "Untitled";
        KernelDisplayName = _document.Metadata.KernelSpec?.DisplayName ?? "Python 3";

        _dirtyTracker.Reset();
        HydrateFromDocument();
        StartAutoSave();
    }

    private void HydrateFromDocument()
    {
        foreach (var cell in Cells)
            cell.ContentChanged -= OnCellContentChanged;

        Cells.Clear();
        foreach (var modelCell in _document.Cells)
        {
            var vm = CreateCellViewModel(modelCell);
            vm.ContentChanged += OnCellContentChanged;
            Cells.Add(vm);
        }

        if (Cells.Count > 0)
            SelectCell(0);
    }

    private static CellViewModel CreateCellViewModel(NotebookCell model) => model.CellType switch
    {
        "markdown" => new MarkdownCellViewModel(model),
        _ => new CodeCellViewModel(model)
    };

    #endregion

    #region Dirty tracking

    private void OnDirtyChanged(bool dirty) => IsDirty = dirty;
    private void OnCellContentChanged()
    {
        _dirtyTracker.MarkDirty();
        UpdateCellSnapshot();
    }

    #endregion

    #region Save

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (_filePath is null)
        {
            StatusMessage = "Use File > Save As for new notebooks";
            return;
        }

        SyncAllCellsToModel();
        try
        {
            var tmpPath = _filePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await NotebookParser.SerializeAsync(_document, stream);
            }
            File.Move(tmpPath, _filePath, overwrite: true);
            _dirtyTracker.MarkClean();
            StatusMessage = $"Saved {Path.GetFileName(_filePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveAsAsync(string filePath)
    {
        _filePath = filePath;
        Title = Path.GetFileName(filePath);

        SyncAllCellsToModel();
        try
        {
            var tmpPath = filePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await NotebookParser.SerializeAsync(_document, stream);
            }
            File.Move(tmpPath, filePath, overwrite: true);
            _dirtyTracker.MarkClean();
            StatusMessage = $"Saved {Path.GetFileName(filePath)}";

            _autoSaveService.StopAndCleanup();
            StartAutoSave();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private void SyncAllCellsToModel()
    {
        _document.Cells.Clear();
        foreach (var cellVm in Cells)
        {
            cellVm.SyncToModel();
            _document.Cells.Add(cellVm.Model);
        }
    }

    #endregion

    #region AutoSave

    // Thread-safe snapshot of cells for autosave. Updated on every cell mutation.
    private volatile List<(string CellType, string? Id, CellMetadata Metadata,
        Dictionary<string, System.Text.Json.JsonElement>? ExtensionData,
        int? ExecutionCount, List<CellOutput>? Outputs, string Source)> _cellSnapshot = [];

    private void UpdateCellSnapshot()
    {
        _cellSnapshot = Cells.Select(c => (
            c.Model.CellType, c.Model.Id, c.Model.Metadata,
            c.Model.ExtensionData, c.Model.ExecutionCount, c.Model.Outputs, c.Source
        )).ToList();
    }

    private void StartAutoSave()
    {
        UpdateCellSnapshot();
        _autoSaveService.Start(_filePath, () =>
        {
            // Read the pre-built snapshot — no ObservableCollection access from timer thread
            var cells = _cellSnapshot;
            var snapshot = new NotebookDocument
            {
                NbFormat = _document.NbFormat,
                NbFormatMinor = _document.NbFormatMinor,
                Metadata = _document.Metadata,
                ExtensionData = _document.ExtensionData,
                Cells = cells.Select(c =>
                {
                    var copy = new NotebookCell
                    {
                        CellType = c.CellType,
                        Id = c.Id,
                        Metadata = c.Metadata,
                        ExtensionData = c.ExtensionData,
                        ExecutionCount = c.ExecutionCount,
                        Outputs = c.Outputs,
                    };
                    copy.SourceText = c.Source;
                    return copy;
                }).ToList()
            };
            return snapshot;
        });
    }

    #endregion

    #region Cell selection

    public void SelectCell(int index)
    {
        if (index < 0 || index >= Cells.Count) return;

        if (SelectedCell is not null)
            SelectedCell.IsSelected = false;

        SelectedCellIndex = index;
        SelectedCell = Cells[index];
        SelectedCell.IsSelected = true;
    }

    #endregion

    #region Cell operations

    [RelayCommand]
    public void AddCellBelow(string? cellType)
    {
        var type = cellType ?? "code";
        var insertIndex = SelectedCellIndex >= 0 ? SelectedCellIndex + 1 : Cells.Count;
        InsertCell(insertIndex, type);
    }

    [RelayCommand]
    public void AddCellAbove(string? cellType)
    {
        var type = cellType ?? "code";
        var insertIndex = SelectedCellIndex >= 0 ? SelectedCellIndex : 0;
        InsertCell(insertIndex, type);
    }

    private void InsertCell(int index, string cellType)
    {
        var model = new NotebookCell
        {
            CellType = cellType,
            Id = NotebookParser.GenerateCellId(),
            Source = [],
            Outputs = cellType == "code" ? [] : null,
            ExecutionCount = null
        };

        var vm = CreateCellViewModel(model);
        vm.ContentChanged += OnCellContentChanged;
        Cells.Insert(index, vm);
        _dirtyTracker.MarkDirty();
        SelectCell(index);
    }

    [RelayCommand]
    public void DeleteSelectedCell()
    {
        if (SelectedCellIndex < 0 || Cells.Count <= 1) return;

        var index = SelectedCellIndex;
        var cell = Cells[index];
        cell.ContentChanged -= OnCellContentChanged;
        Cells.RemoveAt(index);
        _dirtyTracker.MarkDirty();
        SelectCell(Math.Min(index, Cells.Count - 1));
    }

    [RelayCommand]
    public void MoveCellUp()
    {
        if (SelectedCellIndex <= 0) return;
        var index = SelectedCellIndex;
        Cells.Move(index, index - 1);
        _dirtyTracker.MarkDirty();
        SelectCell(index - 1);
    }

    [RelayCommand]
    public void MoveCellDown()
    {
        if (SelectedCellIndex < 0 || SelectedCellIndex >= Cells.Count - 1) return;
        var index = SelectedCellIndex;
        Cells.Move(index, index + 1);
        _dirtyTracker.MarkDirty();
        SelectCell(index + 1);
    }

    [RelayCommand]
    public void SplitCell(int cursorPosition)
    {
        if (SelectedCell is null || SelectedCellIndex < 0) return;
        var source = SelectedCell.Source;
        if (cursorPosition < 0 || cursorPosition > source.Length) return;

        var topSource = source[..cursorPosition];
        var bottomSource = source[cursorPosition..];

        SelectedCell.Source = topSource;

        var model = new NotebookCell
        {
            CellType = SelectedCell.CellType,
            Id = NotebookParser.GenerateCellId(),
            Source = NotebookCell.SplitSourceLines(bottomSource),
            Outputs = SelectedCell.CellType == "code" ? [] : null,
            ExecutionCount = null
        };

        var vm = CreateCellViewModel(model);
        vm.ContentChanged += OnCellContentChanged;
        Cells.Insert(SelectedCellIndex + 1, vm);
        _dirtyTracker.MarkDirty();
        SelectCell(SelectedCellIndex + 1);
    }

    [RelayCommand]
    public void MergeCellBelow()
    {
        if (SelectedCellIndex < 0 || SelectedCellIndex >= Cells.Count - 1) return;

        var current = Cells[SelectedCellIndex];
        var below = Cells[SelectedCellIndex + 1];

        current.Source = current.Source + "\n" + below.Source;
        below.ContentChanged -= OnCellContentChanged;
        Cells.RemoveAt(SelectedCellIndex + 1);
        _dirtyTracker.MarkDirty();
    }

    [RelayCommand]
    public void ChangeCellType(string? newType)
    {
        if (SelectedCell is null || SelectedCellIndex < 0 || newType is null) return;
        if (SelectedCell.CellType == newType) return;

        var oldModel = SelectedCell.Model;
        var newModel = new NotebookCell
        {
            CellType = newType,
            Id = oldModel.Id,
            Source = new List<string>(oldModel.Source),
            Metadata = oldModel.Metadata,
            Outputs = newType == "code" ? [] : null,
            ExecutionCount = null
        };

        var index = SelectedCellIndex;
        var oldVm = Cells[index];
        oldVm.ContentChanged -= OnCellContentChanged;

        var newVm = CreateCellViewModel(newModel);
        newVm.ContentChanged += OnCellContentChanged;
        Cells[index] = newVm;

        _dirtyTracker.MarkDirty();
        SelectCell(index);
    }

    [RelayCommand]
    public void ClearAllOutputs()
    {
        foreach (var cell in Cells)
        {
            if (cell is CodeCellViewModel code)
                code.ClearOutputs();
        }
        _dirtyTracker.MarkDirty();
    }

    #endregion

    #region Execution

    private void HandleKernelStateChanged(KernelState state)
    {
        KernelState = state;
        KernelStatusText = state.ToString();
    }

    [RelayCommand]
    public async Task StartKernelAsync()
    {
        try
        {
            var workDir = _filePath is not null ? Path.GetDirectoryName(_filePath) : null;
            await _kernelService.StartAsync(workDir);
            StatusMessage = "Kernel started";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kernel error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RunSelectedCellAsync()
    {
        if (SelectedCell is not CodeCellViewModel codeCell) return;

        if (KernelState == KernelState.Dead)
            await StartKernelAsync();

        if (KernelState != KernelState.Idle && KernelState != KernelState.Error)
        {
            // Kernel failed to start — show the error in the cell output
            ShowCellError(codeCell, "Kernel not available", StatusMessage);
            return;
        }

        codeCell.IsExecuting = true;
        codeCell.ClearOutputs();

        try
        {
            var result = await _kernelService.ExecuteAsync(codeCell.Source);
            codeCell.ExecutionCount = result.ExecutionCount;

            foreach (var output in result.Outputs)
            {
                var cellOutput = KernelOutputToCellOutput(output, result.ExecutionCount);
                codeCell.Outputs.Add(new CellOutputViewModel(cellOutput));
                codeCell.Model.Outputs ??= [];
                codeCell.Model.Outputs.Add(cellOutput);
            }

            _dirtyTracker.MarkDirty();
        }
        catch (Exception ex)
        {
            ShowCellError(codeCell, "ExecutionError", ex.Message);
        }
        finally
        {
            codeCell.IsExecuting = false;
        }
    }

    private static void ShowCellError(CodeCellViewModel cell, string errorName, string errorMessage)
    {
        cell.ClearOutputs();
        var errorOutput = new CellOutput
        {
            OutputType = "error",
            Ename = errorName,
            Evalue = errorMessage,
            Traceback = [errorMessage]
        };
        cell.Outputs.Add(new CellOutputViewModel(errorOutput));
        cell.Model.Outputs ??= [];
        cell.Model.Outputs.Add(errorOutput);
    }

    [RelayCommand]
    public async Task RunSelectedAndAdvanceAsync()
    {
        await RunSelectedCellAsync();

        if (SelectedCellIndex < Cells.Count - 1)
            SelectCell(SelectedCellIndex + 1);
        else
        {
            AddCellBelowCommand.Execute("code");
        }
    }

    [RelayCommand]
    public async Task RunAllCellsAsync()
    {
        if (KernelState == KernelState.Dead)
            await StartKernelAsync();

        for (var i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] is not CodeCellViewModel) continue;
            SelectCell(i);
            await RunSelectedCellAsync();
            if (KernelState == KernelState.Dead) break; // kernel died
        }
    }

    [RelayCommand]
    public async Task InterruptKernelAsync()
    {
        await _kernelService.InterruptAsync();
        StatusMessage = "Kernel interrupted";
    }

    [RelayCommand]
    public async Task RestartKernelAsync()
    {
        var workDir = _filePath is not null ? Path.GetDirectoryName(_filePath) : null;
        await _kernelService.RestartAsync(workDir);
        StatusMessage = "Kernel restarted";
    }

    private static CellOutput KernelOutputToCellOutput(KernelOutput ko, int execCount)
    {
        var co = new CellOutput { OutputType = ko.OutputType };

        switch (ko.OutputType)
        {
            case "stream":
                co.Name = ko.StreamName;
                co.Text = ko.Text is not null ? [ko.Text] : null;
                break;
            case "execute_result":
            case "display_data":
                if (ko.Data is not null)
                {
                    co.Data = new Dictionary<string, System.Text.Json.JsonElement>();
                    foreach (var kvp in ko.Data)
                    {
                        var jsonStr = System.Text.Json.JsonSerializer.Serialize(kvp.Value);
                        co.Data[kvp.Key] = System.Text.Json.JsonDocument.Parse(jsonStr).RootElement.Clone();
                    }
                }
                if (ko.OutputType == "execute_result")
                    co.ExecutionCount = execCount;
                break;
            case "error":
                co.Ename = ko.Ename;
                co.Evalue = ko.Evalue;
                co.Traceback = ko.Traceback;
                break;
        }

        return co;
    }

    #endregion

    #region Cleanup

    public void Close()
    {
        _autoSaveService.StopAndCleanup();
        _kernelService.StateChanged -= HandleKernelStateChanged;
        _dirtyTracker.DirtyChanged -= OnDirtyChanged;
        foreach (var cell in Cells)
            cell.ContentChanged -= OnCellContentChanged;
    }

    #endregion

}
