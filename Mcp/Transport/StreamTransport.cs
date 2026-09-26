using System.Threading.Channels;

namespace CanfarDesktop.Mcp.Transport;

/// <summary>
/// <see cref="IMcpTransport"/> over a read <see cref="Stream"/> + a write <see cref="Stream"/> with
/// <see cref="FrameCodec"/> framing. A duplex named pipe passes the same stream for both; stdio passes
/// stdin + stdout. A background loop reassembles frames into <see cref="Incoming"/>; writes are
/// framed + serialized behind a lock. Stream-based so it's testable with MemoryStream (the OS-specific
/// stdin/pipe wiring lives in the factory helpers).
/// </summary>
public sealed class StreamTransport : IMcpTransport, IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly FrameMode _mode;
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly FrameCodec.Decoder _decoder;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;
    private readonly bool _ownsStreams;

    /// <param name="ownsStreams">Dispose the streams on close. For a connection that is replaced rather
    /// than lasting the whole process — the bridge's pipe, redialled each time the app comes back.</param>
    public StreamTransport(Stream input, Stream output, FrameMode mode = FrameMode.Ndjson, bool ownsStreams = false)
    {
        _input = input;
        _output = output;
        _mode = mode;
        _ownsStreams = ownsStreams;
        _decoder = new FrameCodec.Decoder(mode);
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public ChannelReader<byte[]> Incoming => _incoming.Reader;

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var frame = FrameCodec.Encode(_mode, payload.Span);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(frame, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // WaitAsync, because not every stream honours the token. Console stdin does not: its
                // read blocks until the client writes or closes, so CloseAsync waited on it forever,
                // and a bridge whose app had quit sat there holding its exe open until the client's
                // next request woke it — which it then dropped unanswered. The abandoned read is
                // harmless: this loop is its only reader, and it has stopped.
                var n = await _input.ReadAsync(buffer, ct).AsTask().WaitAsync(ct);
                if (n == 0) break; // EOF
                _decoder.Append(buffer.AsSpan(0, n));
                while (_decoder.TryReadFrame(out var frame))
                    _incoming.Writer.TryWrite(frame!);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (FrameDecodeException) { }
        finally
        {
            _incoming.Writer.TryComplete();
        }
    }

    public async ValueTask CloseAsync()
    {
        _cts.Cancel();
        _incoming.Writer.TryComplete();
        try { await _readLoop; } catch { /* shutting down */ }

        if (_ownsStreams)
        {
            await _input.DisposeAsync();
            if (!ReferenceEquals(_output, _input)) await _output.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
