using System.Text;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Write a file so a reader never sees it half-written: build a sibling <c>.tmp</c>, then put it over
/// the target in one operation.
///
/// <para>This existed six times — the notebook's Save and Save As, the autosave, the MCP sidecar, the
/// Claude config repair, the manifest store and <c>DiskPersistence</c> — as the same three lines
/// each. They agreed on the
/// important part and differed in one: only the download path deleted its temp file when the write
/// failed, so a failed notebook save left <c>analysis.ipynb.tmp</c> sitting beside the notebook
/// forever. Collapsing them is how the one copy that knew that teaches the other four.</para>
///
/// <para>The temp path is derived from the TARGET rather than being a fixed name, so two writers to
/// different files can never rename each other's half-written file into place.</para>
/// </summary>
public static class AtomicFile
{
    /// <summary>Write text, atomically. The temp file is removed if anything throws.</summary>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
        => Write(path, tmp =>
        {
            if (encoding is null) File.WriteAllText(tmp, contents);
            else File.WriteAllText(tmp, contents, encoding);
        });

    /// <summary>Write text, atomically, without blocking.</summary>
    public static Task WriteAllTextAsync(string path, string contents, CancellationToken ct = default)
        => WriteAsync(path, async tmp => await File.WriteAllTextAsync(tmp, contents, ct));

    /// <summary>
    /// Write through a stream, atomically — for a serializer that writes rather than returns.
    /// <paramref name="write"/> is handed an open, writable stream for the temp file.
    /// </summary>
    public static Task WriteStreamAsync(string path, Func<Stream, Task> write, CancellationToken ct = default)
        => WriteAsync(path, async tmp =>
        {
            await using var stream = File.Create(tmp);
            await write(stream);
        });

    /// <summary>
    /// Write several files as one, on the calling thread — a cutout and the companions cut with it,
    /// streamed row by row by a caller already off the UI. Each is built beside its target, and none is
    /// put in place until every one is built; if putting one in place fails, the ones already put are
    /// put back as they were. Either all the targets are new, or all are as they were.
    /// </summary>
    public static void WriteStreams(IReadOnlyList<(string Path, Action<Stream> Write)> files)
    {
        var sameName = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (files.Select(f => Path.GetFullPath(f.Path)).Distinct(sameName).Count() != files.Count)
            throw new ArgumentException("Each file is written once.", nameof(files));

        var built = new List<string>();
        var placed = new List<(string Path, string? Backup)>();
        try
        {
            foreach (var (path, write) in files)
            {
                var tmp = TempFor(path);
                built.Add(tmp);
                using var stream = File.Create(tmp);
                write(stream);
            }

            for (var i = 0; i < files.Count; i++)
            {
                var path = files[i].Path;
                var backup = File.Exists(path) ? BackupFor(path) : null;
                Commit(built[i], path, backup);
                placed.Add((path, backup));
            }
        }
        catch
        {
            // Back as they were: a replaced target from its backup, a new one gone.
            foreach (var (path, backup) in Enumerable.Reverse(placed))
            {
                try
                {
                    if (backup is not null) File.Move(backup, path, overwrite: true);
                    else File.Delete(path);
                }
                catch { /* best effort: the original exception is the news */ }
            }
            foreach (var tmp in built) Cleanup(tmp);
            throw;
        }

        foreach (var (_, backup) in placed)
            if (backup is not null) Cleanup(backup);
    }

    /// <summary>The shared shape: build the temp file, put it over the target, clean up on failure.</summary>
    private static void Write(string path, Action<string> build)
    {
        var tmp = TempFor(path);
        try
        {
            build(tmp);
            Commit(tmp, path);
        }
        catch
        {
            Cleanup(tmp);
            throw;
        }
    }

    private static async Task WriteAsync(string path, Func<string, Task> build)
    {
        var tmp = TempFor(path);
        try
        {
            await build(tmp);
            Commit(tmp, path);
        }
        catch
        {
            Cleanup(tmp);
            throw;
        }
    }

    /// <summary>
    /// Put the temp file over the target.
    ///
    /// <see cref="File.Replace(string, string, string?)"/> preserves the target's attributes and only
    /// works when it exists; a first write has nothing to replace, hence the move.
    /// </summary>
    private static void Commit(string tmp, string path, string? backup = null)
    {
        if (File.Exists(path)) File.Replace(tmp, path, backup);
        else File.Move(tmp, path);
    }

    /// <summary>Derived from the target, so concurrent writers to different files cannot collide.</summary>
    private static string TempFor(string path) => path + ".tmp";

    /// <summary>Where a replaced target is kept until every file of a <see cref="WriteStreams"/> is in place.</summary>
    private static string BackupFor(string path) => path + ".bak";

    /// <summary>Best effort: the write already failed, and failing to tidy up must not mask why.</summary>
    private static void Cleanup(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* the original exception is the news */ }
    }
}
