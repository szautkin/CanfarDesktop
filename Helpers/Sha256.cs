using System.Security.Cryptography;
using System.Text;

namespace CanfarDesktop.Helpers;

/// <summary>SHA-256 hex helper used to content-hash probe scripts for cache-busting upload names.</summary>
public static class Sha256
{
    public static string HexOf(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>First 12 hex chars of the SHA-256 — the script-hash used in probe filenames.</summary>
    public static string ShortHexOf(string text) => HexOf(text)[..12];
}
