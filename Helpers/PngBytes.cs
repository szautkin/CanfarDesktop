using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Rasterised pixels as PNG bytes, in memory.
///
/// Both viewers' agent captures need exactly this and nothing else: the figure exports write to a file
/// and can encode straight into it, but a capture is handed back over the wire and must never touch the
/// disk. One implementation, because two would be the same fifteen lines with one of them eventually
/// growing a fix the other did not.
/// </summary>
public static class PngBytes
{
    /// <summary>Encode premultiplied BGRA8 pixels as a PNG.</summary>
    public static async Task<byte[]> FromBgraAsync(byte[] bgra, int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)width, (uint)height, 96, 96, bgra);
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
