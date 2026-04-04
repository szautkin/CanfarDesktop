namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Tracks whether a notebook document has unsaved changes.
/// </summary>
public interface IDirtyTracker
{
    bool IsDirty { get; }
    event Action<bool>? DirtyChanged;
    void MarkDirty();
    void MarkClean();
    void Reset();
}
