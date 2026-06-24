using System.Text;

namespace CanfarDesktop.Mcp.Transport;

public enum FrameMode
{
    /// <summary>One JSON document per line, terminated by a single LF (stdio / Claude Desktop).</summary>
    Ndjson,
    /// <summary>LSP-style <c>Content-Length: N\r\n\r\n</c> + N body bytes.</summary>
    ContentLength,
}

public sealed class FrameDecodeException : Exception
{
    public FrameDecodeException(string message) : base(message) { }
}

/// <summary>
/// Pure framing codec for the MCP transports. Encodes a complete JSON document into a frame, and a
/// stateful <see cref="Decoder"/> reassembles complete documents from a byte stream. 1-to-1 with the
/// macOS FrameCodec (limits + ndjson tolerances).
/// </summary>
public static class FrameCodec
{
    public const int MaxFrameBytes = 16 * 1024 * 1024;
    public const int MaxBufferBytes = 32 * 1024 * 1024;

    public static byte[] Encode(FrameMode mode, ReadOnlySpan<byte> payload)
    {
        switch (mode)
        {
            case FrameMode.Ndjson:
            {
                var buffer = new byte[payload.Length + 1];
                payload.CopyTo(buffer);
                buffer[payload.Length] = (byte)'\n';
                return buffer;
            }
            case FrameMode.ContentLength:
            {
                var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
                var buffer = new byte[header.Length + payload.Length];
                header.CopyTo(buffer, 0);
                payload.CopyTo(buffer.AsSpan(header.Length));
                return buffer;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>Streaming, stateful frame reassembler. Feed bytes via <see cref="Append"/>, drain via <see cref="TryReadFrame"/>.</summary>
    public sealed class Decoder
    {
        private readonly FrameMode _mode;
        private readonly List<byte> _buffer = new();

        public Decoder(FrameMode mode) => _mode = mode;

        public void Append(ReadOnlySpan<byte> data)
        {
            if (_buffer.Count + data.Length > MaxBufferBytes)
                throw new FrameDecodeException($"buffer overflow ({_buffer.Count + data.Length} > {MaxBufferBytes})");
            foreach (var b in data) _buffer.Add(b);
        }

        /// <summary>Pull the next complete frame; false (payload null) when more bytes are needed.</summary>
        public bool TryReadFrame(out byte[]? payload)
            => _mode == FrameMode.Ndjson ? TryReadNdjson(out payload) : TryReadContentLength(out payload);

        private bool TryReadNdjson(out byte[]? payload)
        {
            payload = null;
            var nl = _buffer.IndexOf((byte)'\n');
            if (nl < 0) return false;

            var end = nl;
            if (end > 0 && _buffer[end - 1] == (byte)'\r') end--; // tolerate CRLF

            if (end > MaxFrameBytes)
                throw new FrameDecodeException($"frame too large ({end} > {MaxFrameBytes})");

            payload = end == 0 ? Array.Empty<byte>() : _buffer.GetRange(0, end).ToArray();
            _buffer.RemoveRange(0, nl + 1);
            return true;
        }

        private bool TryReadContentLength(out byte[]? payload)
        {
            payload = null;

            // Locate the \r\n\r\n header terminator.
            var headerEnd = IndexOfHeaderTerminator();
            if (headerEnd < 0) return false;

            var headerText = Encoding.ASCII.GetString(_buffer.GetRange(0, headerEnd).ToArray());
            var length = ParseContentLength(headerText);
            if (length < 0) throw new FrameDecodeException("missing Content-Length header");
            if (length > MaxFrameBytes) throw new FrameDecodeException($"frame too large ({length} > {MaxFrameBytes})");

            var bodyStart = headerEnd + 4;
            if (_buffer.Count - bodyStart < length) return false; // body not fully arrived

            payload = _buffer.GetRange(bodyStart, length).ToArray();
            _buffer.RemoveRange(0, bodyStart + length);
            return true;
        }

        private int IndexOfHeaderTerminator()
        {
            for (var i = 0; i + 3 < _buffer.Count; i++)
                if (_buffer[i] == '\r' && _buffer[i + 1] == '\n' && _buffer[i + 2] == '\r' && _buffer[i + 3] == '\n')
                    return i;
            return -1;
        }

        private static int ParseContentLength(string header)
        {
            foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                if (line[..colon].Trim().Equals("content-length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line[(colon + 1)..].Trim(), out var n))
                    return n;
            }
            return -1;
        }
    }
}
