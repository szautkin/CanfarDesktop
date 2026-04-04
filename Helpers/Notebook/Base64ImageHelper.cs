namespace CanfarDesktop.Helpers.Notebook;

using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

/// <summary>
/// Converts base64-encoded image data from notebook outputs to WinUI BitmapImage.
/// Must be called on the UI thread.
/// </summary>
public static class Base64ImageHelper
{
    public static async Task<BitmapImage?> DecodeAsync(string base64, int maxPixelWidth = 800)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;

        try
        {
            var cleaned = base64.Replace("\n", "").Replace("\r", "").Trim();
            var bytes = Convert.FromBase64String(cleaned);

            var bitmap = new BitmapImage();
            if (maxPixelWidth > 0)
                bitmap.DecodePixelWidth = maxPixelWidth;

            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);

            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Base64 image decode failed: {ex.Message}");
            return null;
        }
    }
}
