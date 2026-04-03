using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Formats raw TAP cell values for display based on column key.
/// Matches the macOS CellFormatters implementation.
/// </summary>
public static class CellFormatter
{
    public static string Format(string columnKey, string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";

        var key = CleanKey(columnKey);

        return key switch
        {
            "startdate" or "enddate" or "provelastexecuted" => FormatMjdDate(trimmed),
            "ra(j20000)" or "dec(j20000)" => FormatCoordinate(trimmed, 5),
            "inttime" => FormatIntegrationTime(trimmed),
            "callev" => FormatCalibrationLevel(trimmed),
            "download" or "movingtarget" => FormatBoolean(trimmed),
            "minwavelength" or "maxwavelength" or "restframeenergy" => FormatWavelength(trimmed),
            "pixelscale" => FormatScientific(trimmed, 4),
            "fieldofview" => FormatScientific(trimmed, 6),
            "datarelease" => FormatTimestamp(trimmed),
            _ => trimmed
        };
    }

    /// <summary>Clean column header to a normalized key (lowercase, no quotes, spaces→underscores).</summary>
    public static string CleanKey(string header)
    {
        return header
            .Replace("\"", "")
            .Trim()
            .ToLower(CultureInfo.InvariantCulture)
            .Replace(" ", "")
            .Replace(".", "");
    }

    private static string FormatMjdDate(string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var mjd)) return raw;
        var unixSeconds = (mjd - 40587.0) * 86400.0;
        var dt = DateTimeOffset.UnixEpoch.AddSeconds(unixSeconds).UtcDateTime;
        return dt.ToString("yyyy-MM-dd");
    }

    private static string FormatCoordinate(string raw, int decimals)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return raw;
        return v.ToString($"F{decimals}");
    }

    private static string FormatIntegrationTime(string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)) return raw;
        if (secs >= 3600)
        {
            var h = secs / 3600.0;
            return Math.Abs(h - Math.Round(h)) < 0.01 ? $"{(int)h}h" : $"{h:F1}h";
        }
        if (secs >= 60)
        {
            var m = secs / 60.0;
            return Math.Abs(m - Math.Round(m)) < 0.01 ? $"{(int)m}m" : $"{m:F1}m";
        }
        return Math.Abs(secs - Math.Round(secs)) < 0.01 ? $"{(int)secs}s" : $"{secs:F1}s";
    }

    private static string FormatCalibrationLevel(string raw) => raw switch
    {
        "0" => "Raw",
        "1" => "Cal",
        "2" => "Product",
        "3" => "Composite",
        _ => raw
    };

    private static string FormatBoolean(string raw) =>
        raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1" ? "\u2713" : "";

    private static string FormatWavelength(string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return raw;
        return Math.Abs(v) < 0.001 || Math.Abs(v) > 1e6 ? v.ToString("E3") : v.ToString("G6");
    }

    private static string FormatScientific(string raw, int decimals)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return raw;
        return Math.Abs(v) < 0.001 || Math.Abs(v) > 1e6 ? v.ToString($"E{decimals}") : v.ToString($"G{decimals}");
    }

    private static string FormatTimestamp(string raw)
    {
        if (!raw.Contains('T') && !raw.Contains(' ')) return raw;
        var cleaned = raw.Replace("T", " ").Replace("Z", "");
        var dotIdx = cleaned.IndexOf('.', Math.Min(10, cleaned.Length));
        return dotIdx >= 0 ? cleaned[..dotIdx] : cleaned;
    }

    /// <summary>Default visible column keys (matching macOS).</summary>
    public static readonly HashSet<string> DefaultVisibleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "download", "preview",
        "collection", "targetname", "ra(j20000)", "dec(j20000)",
        "startdate", "instrument", "filter", "callev",
        "obstype", "proposalid", "piname", "obsid"
    };

    /// <summary>Column display width based on key.</summary>
    public static int ColumnWidth(string key) => key switch
    {
        "download" => 35,
        "preview" => 35,
        "collection" or "proposalid" or "obsid" => 100,
        "targetname" or "piname" => 110,
        "ra(j20000)" or "dec(j20000)" => 95,
        "startdate" or "enddate" or "datarelease" => 90,
        "instrument" => 90,
        "inttime" => 65,
        "filter" or "callev" or "band" => 60,
        "obstype" or "datatype" => 75,
        _ => 80
    };
}
