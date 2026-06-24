using System.IO.Pipes;

namespace CanfarDesktop.Mcp.Transport;

/// <summary>
/// OS wiring for <see cref="StreamTransport"/>: the duplex named pipe (one stream for both directions)
/// and the bridge's stdin/stdout pair. Kept apart from <see cref="StreamTransport"/> itself so the
/// framing/pump logic stays unit-testable with MemoryStream while this thin, untestable glue is the
/// only OS-coupled surface.
/// </summary>
public static class OsTransports
{
    /// <summary>A duplex named pipe — the same stream reads and writes.</summary>
    public static StreamTransport ForPipe(PipeStream pipe, FrameMode mode = FrameMode.Ndjson)
        => new(pipe, pipe, mode);

    /// <summary>The bridge process's standard input/output, used to talk to the MCP client.</summary>
    public static StreamTransport ForStdio(FrameMode mode = FrameMode.Ndjson)
        => new(Console.OpenStandardInput(), Console.OpenStandardOutput(), mode);
}
