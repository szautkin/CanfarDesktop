using CanfarDesktop.Helpers;

namespace CanfarDesktop.Models.Fits;

/// <summary>
/// World Coordinate System parameters extracted from a FITS header.
/// Supports CD matrix and CDELT+CROTA2 conventions, the four common zenithal
/// projections (TAN/SIN/STG/ZEA) with a linear fallback, and an approximate
/// reconstruction from legacy RA/DEC keywords.
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

    /// <summary>
    /// SIP distortion coefficients (Shupe et al. 2005), indexed [p, q] = coefficient of uᵖvᵍ.
    /// Forward (A/B): pixel offset → distortion, applied before the CD matrix in PixelToWorld.
    /// Inverse (AP/BP): applied after the inverse CD matrix in WorldToPixel. Null = no SIP.
    /// Matters enormously for wide-field mosaics — a TESS FFI's corner is ~22′ off without it.
    /// </summary>
    public double[,]? SipA { get; init; }
    public double[,]? SipB { get; init; }
    public double[,]? SipAp { get; init; }
    public double[,]? SipBp { get; init; }

    /// <summary>
    /// True when the WCS was reconstructed from legacy (non-standard) RA/DEC keywords
    /// rather than a real CD/CDELT solution. Spatial operations (crosshair, Go To,
    /// Search Here) are approximate and the UI should warn the user.
    /// </summary>
    public bool IsApproximate { get; init; }

    public bool IsValid => Cd1_1 != 0 || Cd2_2 != 0;

    /// <summary>
    /// Rotation angle in degrees from celestial North (measured East of North).
    /// To display North-up, rotate the image by -NorthAngle.
    /// </summary>
    public double NorthAngle => Math.Atan2(-Cd1_2, Cd2_2) * 180.0 / Math.PI;

    /// <summary>
    /// True if the image has a parity flip (East appears right instead of left).
    /// When true, the image should be mirrored horizontally to show standard orientation.
    /// Most instruments produce negative determinant (no flip needed).
    /// </summary>
    public bool HasParityFlip => (Cd1_1 * Cd2_2 - Cd1_2 * Cd2_1) > 0;

    /// <summary>
    /// Pixel scale in arcseconds per pixel (geometric mean of axis scales).
    /// </summary>
    public double PixelScaleArcsec
    {
        get
        {
            var sx = Math.Sqrt(Cd1_1 * Cd1_1 + Cd2_1 * Cd2_1);
            var sy = Math.Sqrt(Cd1_2 * Cd1_2 + Cd2_2 * Cd2_2);
            return Math.Sqrt(sx * sy) * 3600.0; // degrees to arcsec
        }
    }

    #region Projection

    /// <summary>Zenithal projection family resolved from CTYPE, with a linear fallback.</summary>
    public enum Projection { Tan, Sin, Stg, Zea, Linear }

    /// <summary>
    /// Resolved projection. Both axes must agree on the projection code; otherwise we
    /// fall back to <see cref="Projection.Linear"/>.
    /// </summary>
    public Projection Proj
    {
        get
        {
            var p1 = ProjectionCode(CType1);
            var p2 = ProjectionCode(CType2);
            if (!string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase)) return Projection.Linear;
            return p1.ToUpperInvariant() switch
            {
                "TAN" => Projection.Tan,
                "SIN" => Projection.Sin,
                "STG" => Projection.Stg,
                "ZEA" => Projection.Zea,
                _ => Projection.Linear,
            };
        }
    }

    /// <summary>
    /// Projection algorithm code from a CTYPE string. Per FITS WCS Paper II, CTYPE is
    /// "&lt;coord&gt;-&lt;ALGO&gt;" so the algorithm is the token AFTER the coordinate name
    /// ("RA---TAN" → "TAN"), and a further token is a distortion suffix, NOT the projection
    /// ("RA---TAN-SIP" → "TAN", never "SIP"). Taking the last token misread every SIP image as
    /// linear — catastrophic for wide TAN fields like TESS FFIs.
    /// </summary>
    private static string ProjectionCode(string ctype)
    {
        var parts = ctype.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    /// <summary>Evaluate a SIP polynomial Σ c[p,q]·uᵖ·vᵍ.</summary>
    private static double SipPoly(double[,] c, double u, double v)
    {
        var order = c.GetLength(0) - 1;
        double sum = 0;
        for (var p = 0; p <= order; p++)
            for (var q = 0; q <= order - p; q++)
                if (c[p, q] != 0) sum += c[p, q] * Math.Pow(u, p) * Math.Pow(v, q);
        return sum;
    }

    #endregion

    /// <summary>
    /// Convert pixel (x, y) to world (RA, Dec) in degrees. Uses a rigorous spherical
    /// deprojection for TAN/SIN/STG/ZEA and a linear fallback otherwise.
    /// </summary>
    public (double Ra, double Dec) PixelToWorld(double px, double py)
    {
        var dx = px - CrPix1;
        var dy = py - CrPix2;
        // SIP forward distortion (before the CD matrix): the true intermediate pixel offset is
        // (u + f(u,v), v + g(u,v)).
        if (SipA is not null && SipB is not null)
        {
            var fx = dx + SipPoly(SipA, dx, dy);
            var fy = dy + SipPoly(SipB, dx, dy);
            dx = fx;
            dy = fy;
        }
        // Intermediate world coordinates (ξ, η) in degrees via the CD matrix.
        var xi = Cd1_1 * dx + Cd1_2 * dy;
        var eta = Cd2_1 * dx + Cd2_2 * dy;

        var world = Deproject(xi, eta, CrVal1, CrVal2, Proj);
        if (world is not null) return world.Value;
        // Unknown projection or out-of-domain plane point: linear interpolation so the
        // UI can still display something near the reference pixel.
        return (CrVal1 + xi, CrVal2 + eta);
    }

    /// <summary>
    /// Convert world (RA, Dec) in degrees to pixel (x, y). Returns null if the CD matrix
    /// is singular or the coordinate is outside the projection's domain.
    /// </summary>
    public (double Px, double Py)? WorldToPixel(double ra, double dec)
    {
        var det = Cd1_1 * Cd2_2 - Cd1_2 * Cd2_1;
        if (Math.Abs(det) < 1e-30) return null;

        double xi, eta;
        var plane = Project(ra, dec, CrVal1, CrVal2, Proj);
        if (plane is not null)
        {
            xi = plane.Value.Xi;
            eta = plane.Value.Eta;
        }
        else if (Proj == Projection.Linear)
        {
            xi = ra - CrVal1;
            eta = dec - CrVal2;
        }
        else
        {
            return null; // out of projection domain
        }

        var dx = (Cd2_2 * xi - Cd1_2 * eta) / det;
        var dy = (-Cd2_1 * xi + Cd1_1 * eta) / det;
        // SIP inverse distortion (after the inverse CD matrix): AP/BP map the corrected offset
        // back to the true pixel offset.
        if (SipAp is not null && SipBp is not null)
        {
            var u = dx + SipPoly(SipAp, dx, dy);
            var v = dy + SipPoly(SipBp, dx, dy);
            dx = u;
            dy = v;
        }
        return (CrPix1 + dx, CrPix2 + dy);
    }

    #region Spherical projection math (Calabretta & Greisen 2002, A&A 395, 1077)

    // For all zenithal projections the math factors as:
    //   1. Angular distance ψ between target and reference, plus position angle B.
    //   2. Map ψ → ρ via a projection-specific radial law:
    //        TAN: ρ = tan(ψ)   SIN: ρ = sin(ψ)   STG: ρ = 2·tan(ψ/2)   ZEA: ρ = 2·sin(ψ/2)
    //   3. ξ = ρ·sin(B), η = ρ·cos(B). The inverse runs the chain backwards.

    /// <summary>
    /// Forward project (RA, Dec) → intermediate world (ξ, η) in degrees. Returns null for
    /// projection-domain violations or a degenerate Linear request.
    /// </summary>
    public static (double Xi, double Eta)? Project(double ra, double dec, double crval1, double crval2, Projection projection)
    {
        if (projection == Projection.Linear) return null;

        var raRad = ra * Math.PI / 180.0;
        var decRad = dec * Math.PI / 180.0;
        var ra0 = crval1 * Math.PI / 180.0;
        var dec0 = crval2 * Math.PI / 180.0;
        const double radToDeg = 180.0 / Math.PI;

        var cosPsi = Math.Sin(decRad) * Math.Sin(dec0) + Math.Cos(decRad) * Math.Cos(dec0) * Math.Cos(raRad - ra0);
        var xNum = Math.Cos(decRad) * Math.Sin(raRad - ra0);
        var yNum = Math.Sin(decRad) * Math.Cos(dec0) - Math.Cos(decRad) * Math.Sin(dec0) * Math.Cos(raRad - ra0);

        switch (projection)
        {
            case Projection.Tan:
                if (cosPsi <= 1e-12) return null; // forward hemisphere only
                return (xNum / cosPsi * radToDeg, yNum / cosPsi * radToDeg);
            case Projection.Sin:
                return (xNum * radToDeg, yNum * radToDeg);
            case Projection.Stg:
            {
                var denom = 1 + cosPsi;
                if (denom <= 1e-12) return null; // antipode
                return (2 * xNum / denom * radToDeg, 2 * yNum / denom * radToDeg);
            }
            case Projection.Zea:
            {
                if (cosPsi <= -1 + 1e-12) return null; // antipode
                var factor = Math.Sqrt(2 / (1 + cosPsi));
                return (xNum * factor * radToDeg, yNum * factor * radToDeg);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Inverse project intermediate world (ξ, η) in degrees → (RA, Dec). Returns null when
    /// the input is outside the projection's domain (SIN/ZEA past their radii). RA is
    /// normalised to [0, 360).
    /// </summary>
    public static (double Ra, double Dec)? Deproject(double xi, double eta, double crval1, double crval2, Projection projection)
    {
        if (projection == Projection.Linear) return null;

        var xiRad = xi * Math.PI / 180.0;
        var etaRad = eta * Math.PI / 180.0;
        var rho = Math.Sqrt(xiRad * xiRad + etaRad * etaRad);
        var ra0 = crval1 * Math.PI / 180.0;
        var dec0 = crval2 * Math.PI / 180.0;

        // At the reference pixel, all projections collapse to the centre.
        if (rho < 1e-12) return (crval1, crval2);

        double cosPsi, sinPsi;
        switch (projection)
        {
            case Projection.Tan:
            {
                var denom = Math.Sqrt(1 + rho * rho);
                cosPsi = 1 / denom;
                sinPsi = rho / denom;
                break;
            }
            case Projection.Sin:
                if (rho > 1.0) return null; // visible hemisphere only
                sinPsi = rho;
                cosPsi = Math.Sqrt(Math.Max(0, 1 - rho * rho));
                break;
            case Projection.Stg:
            {
                var halfPsi = Math.Atan(rho / 2);
                cosPsi = Math.Cos(2 * halfPsi);
                sinPsi = Math.Sin(2 * halfPsi);
                break;
            }
            case Projection.Zea:
            {
                if (rho > 2.0) return null;
                var halfPsi = Math.Asin(rho / 2);
                cosPsi = Math.Cos(2 * halfPsi);
                sinPsi = Math.Sin(2 * halfPsi);
                break;
            }
            default:
                return null;
        }

        // Position angle B: sin(B) = ξ/ρ, cos(B) = η/ρ (from celestial north, east-positive).
        var sinB = xiRad / rho;
        var cosB = etaRad / rho;

        var sinDec = cosPsi * Math.Sin(dec0) + sinPsi * cosB * Math.Cos(dec0);
        var decRad = Math.Asin(Math.Min(1, Math.Max(-1, sinDec)));

        var yArg = sinPsi * sinB;
        var xArg = cosPsi * Math.Cos(dec0) - sinPsi * cosB * Math.Sin(dec0);
        var raRad = ra0 + Math.Atan2(yArg, xArg);

        var raDeg = raRad * (180.0 / Math.PI);
        raDeg %= 360.0;
        if (raDeg < 0) raDeg += 360.0;
        return (raDeg, decRad * (180.0 / Math.PI));
    }

    #endregion

    #region Formatting

    /// <summary>Format RA in degrees to sexagesimal (HHhMMmSS.ss s).</summary>
    public static string FormatRa(double raDeg)
    {
        var ra = raDeg / 15.0; // degrees to hours
        if (ra < 0) ra += 24;
        var h = (int)ra;
        var m = (int)((ra - h) * 60);
        var s = (ra - h - m / 60.0) * 3600;
        return $"{h:D2}h{m:D2}m{s:00.00}s";
    }

    /// <summary>Format Dec in degrees to sexagesimal (+DD°MM'SS.s").</summary>
    public static string FormatDec(double decDeg)
    {
        var sign = decDeg >= 0 ? "+" : "-";
        var dec = Math.Abs(decDeg);
        var d = (int)dec;
        var m = (int)((dec - d) * 60);
        var s = (dec - d - m / 60.0) * 3600;
        return $"{sign}{d:D2}°{m:D2}'{s:00.0}\"";
    }

    /// <summary>
    /// Format as CADC resolver-compatible coordinate string.
    /// Format: "HH:MM:SS.ss,+DD:MM:SS.s" (no spaces, with decimal points).
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

    #endregion

    /// <summary>Extract WCS from a parsed FITS header.</summary>
    public static WcsInfo FromHeader(FitsHeader header)
    {
        var baseWcs = new WcsInfo
        {
            CrPix1 = header.GetDouble("CRPIX1"),
            CrPix2 = header.GetDouble("CRPIX2"),
            CrVal1 = header.GetDouble("CRVAL1"),
            CrVal2 = header.GetDouble("CRVAL2"),
            CType1 = header.GetString("CTYPE1") ?? "",
            CType2 = header.GetString("CTYPE2") ?? "",
        };

        WcsInfo wcs;
        if (header.Contains("CD1_1"))
        {
            wcs = baseWcs with
            {
                Cd1_1 = header.GetDouble("CD1_1"),
                Cd1_2 = header.GetDouble("CD1_2"),
                Cd2_1 = header.GetDouble("CD2_1"),
                Cd2_2 = header.GetDouble("CD2_2"),
            };
        }
        else
        {
            // Fall back to CDELT + CROTA2.
            var cdelt1 = header.GetDouble("CDELT1");
            var cdelt2 = header.GetDouble("CDELT2");
            var crota2 = header.GetDouble("CROTA2") * Math.PI / 180.0;
            wcs = baseWcs with
            {
                Cd1_1 = cdelt1 * Math.Cos(crota2),
                Cd1_2 = -cdelt2 * Math.Sin(crota2),
                Cd2_1 = cdelt1 * Math.Sin(crota2),
                Cd2_2 = cdelt2 * Math.Cos(crota2),
            };
        }

        // SIP polynomial distortion (CTYPE "…-SIP"): read the A/B forward and AP/BP inverse
        // coefficient sets. Without these a wide TAN field (TESS FFI) is off by many arcminutes.
        if (wcs.CType1.Contains("SIP", StringComparison.OrdinalIgnoreCase))
        {
            wcs = wcs with
            {
                SipA = ReadSipCoefficients(header, "A"),
                SipB = ReadSipCoefficients(header, "B"),
                SipAp = ReadSipCoefficients(header, "AP"),
                SipBp = ReadSipCoefficients(header, "BP"),
            };
        }

        // Degenerate/half-zero CD (e.g. CDELT1=0 with CDELT2≠0) is non-invertible for WCS;
        // try an approximate reconstruction from legacy RA/DEC keywords.
        if (wcs.Cd1_1 == 0 || wcs.Cd2_2 == 0)
        {
            var legacy = FromLegacyHeader(header);
            if (legacy is not null) return legacy;
        }
        return wcs;
    }

    /// <summary>Read a SIP coefficient set (&lt;prefix&gt;_ORDER + &lt;prefix&gt;_p_q) into a
    /// [p, q] array, or null when absent.</summary>
    private static double[,]? ReadSipCoefficients(FitsHeader header, string prefix)
    {
        if (!header.Contains(prefix + "_ORDER")) return null;
        var order = (int)header.GetDouble(prefix + "_ORDER");
        if (order < 1 || order > 9) return null; // sane cap (FITS SIP orders are small)

        var c = new double[order + 1, order + 1];
        var any = false;
        for (var p = 0; p <= order; p++)
            for (var q = 0; q <= order - p; q++)
            {
                var key = $"{prefix}_{p}_{q}";
                if (header.Contains(key))
                {
                    c[p, q] = header.GetDouble(key);
                    any = true;
                }
            }
        return any ? c : null;
    }

    /// <summary>
    /// Construct an approximate WCS from legacy RA/DEC keywords (sexagesimal strings) when
    /// standard WCS keywords (CRVAL/CD/CDELT) are absent. Assumes RA/DEC points to the image
    /// centre with a SECPIX/PIXSCALE/SCALE plate scale (default 0.5"/px).
    /// </summary>
    private static WcsInfo? FromLegacyHeader(FitsHeader header)
    {
        var ra = Sexagesimal.ParseRa(header.GetString("RA"));
        var dec = Sexagesimal.ParseDec(header.GetString("DEC"));
        if (ra is null || dec is null) return null;

        var naxis1 = header.NAxis1;
        var naxis2 = header.NAxis2;
        if (naxis1 <= 0 || naxis2 <= 0) return null;

        var pixelScale = header.GetDouble("SECPIX");
        if (pixelScale == 0) pixelScale = header.GetDouble("PIXSCALE");
        if (pixelScale == 0) pixelScale = header.GetDouble("SCALE");
        if (pixelScale == 0) pixelScale = 0.5; // conservative default

        var cdelt = pixelScale / 3600.0; // arcsec → degrees
        return new WcsInfo
        {
            CrPix1 = naxis1 / 2.0, // image centre
            CrPix2 = naxis2 / 2.0,
            CrVal1 = ra.Value,
            CrVal2 = dec.Value,
            Cd1_1 = -cdelt, // RA increases to the left (standard)
            Cd1_2 = 0,
            Cd2_1 = 0,
            Cd2_2 = cdelt,
            CType1 = "RA---TAN",
            CType2 = "DEC--TAN",
            IsApproximate = true,
        };
    }
}
