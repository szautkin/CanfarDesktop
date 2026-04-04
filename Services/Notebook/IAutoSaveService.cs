namespace CanfarDesktop.Services.Notebook;

using CanfarDesktop.Models.Notebook;

/// <summary>
/// Timer-based autosave that writes recovery checkpoints to a temporary location.
/// The autosave file is NOT the user's original file -- it is a crash-recovery artifact.
/// </summary>
public interface IAutoSaveService : IDisposable
{
    void Start(string? originalPath, Func<NotebookDocument> documentProvider);
    void StopAndCleanup();
    Task<string?> SaveNowAsync();
    string? AutoSavePath { get; }
    TimeSpan Interval { get; set; }
}
