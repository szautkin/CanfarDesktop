using System.Globalization;

namespace CanfarDesktop.Models.Fits;

/// <summary>
/// A cube's spectral axis, as its header describes it (FITS WCS Paper III), and what it means in the
/// one unit a cutout's band is given in: wavelength, metres — so "866 to 868 µm" can be found among the
/// planes of a frequency cube, a velocity cube or a wavelength cube alike.
///
/// <para>Linear axes and logarithmic ones (<c>-LOG</c>); an axis sampled any other way (<c>-F2W</c>,
/// <c>-TAB</c>, a grism) is not taken for one, rather than taken for a linear one it is not. A velocity or
/// redshift axis needs the rest frequency or wavelength the header gives for it.</para>
/// </summary>
public sealed record SpectralAxis
{
    private const double SpeedOfLight = 299_792_458.0;   // m/s
    private const double Planck = 6.62607015e-34;       // J·s
    private const double ElectronVolt = 1.602176634e-19; // J

    /// <summary>What a spectral CTYPE begins with (Paper III), and the AIPS forms still in use ("VELO-LSR", "VELOCITY").</summary>
    public static readonly IReadOnlyList<string> Codes =
        ["FREQ", "ENER", "WAVN", "VRAD", "WAVE", "VOPT", "ZOPT", "AWAV", "VELO", "BETA", "FELO", "VELOCITY"];

    /// <summary>Algorithm codes for axes sampled neither linearly nor logarithmically — not read as either.</summary>
    private static readonly HashSet<string> NonLinear =
        ["F2W", "F2V", "F2A", "W2F", "W2V", "W2A", "V2F", "V2W", "V2A", "A2F", "A2W", "A2V", "TAB", "GRI", "GRA"];

    /// <summary>The FITS axis it is (3 for most cubes), 1-based.</summary>
    public int Axis { get; init; }

    /// <summary>How many planes there are along it.</summary>
    public int Length { get; init; }

    /// <summary>"FREQ", "WAVE", "VRAD" …</summary>
    public string Type { get; init; } = string.Empty;

    public double CrPix { get; init; }
    public double CrVal { get; init; }
    public double CDelt { get; init; }
    public bool Logarithmic { get; init; }

    /// <summary>The axis unit in SI (Hz, m, J, m⁻¹, m/s, or 1 for a redshift).</summary>
    public double UnitToSi { get; init; } = 1;

    /// <summary>The rest wavelength, metres, when the header gives one (or a rest frequency).</summary>
    public double? RestWavelength { get; init; }

