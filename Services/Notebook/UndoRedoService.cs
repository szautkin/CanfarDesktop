namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Simple undo/redo stack for notebook structural operations.
/// Stores snapshots of the cell list state. Each undo point is a full cell snapshot.
/// Max depth 50 to bound memory usage.
/// </summary>
public class UndoRedoService
{
    private const int MaxDepth = 50;
    private readonly Stack<UndoState> _undoStack = new();
    private readonly Stack<UndoState> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event Action? StateChanged;

    /// <summary>
    /// Push the current state before a structural change.
    /// Call this BEFORE add/delete/move/merge/split/type-change.
    /// </summary>
    public void PushUndo(UndoState state)
    {
        _undoStack.Push(state);
        _redoStack.Clear(); // new action invalidates redo
        if (_undoStack.Count > MaxDepth)
        {
            // Trim oldest — convert to array, drop last, rebuild
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (var i = items.Length - 2; i >= 0; i--)
                _undoStack.Push(items[i]);
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Pop the last undo state. Returns null if nothing to undo.
    /// The caller should push the CURRENT state onto redo before restoring.
    /// </summary>
    public UndoState? PopUndo(UndoState currentState)
    {
        if (_undoStack.Count == 0) return null;
        _redoStack.Push(currentState);
        var state = _undoStack.Pop();
        StateChanged?.Invoke();
        return state;
    }

    /// <summary>
    /// Pop the last redo state. Returns null if nothing to redo.
    /// </summary>
    public UndoState? PopRedo(UndoState currentState)
    {
        if (_redoStack.Count == 0) return null;
        _undoStack.Push(currentState);
        var state = _redoStack.Pop();
        StateChanged?.Invoke();
        return state;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }
}

/// <summary>
/// Snapshot of the cell list for undo/redo. Stores serialized cell data.
/// </summary>
public class UndoState
{
    public required List<CellSnapshot> Cells { get; init; }
    public int SelectedIndex { get; init; }
}

public class CellSnapshot
{
    public required string CellType { get; init; }
    public required string Source { get; init; }
    public string? Id { get; init; }
    public int? ExecutionCount { get; init; }
}
