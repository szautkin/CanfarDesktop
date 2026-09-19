namespace CanfarDesktop.Helpers;

/// <summary>
/// Copy a stream to a file, atomically, reporting progress.
///
/// <para>This existed twice. <c>ObservationDownloadService</c> wrote to a sibling <c>.tmp</c> and put
/// it over the target at the end, so an interrupted transfer left the previous file intact.
/// <c>download_vospace_file</c> forked the same loop and opened the DESTINATION with
/// <see cref="FileMode.Create"/> — so a cancelled or failed download truncated the user's existing
/// file and left a partial one in its place. The original grew atomicity and the copy did not, which
/// is the usual way a fork goes wrong: not immediately, but at the next fix.</para>
/// </summary>
public static class StreamToFile
{
    /// <summary>80 KB, the framework's own default copy buffer.</summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// Copy <paramref name="source"/> to <paramref name="path"/> and return the bytes written.
    ///
    /// <para><paramref name="validateTotal"/> runs after the last byte and BEFORE the file is put over
    /// the target; returning an exception aborts the write, so the temp file is removed and whatever
    /// was already at <paramref name="path"/> is untouched. That is where a caller puts a policy like
    /// "zero bytes is not a download" — a policy that belongs to the caller, because a zero-byte file
    /// in VOSpace is a legitimate thing to fetch and a zero-byte archive response is not.</para>
    /// </summary>
    public static async Task<long> WriteAsync(
        Stream source,
        string path,
        long? expectedTotal = null,
        IProgress<(long Downloaded, long? Total)>? progress = null,
        Func<long, Exception?>? validateTotal = null,
        CancellationToken ct = default)
    {
        long total = 0;

        await AtomicFile.WriteStreamAsync(path, async destination =>
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                progress?.Report((total, expectedTotal));
            }

            // Thrown from INSIDE the write so the temp file is cleaned up and the target never changes.
            if (validateTotal?.Invoke(total) is { } refusal) throw refusal;
        }, ct);

        return total;
    }
}
