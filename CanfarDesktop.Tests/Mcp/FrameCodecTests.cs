using System.Text;
using Xunit;
using CanfarDesktop.Mcp.Transport;

namespace CanfarDesktop.Tests.Mcp;

public class FrameCodecTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);
    private static string Str(byte[]? b) => Encoding.UTF8.GetString(b!);

    [Fact]
    public void Encode_Ndjson_AppendsSingleLf()
    {
        var f = FrameCodec.Encode(FrameMode.Ndjson, Bytes("{}"));
        Assert.Equal("{}\n", Encoding.UTF8.GetString(f));
    }

    [Fact]
    public void Encode_ContentLength_HasHeaderAndBody()
    {
        var f = Encoding.ASCII.GetString(FrameCodec.Encode(FrameMode.ContentLength, Bytes("abc")));
        Assert.Equal("Content-Length: 3\r\n\r\nabc", f);
    }

    [Fact]
    public void Ndjson_ReadsTwoFrames()
    {
        var d = new FrameCodec.Decoder(FrameMode.Ndjson);
        d.Append(Bytes("first\nsecond\n"));
        Assert.True(d.TryReadFrame(out var a));
        Assert.True(d.TryReadFrame(out var b));
        Assert.False(d.TryReadFrame(out _));
        Assert.Equal("first", Str(a));
        Assert.Equal("second", Str(b));
    }

    [Fact]
    public void Ndjson_ReassemblesAcrossAppends()
    {
        var d = new FrameCodec.Decoder(FrameMode.Ndjson);
        d.Append(Bytes("hel"));
        Assert.False(d.TryReadFrame(out _));
        d.Append(Bytes("lo\n"));
        Assert.True(d.TryReadFrame(out var a));
        Assert.Equal("hello", Str(a));
    }

    [Fact]
    public void Ndjson_ToleratesCrlf()
    {
        var d = new FrameCodec.Decoder(FrameMode.Ndjson);
        d.Append(Bytes("x\r\n"));
        Assert.True(d.TryReadFrame(out var a));
        Assert.Equal("x", Str(a)); // trailing CR stripped
    }

    [Fact]
    public void Ndjson_EmptyLeadingLine_IsKeepAliveFrame()
    {
        var d = new FrameCodec.Decoder(FrameMode.Ndjson);
        d.Append(Bytes("\ndata\n"));
        Assert.True(d.TryReadFrame(out var keepAlive));
        Assert.Empty(keepAlive!);
        Assert.True(d.TryReadFrame(out var data));
        Assert.Equal("data", Str(data));
    }

    [Fact]
    public void ContentLength_ReadsBody()
    {
        var d = new FrameCodec.Decoder(FrameMode.ContentLength);
        d.Append(Bytes("Content-Length: 5\r\n\r\nhello"));
        Assert.True(d.TryReadFrame(out var a));
        Assert.Equal("hello", Str(a));
    }

    [Fact]
    public void ContentLength_WaitsForFullBody()
    {
        var d = new FrameCodec.Decoder(FrameMode.ContentLength);
        d.Append(Bytes("Content-Length: 5\r\n\r\nhel"));
        Assert.False(d.TryReadFrame(out _)); // body incomplete
        d.Append(Bytes("lo"));
        Assert.True(d.TryReadFrame(out var a));
        Assert.Equal("hello", Str(a));
    }

    [Fact]
    public void RoundTrip_BothModes()
    {
        foreach (var mode in new[] { FrameMode.Ndjson, FrameMode.ContentLength })
        {
            var d = new FrameCodec.Decoder(mode);
            d.Append(FrameCodec.Encode(mode, Bytes("""{"jsonrpc":"2.0"}""")));
            Assert.True(d.TryReadFrame(out var a));
            Assert.Equal("""{"jsonrpc":"2.0"}""", Str(a));
        }
    }
}
