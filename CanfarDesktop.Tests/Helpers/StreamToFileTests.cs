using System.Text;
using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The streaming copy existed twice. The observation download wrote to a temp and put it over the
/// target at the end; <c>download_vospace_file</c> forked the loop and opened the DESTINATION with
/// <c>FileMode.Create</c>, so a cancelled transfer truncated the user's existing file and left a
/// partial one in its place.
/// </summary>
public class StreamToFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "verbinal-streamcopy-" + Guid.NewGuid().ToString("N")[..8]);

    public StreamToFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_dir, name);
    private static MemoryStream Source(string text) => new(Encoding.UTF8.GetBytes(text));

    // ── The copy ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopiesTheStreamAndReportsTheByteCount()
    {
        var path = Path_("out.bin");

        var written = await StreamToFile.WriteAsync(Source("hello world"), path);

        Assert.Equal(11, written);
        Assert.Equal("hello world", File.ReadAllText(path));
    }

    /// <summary>Larger than one buffer, so the loop actually loops.</summary>
    [Fact]
    public async Task CopiesAcrossManyBuffers()
    {
        var payload = new string('x', 300_000);
        var path = Path_("big.bin");

        var written = await StreamToFile.WriteAsync(Source(payload), path);

        Assert.Equal(300_000, written);
        Assert.Equal(300_000, new FileInfo(path).Length);
    }

    /// <summary>
    /// The progress bar's numbers. Collected on a plain sink rather than <see cref="Progress{T}"/>,
    /// which posts to a synchronization context and would make this a race.
    /// </summary>
    [Fact]
    public async Task ReportsProgressWithTheExpectedTotal()
    {
        var sink = new CollectingProgress();

        await StreamToFile.WriteAsync(Source(new string('y', 200_000)), Path_("p.bin"),
            expectedTotal: 200_000, progress: sink);

        Assert.NotEmpty(sink.Reports);
        Assert.All(sink.Reports, r => Assert.Equal(200_000, r.Total));
        Assert.Equal(200_000, sink.Reports[^1].Downloaded);   // the last report is the whole file
    }

    private sealed class CollectingProgress : IProgress<(long Downloaded, long? Total)>
    {
        public List<(long Downloaded, long? Total)> Reports { get; } = [];
        public void Report((long Downloaded, long? Total) value) => Reports.Add(value);
    }

    // ── Atomicity, which the VOSpace copy did not have ───────────────────────

    /// <summary>
    /// The regression the fork caused. A transfer that fails must leave the file that was already
    /// there exactly as it was — not truncated, not partial.
    /// </summary>
    [Fact]
    public async Task AFailedCopyLeavesTheExistingFileIntact()
    {
        var path = Path_("precious.bin");
        File.WriteAllText(path, "the good version");

        await Assert.ThrowsAsync<IOException>(() =>
            StreamToFile.WriteAsync(new ThrowingStream("the network gave up"), path));

        Assert.Equal("the good version", File.ReadAllText(path));
    }

    [Fact]
    public async Task AFailedCopyLeavesNoPartialFileBehind()
    {
        await Assert.ThrowsAsync<IOException>(() =>
            StreamToFile.WriteAsync(new ThrowingStream("boom"), Path_("never.bin")));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    /// <summary>Cancelling mid-transfer is the same story as failing mid-transfer.</summary>
    [Fact]
    public async Task ACancelledCopyLeavesTheExistingFileIntact()
    {
        var path = Path_("cancelled.bin");
        File.WriteAllText(path, "before");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StreamToFile.WriteAsync(Source(new string('z', 500_000)), path, ct: cts.Token));

        Assert.Equal("before", File.ReadAllText(path));
    }

    // ── Stalls ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A transfer that goes quiet fails, and fails as a stall. The observation download relied on a
    /// "120 s" that only covered the response starting, so a dead body held the agent's apply gate —
    /// and every other write behind it — for as long as the connection stayed open.
    /// </summary>
    [Fact]
    public async Task AStalledTransfer_FailsAsATimeout_AndLeavesTheExistingFileIntact()
    {
        var path = Path_("stalled.bin");
        File.WriteAllText(path, "before");

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            StreamToFile.WriteAsync(new StallingStream(chunks: 2), path, stallTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Contains("stalled", ex.Message);
        Assert.Equal("before", File.ReadAllText(path));
    }

    /// <summary>
    /// The limit is on SILENCE, not on the transfer: one that keeps arriving runs as long as it needs.
    /// A total would have to choose between cutting off a 1.6 GB tile and never catching a dead one.
    /// </summary>
    [Fact]
    public async Task ATransferThatKeepsArriving_OutlastsTheStallTimeout()
    {
        var path = Path_("slow-but-alive.bin");

        // Twenty chunks 60 ms apart: 1.2 s in all, past the 1 s limit, and no gap within a sixteenth of
        // it — margin enough that a busy test run's thread pool cannot turn a gap into a stall.
        var written = await StreamToFile.WriteAsync(
            new StallingStream(chunks: 20, gap: TimeSpan.FromMilliseconds(60), thenEnd: true), path,
            stallTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(20 * StallingStream.ChunkSize, written);
    }

    /// <summary>The caller's cancel is still a cancel, not a stall.</summary>
    [Fact]
    public async Task CancellingDuringAStall_IsACancel()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StreamToFile.WriteAsync(new StallingStream(chunks: 1), Path_("cancel.bin"),
                ct: cts.Token, stallTimeout: TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// Hands out <c>chunks</c> chunks, <c>gap</c> apart, then either ends or goes silent until
    /// cancelled — a connection that stops sending without closing.
    /// </summary>
    private sealed class StallingStream(int chunks, TimeSpan? gap = null, bool thenEnd = false) : Stream
    {
        public const int ChunkSize = 1024;
        private int _sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_sent >= chunks)
            {
                if (thenEnd) return 0;
                await Task.Delay(Timeout.Infinite, ct);
            }
            if (gap is { } wait) await Task.Delay(wait, ct);
            _sent++;
            var n = Math.Min(ChunkSize, buffer.Length);
            buffer.Span[..n].Fill((byte)'s');
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ── The caller's own policy ──────────────────────────────────────────────

    /// <summary>
    /// The validation runs BEFORE the file is put over the target, so a refusal leaves the previous
    /// file untouched — which is the whole point of refusing rather than filing it.
    /// </summary>
    [Fact]
    public async Task ValidateTotal_AbortsBeforeTheTargetIsTouched()
    {
        var path = Path_("guarded.bin");
        File.WriteAllText(path, "the old file");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StreamToFile.WriteAsync(Source(""), path,
                validateTotal: written => written == 0 ? new InvalidOperationException("empty") : null));

        Assert.Equal("the old file", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    /// <summary>
    /// And it is the CALLER's policy, not the copier's: a zero-byte file in VOSpace is a legitimate
    /// thing to fetch, so without a validator an empty stream writes an empty file.
    /// </summary>
    [Fact]
    public async Task WithoutAValidator_AnEmptyStreamWritesAnEmptyFile()
    {
        var path = Path_("legitimately-empty.bin");

        var written = await StreamToFile.WriteAsync(Source(""), path);

        Assert.Equal(0, written);
        Assert.True(File.Exists(path));
        Assert.Equal(0, new FileInfo(path).Length);
    }

    [Fact]
    public async Task ValidateTotal_ReturningNull_LetsTheWriteThrough()
    {
        var path = Path_("fine.bin");

        await StreamToFile.WriteAsync(Source("data"), path, validateTotal: _ => null);

        Assert.Equal("data", File.ReadAllText(path));
    }

    /// <summary>A stream that fails partway, to stand in for a dropped transfer.</summary>
    private sealed class ThrowingStream : Stream
    {
        private readonly string _message;
        private int _reads;

        public ThrowingStream(string message) => _message = message;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ == 0) return Math.Min(count, 16);   // some bytes land first
            throw new IOException(_message);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => ValueTask.FromResult(Read(new byte[buffer.Length], 0, buffer.Length));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
