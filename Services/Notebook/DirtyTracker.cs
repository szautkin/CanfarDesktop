namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Simple boolean dirty tracker. One instance per open notebook (Transient DI).
/// </summary>
public class DirtyTracker : IDirtyTracker
{
    private bool _isDirty;

    public bool IsDirty => _isDirty;

    public event Action<bool>? DirtyChanged;

    public void MarkDirty()
    {
        if (_isDirty) return;
        _isDirty = true;
        DirtyChanged?.Invoke(true);
    }

    public void MarkClean()
    {
        if (!_isDirty) return;
        _isDirty = false;
        DirtyChanged?.Invoke(false);
    }

    public void Reset()
    {
        _isDirty = false;
        DirtyChanged?.Invoke(false);
    }
}