    /// <summary>Whether a CTYPE names a spectral axis.</summary>
    public static bool IsSpectralType(string? ctype)
        => !string.IsNullOrWhiteSpace(ctype) && Codes.Any(c => ctype.Trim().StartsWith(c, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The spectral axis of an image with more than two axes, when it has one this can turn into
    /// wavelength — or null.
    /// </summary>
    public static SpectralAxis? Find(FitsHeader header)
    {
        for (var n = 3; n <= header.NAxis; n++)
        {
            var ctype = (header.GetString($"CTYPE{n}") ?? "").Trim().ToUpperInvariant();
            if (!IsSpectralType(ctype)) continue;

            var parts = ctype.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var type = parts[0] == "VELOCITY" ? "VELO" : parts[0];
            var algorithm = parts.Length > 1 ? parts[1] : "";
            if (NonLinear.Contains(algorithm)) return null;

            var cdelt = header.Contains($"CD{n}_{n}")
                ? header.GetDouble($"CD{n}_{n}")
                : header.GetDouble($"CDELT{n}") * header.GetDouble($"PC{n}_{n}", 1.0);
            if (cdelt == 0 || !double.IsFinite(cdelt)) return null;
            if (UnitScale(type, (header.GetString($"CUNIT{n}") ?? "").Trim()) is not { } scale) return null;

            var axis = new SpectralAxis
            {
                Axis = n,
                Length = header.GetInt($"NAXIS{n}"),
                Type = type,
                CrPix = header.GetDouble($"CRPIX{n}", 1.0),
                CrVal = header.GetDouble($"CRVAL{n}"),
                CDelt = cdelt,
                Logarithmic = algorithm == "LOG",
                UnitToSi = scale,
                RestWavelength = RestOf(header),
            };
            return axis.Length > 0 && axis.WavelengthAt(1) is not null ? axis : null;
        }
        return null;
    }

    /// <summary>The wavelength, metres, at a pixel along the axis (FITS's 1-based numbering); null where it has none.</summary>
    public double? WavelengthAt(double pixel)
    {
        var world = Logarithmic
            ? CrVal * Math.Exp(CDelt * (pixel - CrPix) / CrVal)
            : CrVal + CDelt * (pixel - CrPix);
        var w = world * UnitToSi;
        double? metres = Type switch
        {
            "WAVE" or "AWAV" => w,
            "FREQ" => w > 0 ? SpeedOfLight / w : null,
            "ENER" => w > 0 ? Planck * SpeedOfLight / w : null,
            "WAVN" => w > 0 ? 1 / w : null,
            "VRAD" => RestWavelength is { } l0 && w < SpeedOfLight ? l0 / (1 - w / SpeedOfLight) : null,
            "VOPT" or "FELO" => RestWavelength * (1 + w / SpeedOfLight),
            "ZOPT" => RestWavelength * (1 + w),
            "VELO" => Relativistic(w / SpeedOfLight),
            "BETA" => Relativistic(w),
            _ => null,
        };
        return metres is { } m && double.IsFinite(m) && m > 0 ? m : null;
    }

    /// <summary>The wavelengths the cube covers, edge to edge, metres.</summary>
    public (double Min, double Max)? Range
    {
        get
        {
            if (WavelengthAt(0.5) is not { } a || WavelengthAt(Length + 0.5) is not { } b) return null;
            return (Math.Min(a, b), Math.Max(a, b));
        }
    }

    /// <summary>
    /// The planes a band reaches — every plane whose own width it reaches into, so a band narrower than
    /// one channel still has the channel it falls in, while one that only touches a channel's edge does
    /// not take that channel too — as the first (0-based) and how many. Null when it reaches none. An open
    /// end is the cube's own.
    /// </summary>
    public (long Start, long Count)? PlanesWithin(double? min, double? max)
    {
        long first = -1, last = -1;
        for (var k = 1; k <= Length; k++)
        {
            if (WavelengthAt(k - 0.5) is not { } a || WavelengthAt(k + 0.5) is not { } b) continue;
            var (lo, hi) = (Math.Min(a, b), Math.Max(a, b));
            var into = 1e-6 * (hi - lo); // a millionth of a channel: an edge met in rounding is not a reach
            if ((min is { } m0 && hi - m0 <= into) || (max is { } m1 && m1 - lo <= into)) continue;
            if (first < 0) first = k;
            last = k;
        }
        return first < 0 ? null : (first - 1, last - first + 1);
    }

    private double? Relativistic(double beta)
        => RestWavelength is { } l0 && Math.Abs(beta) < 1 ? l0 * Math.Sqrt((1 + beta) / (1 - beta)) : null;

    /// <summary>RESTWAV (or the older RESTWAVE), else c over RESTFRQ (or RESTFREQ): the line the velocities are of.</summary>
    private static double? RestOf(FitsHeader header)
    {
        foreach (var key in new[] { "RESTWAV", "RESTWAVE" })
            if (header.Contains(key) && header.GetDouble(key) > 0) return header.GetDouble(key);
        foreach (var key in new[] { "RESTFRQ", "RESTFREQ" })
            if (header.Contains(key) && header.GetDouble(key) > 0) return SpeedOfLight / header.GetDouble(key);
        return null;
    }

    /// <summary>What one unit of the axis is in SI — for the axis's own kind of quantity; null for a unit this does not know.</summary>
    private static double? UnitScale(string type, string unit)
    {
        var u = unit.Replace(" ", "").Replace("µ", "u").ToLowerInvariant();
        return type switch
        {
            "WAVE" or "AWAV" => u switch
            {
                "" or "m" => 1, "cm" => 1e-2, "mm" => 1e-3, "um" or "micron" or "microns" => 1e-6,
                "nm" => 1e-9, "angstrom" or "a" or "å" => 1e-10, _ => null,
            },
            "FREQ" => u switch { "" or "hz" => 1, "khz" => 1e3, "mhz" => 1e6, "ghz" => 1e9, "thz" => 1e12, _ => null },
            "ENER" => u switch { "" or "j" => 1, "ev" => ElectronVolt, "kev" => 1e3 * ElectronVolt, "mev" => 1e6 * ElectronVolt, _ => null },
            "WAVN" => u switch { "" or "m-1" or "1/m" or "/m" => 1, "cm-1" or "1/cm" or "/cm" => 100, _ => null },
            "VRAD" or "VOPT" or "FELO" or "VELO" => u switch
            {
                "" or "m/s" or "ms-1" or "m.s-1" => 1, "km/s" or "kms-1" or "km.s-1" => 1e3, _ => null,
            },
            "ZOPT" or "BETA" => u.Length == 0 ? 1 : null,
            _ => null,
        };
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"axis {Axis}: {Type}, {Length} planes");
}
