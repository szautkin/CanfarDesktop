using System.Text;
using Xunit;
using CanfarDesktop.Mcp.Transport;

namespace CanfarDesktop.Tests.Mcp;

public class StreamTransportTests
{
    private static byte[] Frame(string s) => FrameCodec.Encode(FrameMode.Ndjson, Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task ReadLoop_ReassemblesFramesUntilEof()
    {
        var input = new MemoryStream();
        input.Write(Frame("alpha"));
        input.Write(Frame("beta"));
        input.Position = 0;

        await using var transport = new StreamTransport(input, new MemoryStream());

        var got = new List<string>();
        await foreach (var frame in transport.Incoming.ReadAllAsync())
            got.Add(Encoding.UTF8.GetString(frame));

        Assert.Equal(new[] { "alpha", "beta" }, got);
    }

    [Fact]
    public async Task ReadLoop_HandlesFrameSplitAcrossReads()
    {
        // A frame whose bytes arrive in two chunks must still reassemble.
        var full = Frame("a-document-split-across-reads");
        var input = new ChunkedStream(full[..5], full[5..]);

        await using var transport = new StreamTransport(input, new MemoryStream());

        var got = new List<string>();
        await foreach (var frame in transport.Incoming.ReadAllAsync())
            got.Add(Encoding.UTF8.GetString(frame));

        Assert.Equal(new[] { "a-document-split-across-reads" }, got);
    }

    [Fact]
    public async Task Send_WritesFramedBytes()
    {
        var output = new MemoryStream();
        await using var transport = new StreamTransport(new MemoryStream(), output);

        await transport.SendAsync(Encoding.UTF8.GetBytes("hello"), default);

        Assert.Equal(Frame("hello"), output.ToArray());
    }

    /// <summary>A stream that hands back its data in preset chunks to exercise frame reassembly.</summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly Queue<byte[]> _chunks;
        public ChunkedStream(params byte[][] chunks) => _chunks = new Queue<byte[]>(chunks);

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_chunks.Count == 0) return 0;
            var chunk = _chunks.Dequeue();
            Array.Copy(chunk, 0, buffer, offset, chunk.Length);
            return chunk.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_chunks.Count == 0) return ValueTask.FromResult(0);
            var chunk = _chunks.Dequeue();
            chunk.CopyTo(buffer);
            return ValueTask.FromResult(chunk.Length);
        }

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
