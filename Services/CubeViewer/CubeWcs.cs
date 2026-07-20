using System.Globalization;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// World Coordinate System for a 3D spectral cube: the two spatial axes (reusing
/// the rigorous <see cref="WcsInfo"/> projection machinery) plus the spectral
/// third axis (FREQ / VELO / WAVE / FDEP …). Provides the formatted axis names
/// and endpoint values the 3D box captions display — the Windows analogue of the
/// macOS <c>CubeWCS</c>.
/// </summary>
public sealed class CubeWcs
{
    /// <summary>Original full-resolution cube dimensions (NAXIS1/2/3), for pixel→world mapping.</summary>
    public int Nx { get; init; } = 1;
    public int Ny { get; init; } = 1;
    public int Nz { get; init; } = 1;

    /// <summary>Spatial (RA/Dec or GLON/GLAT) WCS for the first two axes.</summary>
    public WcsInfo Spatial { get; init; } = new();

    /// <summary>True when the spatial frame is galactic (CTYPE1 = GLON-…).</summary>
    public bool Galactic { get; init; }

    // ── Spectral (third) axis ──
    public string SpecCType { get; init; } = "";
    public string SpecUnit { get; init; } = "";
    public double SpecCrpix { get; init; }
    public double SpecCrval { get; init; }
    public double SpecCdelt { get; init; }

    // ── Spectral conventions (mandatory metadata for line kinematics; surfaced, not converted) ──
    /// <summary>Rest frequency in Hz (RESTFRQ/RESTFREQ) for frequency↔velocity conversion; null if absent.</summary>
    public double? RestFrequencyHz { get; init; }
    /// <summary>Spectral reference frame (SPECSYS — LSRK/BARYCENT/TOPOCENT/…); without it a velocity axis is unusable.</summary>
    public string SpectralFrame { get; init; } = "";
    /// <summary>Observer frame (SSYSOBS), when stated.</summary>
    public string ObserverFrame { get; init; } = "";
    /// <summary>Synthesized beam (degrees): major/minor axis + position angle (BMAJ/BMIN/BPA), for flux integration.</summary>
    public double? BeamMajorDeg { get; init; }
    public double? BeamMinorDeg { get; init; }
    public double? BeamPaDeg { get; init; }

    /// <summary>Rest frequency in GHz, or null.</summary>
    public double? RestFrequencyGHz => RestFrequencyHz.HasValue ? RestFrequencyHz.Value / 1e9 : null;

    public bool HasSpatial => Spatial.IsValid;
    public bool HasSpectral => SpecCdelt != 0 && Nz > 1;

    /// <summary>Longitude axis name: "GLON" (galactic) or "RA" (equatorial).</summary>
    public string LonName => Galactic ? "GLON" : "RA";

    /// <summary>Latitude axis name: "GLAT" (galactic) or "DEC" (equatorial).</summary>
    public string LatName => Galactic ? "GLAT" : "DEC";

    /// <summary>Build the cube WCS from a parsed FITS header and the cube dimensions.</summary>
    public static CubeWcs FromHeader(FitsHeader h, int nx, int ny, int nz)
    {
        var ctype1 = (h.GetString("CTYPE1") ?? "").Trim();
        var galactic = ctype1.StartsWith("GLON", StringComparison.OrdinalIgnoreCase);

        // CDELT3 is the common spectral increment; some cubes use CD3_3 instead.
        var cdelt3 = h.GetDouble("CDELT3");
        if (cdelt3 == 0 && h.Contains("CD3_3")) cdelt3 = h.GetDouble("CD3_3");

        return new CubeWcs
        {
            Nx = nx,
            Ny = ny,
            Nz = nz,
            Spatial = WcsInfo.FromHeader(h),
            Galactic = galactic,
            SpecCType = (h.GetString("CTYPE3") ?? "").Trim(),
            SpecUnit = (h.GetString("CUNIT3") ?? "").Trim(),
            SpecCrpix = h.GetDouble("CRPIX3", 1.0),
            SpecCrval = h.GetDouble("CRVAL3"),
            SpecCdelt = cdelt3,
            RestFrequencyHz = ReadOptional(h, "RESTFRQ") ?? ReadOptional(h, "RESTFREQ"),
            SpectralFrame = (h.GetString("SPECSYS") ?? "").Trim(),
            ObserverFrame = (h.GetString("SSYSOBS") ?? "").Trim(),
            BeamMajorDeg = ReadOptional(h, "BMAJ"),
            BeamMinorDeg = ReadOptional(h, "BMIN"),
            BeamPaDeg = ReadOptional(h, "BPA"),
        };
    }

    /// <summary>A header keyword as a double, or null when the keyword is absent.</summary>
    private static double? ReadOptional(FitsHeader h, string key) => h.Contains(key) ? h.GetDouble(key) : null;

