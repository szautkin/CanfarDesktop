using System.Globalization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>What is wrong with a cutout — errors stop it, warnings only say what will happen.</summary>
public sealed record CutoutCheck(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// A cutout checked against its file's descriptor, and the SODA request made from it.
///
/// <para>The one place a cutout is judged. The editor shows these messages as they are typed, an
/// agent's download_cutout is refused with them before anything is queued, and the URL is only ever
/// built from a cutout that passed — so no two surfaces can disagree about what is allowed.</para>
///
/// <para>Takes its translations through <see cref="Translate"/> rather than calling <c>Loc</c>, as
/// <c>ActivitySummary</c> does: the resource loader needs a packaged app, and this is tested without
/// one. Unset, it answers in English — which is also what an agent is told.</para>
/// </summary>
public static class SodaRequest
{
    /// <summary>Resolves a resource key, or null to fall back to the English written here.</summary>
    public static Func<string, string?>? Translate { get; set; }

    private static string T(string key, string english) => Translate?.Invoke(key) ?? english;

    public static CutoutCheck Check(SodaDescriptor file, CutoutSpec spec)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (spec.IsEmpty)
            errors.Add(T("Cutout_CheckNothing", "Choose a region (or a band) to cut; with neither, the whole file would come back."));

        if (spec.Region is { } region)
            CheckRegion(file, region, errors, warnings);

        if (spec.BandMin is not null || spec.BandMax is not null)
            CheckInterval(file.Supports("BAND"), spec.BandMin, spec.BandMax, file.BandMin, file.BandMax,
                T("Cutout_CheckNoBand", "This file cannot be cut by wavelength."),
                T("Cutout_CheckBandOrder", "The shortest wavelength has to be below the longest."),
                T("Cutout_CheckBandOutside", "That wavelength range is outside this file's."),
                T("Cutout_CheckBandPartial", "Part of that wavelength range is outside this file's; the cutout will be trimmed to it."),
                errors, warnings);

        if (spec.TimeMin is not null || spec.TimeMax is not null)
            CheckInterval(file.Supports("TIME"), spec.TimeMin, spec.TimeMax, file.TimeMin, file.TimeMax,
                T("Cutout_CheckNoTime", "This file cannot be cut by time."),
                T("Cutout_CheckTimeOrder", "The start has to be before the end."),
                T("Cutout_CheckTimeOutside", "That time range is outside this file's."),
                T("Cutout_CheckTimePartial", "Part of that time range is outside this file's; the cutout will be trimmed to it."),
                errors, warnings);

        if (spec.Pol.Count > 0)
        {
            if (!file.Supports("POL"))
                errors.Add(T("Cutout_CheckNoPol", "This file cannot be cut by polarization."));
            else if (file.PolStates.Count > 0 && spec.Pol.FirstOrDefault(s => !file.PolStates.Contains(s)) is { } unknown)
                errors.Add(string.Format(T("Cutout_CheckPolUnknown", "This file has no {0} polarization; it has {1}."),
                    unknown, string.Join(", ", file.PolStates)));
        }

        return new CutoutCheck(errors, warnings);
    }

    /// <summary>
    /// The request for a cutout that passed <see cref="Check"/>: SODA's sync endpoint with the file's
    /// ID and one value for each parameter the cutout sets. Throws for one that did not, with the
    /// first reason — building a URL for a refused cutout is a bug, not a request.
    /// </summary>
    public static string Url(SodaDescriptor file, CutoutSpec spec)
    {
        var check = Check(file, spec);
        if (!check.IsValid) throw new InvalidOperationException(check.Errors[0]);

        var query = new List<string> { Param("ID", file.ArtifactId) };
        if (spec.Region is { } region) query.Add(Param(region.SodaParameter, region.ToSoda()));
        if (spec.BandMin is not null || spec.BandMax is not null)
            query.Add(Param("BAND", Range(spec.BandMin, spec.BandMax)));
        if (spec.TimeMin is not null || spec.TimeMax is not null)
            query.Add(Param("TIME", Range(spec.TimeMin, spec.TimeMax)));
        query.AddRange(spec.Pol.Select(state => Param("POL", state)));

        return file.AccessUrl + (file.AccessUrl.Contains('?') ? "&" : "?") + string.Join('&', query);
    }

    /// <summary>
    /// About how large the cutout will be: the whole file's size in proportion to the share of its
    /// footprint the cut covers, and of its band the band does. An estimate for a person deciding
    /// — "about 15 MB of 1.6 GB" — not a promise; null when there is nothing to go on.
    /// </summary>
    public static long? EstimateBytes(SodaDescriptor file, CutoutSpec spec, long? wholeFile)
    {
        if (wholeFile is not { } whole || whole <= 0) return null;

        var share = 1.0;
        if (spec.Region is { } region && file.Footprint is { } footprint)
        {
            var fp = SkyGeometry.Area(footprint.Outline());
            if (fp > 0) share *= Math.Min(1, BoundingArea(region) / fp);
        }
        if (file.BandMin is { } lo && file.BandMax is { } hi && hi > lo)
        {
            var from = Math.Max(lo, spec.BandMin ?? lo);
            var to = Math.Min(hi, spec.BandMax ?? hi);
            share *= Math.Clamp((to - from) / (hi - lo), 0, 1);
        }
        return (long)(whole * share);
    }

    /// <summary>
    /// The area SODA actually returns for a region: the pixel box around it, not the region itself — a
    /// circle comes back as its square. Measured on a MegaPipe tile, a 0.05° circle was 1938 × 1937
    /// pixels, (2r)², where the circle's own area would have estimated a quarter less.
    /// </summary>
    private static double BoundingArea(SkyRegion region)
    {
        var centre = region.Centre;
        var plane = region.Outline().Select(v => SkyGeometry.Project(centre, v)).OfType<(double X, double Y)>().ToList();
        return plane.Count == 0 ? 0
            : (plane.Max(p => p.X) - plane.Min(p => p.X)) * (plane.Max(p => p.Y) - plane.Min(p => p.Y));
    }

    /// <summary>The words for a region's own problem.</summary>
    public static string Describe(RegionProblem problem) => problem switch
    {
        RegionProblem.NotANumber => T("Cutout_ProblemNumber", "The position and size have to be numbers."),
        RegionProblem.DecOutOfRange => T("Cutout_ProblemDec", "Dec has to be between −90° and +90°."),
        RegionProblem.SizeNotPositive => T("Cutout_ProblemSize", "The size has to be greater than zero."),
        RegionProblem.TooLarge => T("Cutout_ProblemTooLarge", "That region is larger than any cutout can be."),
        RegionProblem.TooFewVertices => T("Cutout_ProblemVertices", "A polygon needs at least three corners."),
        _ => T("Cutout_ProblemDegenerate", "Those corners enclose no area."),
    };

    private static void CheckRegion(SodaDescriptor file, SkyRegion region, List<string> errors, List<string> warnings)
    {
        if (region.Problem() is { } problem)
        {
            errors.Add(Describe(problem));
            return;
        }

        if (!file.Supports(region.SodaParameter))
        {
            errors.Add(T("Cutout_CheckNoRegion", "This file cannot be cut to that shape."));
            return;
        }

        if (file.Footprint is not { } footprint) return;
        switch (SkyGeometry.Overlap(region.Outline(), footprint.Outline()))
        {
            case SkyOverlap.Outside:
                errors.Add(T("Cutout_CheckOutside", "That region is outside this file's footprint."));
                break;
            case SkyOverlap.Partial:
                warnings.Add(T("Cutout_CheckPartial", "Part of that region is outside the footprint; the cutout will be trimmed to it."));
                break;
        }
    }

    private static void CheckInterval(bool supported, double? min, double? max, double? fileMin, double? fileMax,
        string unsupported, string order, string outside, string partial, List<string> errors, List<string> warnings)
    {
        if (!supported) { errors.Add(unsupported); return; }
        if (min is { } a && max is { } b && a >= b) { errors.Add(order); return; }

        var lo = min ?? fileMin ?? double.NegativeInfinity;
        var hi = max ?? fileMax ?? double.PositiveInfinity;
        var fLo = fileMin ?? double.NegativeInfinity;
        var fHi = fileMax ?? double.PositiveInfinity;
        if (hi <= fLo || lo >= fHi) errors.Add(outside);
        else if (lo < fLo || hi > fHi) warnings.Add(partial);
    }

    /// <summary>An open end is written as infinity, as SODA's interval syntax has it.</summary>
    private static string Range(double? min, double? max)
        => $"{Num(min, double.NegativeInfinity)} {Num(max, double.PositiveInfinity)}";

    private static string Num(double? v, double open)
    {
        var value = v ?? open;
        if (double.IsNegativeInfinity(value)) return "-Inf";
        if (double.IsPositiveInfinity(value)) return "+Inf";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Param(string name, string value) => $"{name}={Uri.EscapeDataString(value)}";
}
