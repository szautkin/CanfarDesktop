namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Detects orphaned autosave files on app startup.
/// </summary>
public interface IRecoveryService
{
    List<RecoveryCandidate> DetectOrphanedFiles();
    void Discard(RecoveryCandidate candidate);
    void DiscardAll();
}

/// <summary>
/// An orphaned autosave file that the user may want to recover.
/// </summary>
public class RecoveryCandidate
{
    public required string AutoSavePath { get; init; }
    public string? OriginalPath { get; init; }
    public required string DisplayName { get; init; }
    public DateTime LastModifiedUtc { get; init; }
    public long SizeBytes { get; init; }
}
