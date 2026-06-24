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

    public StreamTransport(Stream input, Stream output, FrameMode mode = FrameMode.Ndjson)
    {
        _input = input;
        _output = output;
        _mode = mode;
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
                var n = await _input.ReadAsync(buffer, ct);
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
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
