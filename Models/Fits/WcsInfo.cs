namespace CanfarDesktop.Models.Fits;

/// <summary>
/// World Coordinate System parameters extracted from FITS header.
/// Supports both CD matrix and CDELT+CROTA2 conventions.
/// </summary>
public record WcsInfo
{
    public double CrPix1 { get; init; }
    public double CrPix2 { get; init; }
    public double CrVal1 { get; init; }
    public double CrVal2 { get; init; }
    public double Cd1_1 { get; init; }
    public double Cd1_2 { get; init; }
    public double Cd2_1 { get; init; }
    public double Cd2_2 { get; init; }
    public string CType1 { get; init; } = string.Empty;
    public string CType2 { get; init; } = string.Empty;

    public bool IsValid => Cd1_1 != 0 || Cd2_2 != 0;

    /// <summary>
    /// Convert pixel (x, y) to world (RA, Dec) in degrees using the CD matrix.
    /// </summary>
    public (double Ra, double Dec) PixelToWorld(double px, double py)
    {
        var dx = px - CrPix1;
        var dy = py - CrPix2;
        var ra = CrVal1 + Cd1_1 * dx + Cd1_2 * dy;
        var dec = CrVal2 + Cd2_1 * dx + Cd2_2 * dy;
        return (ra, dec);
    }

    /// <summary>
    /// Format RA in degrees to sexagesimal (HH:MM:SS.ss).
    /// </summary>
    public static string FormatRa(double raDeg)
    {
        var ra = raDeg / 15.0; // degrees to hours
        if (ra < 0) ra += 24;
        var h = (int)ra;
        var m = (int)((ra - h) * 60);
        var s = (ra - h - m / 60.0) * 3600;
        return $"{h:D2}h{m:D2}m{s:00.00}s";
    }

    /// <summary>
    /// Format Dec in degrees to sexagesimal (+DD°MM'SS.s").
    /// </summary>
    public static string FormatDec(double decDeg)
    {
        var sign = decDeg >= 0 ? "+" : "-";
        var dec = Math.Abs(decDeg);
        var d = (int)dec;
        var m = (int)((dec - d) * 60);
        var s = (dec - d - m / 60.0) * 3600;
        return $"{sign}{d:D2}\u00b0{m:D2}'{s:00.0}\"";
    }

    /// <summary>
    /// Format as CADC resolver-compatible coordinate string.
    /// Format: "HH:MM:SS.ss,+DD:MM:SS.s" (no spaces, with decimal points)
    /// </summary>
    public static string FormatForResolver(double raDeg, double decDeg)
    {
        // RA: degrees → hours → HH:MM:SS.ss
        var ra = raDeg / 15.0;
        if (ra < 0) ra += 24;
        var rh = (int)ra;
        var rm = (int)((ra - rh) * 60);
        var rs = (ra - rh - rm / 60.0) * 3600;

        // Dec: degrees → DD:MM:SS.s
        var sign = decDeg >= 0 ? "+" : "-";
        var dec = Math.Abs(decDeg);
        var dd = (int)dec;
        var dm = (int)((dec - dd) * 60);
        var ds = (dec - dd - dm / 60.0) * 3600;

        // CADC format: seconds × 100 (RA) or × 10 (Dec) as integers, no decimal point
        var rsInt = (int)Math.Round(rs * 100);
        var dsInt = (int)Math.Round(ds * 10);
        return $"{rh:D2}:{rm:D2}:{rsInt:D4},{sign}{dd:D2}:{dm:D2}:{dsInt:D3}";
    }

    /// <summary>
    /// Extract WCS from a parsed FITS header.
    /// </summary>
    public static WcsInfo FromHeader(FitsHeader header)
    {
        var wcs = new WcsInfo
        {
            CrPix1 = header.GetDouble("CRPIX1"),
            CrPix2 = header.GetDouble("CRPIX2"),
            CrVal1 = header.GetDouble("CRVAL1"),
            CrVal2 = header.GetDouble("CRVAL2"),
            CType1 = header.GetString("CTYPE1") ?? "",
            CType2 = header.GetString("CTYPE2") ?? "",
        };

        // Prefer CD matrix
        if (header.Contains("CD1_1"))
        {
            return wcs with
            {
                Cd1_1 = header.GetDouble("CD1_1"),
                Cd1_2 = header.GetDouble("CD1_2"),
                Cd2_1 = header.GetDouble("CD2_1"),
                Cd2_2 = header.GetDouble("CD2_2"),
            };
        }

        // Fall back to CDELT + CROTA2
        var cdelt1 = header.GetDouble("CDELT1");
        var cdelt2 = header.GetDouble("CDELT2");
        var crota2 = header.GetDouble("CROTA2") * Math.PI / 180.0;
        return wcs with
        {
            Cd1_1 = cdelt1 * Math.Cos(crota2),
            Cd1_2 = -cdelt2 * Math.Sin(crota2),
            Cd2_1 = cdelt1 * Math.Sin(crota2),
            Cd2_2 = cdelt2 * Math.Cos(crota2),
        };
    }
}
