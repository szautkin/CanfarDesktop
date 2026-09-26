using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>What is wrong with a cutout — errors stop it, warnings only say what will happen.</summary>
public sealed record CutoutCheck(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;

    /// <summary>This check with more errors and warnings after its own — a way of cutting adding what only it knows.</summary>
    public CutoutCheck With(IEnumerable<string> errors, IEnumerable<string> warnings)
        => new([.. Errors, .. errors], [.. Warnings, .. warnings]);
}

/// <summary>
/// The rules every cutout is held to, whoever cuts it: there is something to cut; the region is a
/// region; the file can be cut to that shape, by that band, that time, that polarization; and the region
/// and band fall on the file.
///
/// <para>The one place these are judged. They were SODA's, and a local cut would have needed the same
/// ones again; a way of cutting now adds only what is its own (<see cref="ICutoutSource.Check"/>), so
/// the editor, an agent's request and the cut itself cannot disagree about the rest.</para>
///
/// <para>Takes its translations through <see cref="Translate"/> rather than calling <c>Loc</c>, as
/// <c>ActivitySummary</c> does: the resource loader needs a packaged app, and this is tested without
/// one. Unset, it answers in English — which is also what an agent is told.</para>
/// </summary>
public static class CutoutRules
{
    /// <summary>Resolves a resource key, or null to fall back to the English written here.</summary>
    public static Func<string, string?>? Translate { get; set; }

    /// <summary>A message by its resource key, in the app's language when there is one.</summary>
    public static string T(string key, string english) => Translate?.Invoke(key) ?? english;

    public static CutoutCheck Check(ICutoutFile file, CutoutSpec spec)
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

    private static void CheckRegion(ICutoutFile file, SkyRegion region, List<string> errors, List<string> warnings)
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
}
