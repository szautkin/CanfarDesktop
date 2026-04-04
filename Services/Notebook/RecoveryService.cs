namespace CanfarDesktop.Services.Notebook;

using System.Diagnostics;

/// <summary>
/// Scans the autosave directory for .autosave.ipynb files that were not
/// cleaned up (previous crash). Singleton in DI.
/// </summary>
public class RecoveryService : IRecoveryService
{
    public List<RecoveryCandidate> DetectOrphanedFiles()
    {
        var dir = AutoSaveService.GetAutoSaveDirectory();
        if (!Directory.Exists(dir)) return [];

        var candidates = new List<RecoveryCandidate>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.autosave.ipynb"))
            {
                try
                {
                    var info = new FileInfo(file);
                    var baseName = Path.GetFileNameWithoutExtension(file)
                        .Replace(".autosave", "", StringComparison.OrdinalIgnoreCase);

                    candidates.Add(new RecoveryCandidate
                    {
                        AutoSavePath = file,
                        OriginalPath = baseName.StartsWith("untitled-") ? null : baseName + ".ipynb",
                        DisplayName = baseName,
                        LastModifiedUtc = info.LastWriteTimeUtc,
                        SizeBytes = info.Length
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Recovery scan skipped file: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Recovery scan failed: {ex.Message}");
        }

        return candidates;
    }

    public void Discard(RecoveryCandidate candidate)
    {
        try
        {
            if (File.Exists(candidate.AutoSavePath))
                File.Delete(candidate.AutoSavePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Recovery discard failed: {ex.Message}");
        }
    }

    public void DiscardAll()
    {
        var dir = AutoSaveService.GetAutoSaveDirectory();
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.autosave.ipynb"))
        {
            try { File.Delete(file); }
            catch (Exception ex) { Debug.WriteLine($"Recovery discard failed for {file}: {ex.Message}"); }
        }
    }
}