    // ── Spatial endpoint formatting (1-based FITS pixel coords) ──

    /// <summary>Formatted longitude value at the given 0-based X pixel (evaluated at the cube's mid Y).</summary>
    public string LonText(int pixX0)
    {
        if (!HasSpatial) return $"px {pixX0}";
        var (lon, lat) = Spatial.PixelToWorld(pixX0 + 1, Ny / 2.0);
        if (Galactic)
        {
            // Galactic/CAR longitude from the linear WCS path is not wrapped; fold into [0,360).
            lon = ((lon % 360.0) + 360.0) % 360.0;
            return FormatDeg(lon);
        }
        return FormatRaShort(lon); // FormatRaShort already wraps RA into [0,24h)
    }

    /// <summary>Formatted latitude value at the given 0-based Y pixel (evaluated at the cube's mid X).</summary>
    public string LatText(int pixY0)
    {
        if (!HasSpatial) return $"px {pixY0}";
        var (ra, dec) = Spatial.PixelToWorld(Nx / 2.0, pixY0 + 1);
        return Galactic ? FormatDeg(dec) : FormatDecShort(dec);
    }

    /// <summary>
    /// Formatted sky position at an exact 0-based spatial pixel (x, y) — the slice hover readout.
    /// Null when the cube has no valid spatial WCS (the caller falls back to pixel coordinates).
    /// </summary>
    public (string Lon, string Lat)? SkyTextAt(double pixX0, double pixY0)
    {
        if (!HasSpatial) return null;
        var (lon, lat) = Spatial.PixelToWorld(pixX0 + 1, pixY0 + 1);
        if (Galactic)
        {
            // Galactic/CAR longitude from the linear WCS path is not wrapped; fold into [0,360).
            lon = ((lon % 360.0) + 360.0) % 360.0;
            return (FormatDeg(lon), FormatDeg(lat));
        }
        return (FormatRaShort(lon), FormatDecShort(lat));
    }

    // ── Spectral axis ──

    /// <summary>Human axis name for the spectral axis ("FREQUENCY", "VELOCITY", …).</summary>
    public string SpecAxisName()
    {
        if (!HasSpectral) return "CHANNEL";
        return SpecBase() switch
        {
            "FREQ" => "FREQUENCY",
            "VRAD" or "VELO" or "VOPT" => "VELOCITY",
            "WAVE" or "AWAV" => "WAVELENGTH",
            "WAVN" => "WAVENUMBER",
            "FDEP" => "FARADAY DEPTH",
            _ => string.IsNullOrEmpty(SpecCType) ? "SPECTRAL" : SpecCType.ToUpperInvariant(),
        };
    }

    /// <summary>Display unit for the spectral axis after any convenience conversion (GHz, km/s, µm).</summary>
    public string SpecUnitDisplay()
    {
        if (!HasSpectral) return "";
        return SpecBase() switch
        {
            "FREQ" => "GHz",
            "VRAD" or "VELO" or "VOPT" => "km/s",
            "WAVE" or "AWAV" => "µm",
            "WAVN" => string.IsNullOrEmpty(SpecUnit) ? "cm⁻¹" : SpecUnit,
            "FDEP" => string.IsNullOrEmpty(SpecUnit) ? "rad/m²" : SpecUnit,
            _ => SpecUnit,
        };
    }

