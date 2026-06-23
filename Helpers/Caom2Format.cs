using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>Friendly formatting helpers for CAOM2 values shown in the observation detail viewer.</summary>
public static class Caom2Format
{
    private const string Dash = "—"; // em dash for "no value"

    public static string Text(string? s) => string.IsNullOrWhiteSpace(s) ? Dash : s!;

    /// <summary>Human-readable byte size (B/KB/MB/GB/TB).</summary>
    public static string Bytes(long? bytes)
    {
        if (bytes is not { } b || b < 0) return Dash;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = b;
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return u == 0 ? $"{b} {units[u]}" : $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[u]}";
    }

    /// <summary>Wavelength in metres → friendly nm/µm/mm/m.</summary>
    public static string Wavelength(double? metres)
    {
        if (metres is not { } m || m <= 0 || !double.IsFinite(m)) return Dash;
        if (m < 1e-6) return $"{(m * 1e9).ToString("0.###", CultureInfo.InvariantCulture)} nm";
        if (m < 1e-3) return $"{(m * 1e6).ToString("0.###", CultureInfo.InvariantCulture)} µm";
        if (m < 1.0) return $"{(m * 1e3).ToString("0.###", CultureInfo.InvariantCulture)} mm";
        return $"{m.ToString("0.###", CultureInfo.InvariantCulture)} m";
    }

    public static string WavelengthRange(double? lower, double? upper)
        => lower is null && upper is null ? Dash : $"{Wavelength(lower)} – {Wavelength(upper)}";

    /// <summary>MJD (epoch 1858-11-17 UTC) → calendar UTC string.</summary>
    public static string MjdToDate(double? mjd)
    {
        if (mjd is not { } v || !double.IsFinite(v)) return Dash;
        var epoch = new DateTime(1858, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddDays(v).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    public static string Date(DateTimeOffset? d)
        => d is { } v ? v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Dash;

    public static string Degrees(double? d)
        => d is { } v && double.IsFinite(v) ? $"{v.ToString("0.######", CultureInfo.InvariantCulture)}°" : Dash;

    public static string Seconds(double? s)
        => s is { } v && double.IsFinite(v) ? $"{v.ToString("0.##", CultureInfo.InvariantCulture)} s" : Dash;

    public static string Number(double? d)
        => d is { } v && double.IsFinite(v) ? v.ToString("0.####", CultureInfo.InvariantCulture) : Dash;

    public static string Bool(bool? b) => b is null ? Dash : (b.Value ? "Yes" : "No");

    /// <summary>Last path segment of a cadc:/vos: artifact URI (the file name).</summary>
    public static string ArtifactFileName(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return uri;
        var slash = uri.LastIndexOf('/');
        return slash >= 0 && slash < uri.Length - 1 ? uri[(slash + 1)..] : uri;
    }
}
