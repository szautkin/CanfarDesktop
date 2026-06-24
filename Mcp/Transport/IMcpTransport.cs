using System.Text;
using System.Threading.Channels;

namespace CanfarDesktop.Mcp.Transport;

/// <summary>
/// A bidirectional MCP message channel. <see cref="Incoming"/> yields COMPLETE, unframed JSON
/// documents (framing lives in the transport); <see cref="SendAsync"/> takes one complete document
/// and the transport adds framing + writes it atomically. Mirrors the macOS MCPTransport.
/// </summary>
public interface IMcpTransport
{
    ChannelReader<byte[]> Incoming { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    ValueTask CloseAsync();
}

/// <summary>
/// In-memory duplex transport for unit-testing the server with NO OS dependency: a test injects
/// request documents and reads the server's response documents.
/// </summary>
public sealed class InMemoryTransport : IMcpTransport
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>();

    public ChannelReader<byte[]> Incoming => _incoming.Reader;

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _outgoing.Writer.TryWrite(payload.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync()
    {
        _incoming.Writer.TryComplete();
        _outgoing.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    public void Inject(string json) => _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(json));

    public void CompleteIncoming() => _incoming.Writer.TryComplete();

    /// <summary>Read the next response document the server sent, or null when the stream is done.</summary>
    public async Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default)
    {
        if (await _outgoing.Reader.WaitToReadAsync(cancellationToken) && _outgoing.Reader.TryRead(out var bytes))
            return Encoding.UTF8.GetString(bytes);
        return null;
    }

    public bool HasPendingResponse => _outgoing.Reader.Count > 0;
}