    /// <summary>Formatted spectral value at a 0-based channel (converted to the display unit).</summary>
    public string SpecText(int channel0)
    {
        if (!HasSpectral) return $"CH {channel0}";
        return ConvertSpectral(SpectralValue(channel0)).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Convert a raw spectral world value to the display unit, honoring CUNIT3 so we never
    /// double-convert (a cube already stored in km/s or GHz must not be divided again).
    /// </summary>
    private double ConvertSpectral(double v)
    {
        string u = SpecUnit.Trim().ToLowerInvariant();
        switch (SpecBase())
        {
            case "FREQ":
                if (u is "ghz") return v;
                if (u is "mhz") return v / 1e3;
                if (u is "khz") return v / 1e6;
                return v / 1e9;                         // Hz (default) → GHz
            case "VRAD" or "VELO" or "VOPT":
                return u.StartsWith("km") ? v : v / 1e3; // m/s → km/s
            case "WAVE" or "AWAV":
                if (u is "um" or "µm" or "micron" or "microns") return v;
                if (u is "nm") return v / 1e3;          // nm → µm
                if (u is "angstrom" or "a" or "ang") return v / 1e4; // Å → µm
                return v * 1e6;                         // m (default) → µm
            default:
                return v;                               // WAVN / FDEP / unknown: raw
        }
    }

    /// <summary>Raw spectral world value at a 0-based channel (CRVAL3 + (chan+1 − CRPIX3)·CDELT3).</summary>
    public double SpectralValue(int channel0) =>
        SpecCrval + ((channel0 + 1) - SpecCrpix) * SpecCdelt;

    private string SpecBase()
    {
        var t = SpecCType;
        int dash = t.IndexOf('-');
        if (dash > 0) t = t[..dash];
        return t.ToUpperInvariant();
    }

    // ── Compact sexagesimal / decimal formatters for captions ──

    private static string FormatRaShort(double raDeg)
    {
        var ra = raDeg / 15.0;
        ra %= 24.0; if (ra < 0) ra += 24.0;
        int h = (int)ra;
        int m = (int)((ra - h) * 60);
        int s = (int)Math.Round((ra - h - m / 60.0) * 3600);
        if (s == 60) { s = 0; m++; } if (m == 60) { m = 0; h = (h + 1) % 24; }
        return $"{h:00}:{m:00}:{s:00}";
    }

    private static string FormatDecShort(double decDeg)
    {
        var sign = decDeg >= 0 ? "+" : "−";
        var d = Math.Abs(decDeg);
        int dd = (int)d;
        int m = (int)((d - dd) * 60);
        int s = (int)Math.Round((d - dd - m / 60.0) * 3600);
        if (s == 60) { s = 0; m++; } if (m == 60) { m = 0; dd++; }
        return $"{sign}{dd:00}:{m:00}:{s:00}";
    }

    private static string FormatDeg(double deg) =>
        deg.ToString("0.000", CultureInfo.InvariantCulture) + "°";
}

/// <summary>
/// Display metadata for a loaded cube: object/instrument labels, dimensions (full
/// and rendered), physical value statistics, NaN fraction, and the cube WCS.
/// Populated by <see cref="FitsCubeReader"/> and surfaced in the info panel + export
/// plate (mirrors the macOS <c>CubeFigureMetadata</c>).
/// </summary>
public sealed class CubeMetadata
{
    public string Object { get; init; } = "";
    public string Instrument { get; init; } = "";
    public string Bunit { get; init; } = "";

    /// <summary>Original full-resolution NAXIS1/2/3.</summary>
    public int Nx { get; init; }
    public int Ny { get; init; }
    public int Nz { get; init; }

    /// <summary>Rendered (down-sampled) dimensions actually uploaded to the GPU.</summary>
    public int RenderNx { get; init; }
    public int RenderNy { get; init; }
    public int RenderNz { get; init; }

    /// <summary>Down-sample stride: rendered voxel i on any axis is native sample i·Stride (1 = full res).</summary>
    public int Stride { get; init; } = 1;

    /// <summary>The native (NAXIS3) channel a rendered channel corresponds to — required wherever a
    /// slider/volume channel index meets the native-resolution spectral WCS (CRPIX3/CDELT3).</summary>
    public int NativeChannel(int renderChannel) => Math.Min(renderChannel * Stride, Math.Max(0, Nz - 1));

    public double DataMin { get; init; }
    public double DataMax { get; init; }
    public double Median { get; init; }

    /// <summary>Physical values the display normalization maps to 0 and 1 (the p0.5…p99.5 cut).</summary>
    public double NormLo { get; init; }
    public double NormHi { get; init; }

    /// <summary>Fraction of voxels that were NaN/Inf (0..1).</summary>
    public double NanFraction { get; init; }

    public CubeWcs Wcs { get; init; } = new();

    /// <summary>Physical value at a normalized display position t∈[0,1] (for colorbar labels).</summary>
    public double ValueAtNormalized(double t) => NormLo + (NormHi - NormLo) * t;

    public string DimensionsText => $"{Nx} × {Ny} × {Nz}";

    public string RangeText => $"{F(DataMin)} … {F(DataMax)}{UnitSuffix}";

    /// <summary>Display-cut range (the p0.5…p99.5 normalization window) — the info panel's RANGE
    /// row, matching the macOS panel where the true extremes live on the MIN/MAX row.</summary>
    public string CutRangeText => $"{F(NormLo)} … {F(NormHi)}{UnitSuffix}";

    /// <summary>True full-cube extremes as "min / max" (unit is on the RANGE row already).</summary>
    public string MinMaxText => $"{F(DataMin)} / {F(DataMax)}";

    public string MedianText => F(Median);

    /// <summary>True when the GPU volume was strided below the native dimensions.</summary>
    public bool IsDownsampled => RenderNx != Nx || RenderNy != Ny || RenderNz != Nz;

    /// <summary>How the in-memory volume relates to the file (the macOS panel's Mode row).</summary>
    public string ModeText => IsDownsampled ? "Downsampled to GPU cap" : "Resident (full)";

    public string NanText => (NanFraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private string UnitSuffix => string.IsNullOrEmpty(Bunit) ? "" : " " + Bunit;

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
