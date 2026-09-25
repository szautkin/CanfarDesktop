using System.Text;
using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The write-to-temp-then-replace idiom existed five times as the same three lines. They agreed on the
/// important part and differed in one: only the download path removed its temp file when the write
/// failed, so a notebook save that threw left <c>analysis.ipynb.tmp</c> beside the notebook.
/// </summary>
public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "verbinal-atomic-" + Guid.NewGuid().ToString("N")[..8]);

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    // ── The ordinary cases ───────────────────────────────────────────────────

    [Fact]
    public void WritesAFileThatDidNotExist()
    {
        var path = Path_("new.txt");
        AtomicFile.WriteAllText(path, "hello");

        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void ReplacesAFileThatDid()
    {
        var path = Path_("existing.txt");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public async Task WritesAsynchronously()
    {
        var path = Path_("async.txt");
        await AtomicFile.WriteAllTextAsync(path, "written");

        Assert.Equal("written", File.ReadAllText(path));
    }

    /// <summary>The stream form, for a serializer that writes rather than returns.</summary>
    [Fact]
    public async Task WritesThroughAStream()
    {
        var path = Path_("stream.bin");
        await AtomicFile.WriteStreamAsync(path, async s =>
        {
            var bytes = Encoding.UTF8.GetBytes("from a stream");
            await s.WriteAsync(bytes);
        });

        Assert.Equal("from a stream", File.ReadAllText(path));
    }

    /// <summary>Nothing is left behind on a successful write either.</summary>
    [Fact]
    public void LeavesNoTempFileBehindOnSuccess()
    {
        var path = Path_("clean.txt");
        AtomicFile.WriteAllText(path, "x");

        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    // ── The divergence it fixes ──────────────────────────────────────────────

    /// <summary>
    /// The one the five copies disagreed on. A write that throws must not leave its temp file: a
    /// failed save should not litter the user's folder with <c>notebook.ipynb.tmp</c>.
    /// </summary>
    [Fact]
    public async Task RemovesTheTempFileWhenTheWriteFails()
    {
        var path = Path_("doomed.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicFile.WriteStreamAsync(path, _ => throw new InvalidOperationException("serializer blew up")));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    /// <summary>And the original exception survives the tidying up — the cleanup is not the news.</summary>
    [Fact]
    public async Task PropagatesTheOriginalFailure()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicFile.WriteStreamAsync(Path_("doomed2.txt"), _ => throw new InvalidOperationException("the real reason")));

        Assert.Equal("the real reason", ex.Message);
    }

    /// <summary>
    /// A failed write must leave the PREVIOUS contents intact. Replacing is the last step for exactly
    /// this reason: a reader during the failure sees the old file, not a truncated one.
    /// </summary>
    [Fact]
    public async Task LeavesTheExistingFileUntouchedWhenTheWriteFails()
    {
        var path = Path_("precious.txt");
        File.WriteAllText(path, "the good version");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicFile.WriteStreamAsync(path, _ => throw new InvalidOperationException("nope")));

        Assert.Equal("the good version", File.ReadAllText(path));
    }

    // ── The temp name ────────────────────────────────────────────────────────

    /// <summary>
    /// Derived from the TARGET, not a fixed name. A shared temp name is how two writers to different
    /// files rename each other's half-written file into place, which is a data-loss bug rather than a
    /// tidiness one.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritesToDifferentFilesDoNotCollide()
    {
        var a = Path_("a.txt");
        var b = Path_("b.txt");

        await Task.WhenAll(
            Task.Run(() => { for (var i = 0; i < 50; i++) AtomicFile.WriteAllText(a, "aaaa"); }),
            Task.Run(() => { for (var i = 0; i < 50; i++) AtomicFile.WriteAllText(b, "bbbb"); }));

        Assert.Equal("aaaa", File.ReadAllText(a));
        Assert.Equal("bbbb", File.ReadAllText(b));
    }

    [Fact]
    public void HonoursAnExplicitEncoding()
    {
        var path = Path_("utf32.txt");
        AtomicFile.WriteAllText(path, "é", new UTF32Encoding());

        Assert.Equal("é", File.ReadAllText(path, new UTF32Encoding()));
    }
}
