using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// What a search already knows that a cutout can start from: where it looked, how far out, and the
/// wavelengths it asked for — all in SODA's units (degrees, metres).
/// </summary>
public sealed record CutoutHints(
    double? Ra = null,
    double? Dec = null,
    double? RadiusDeg = null,
    double? BandMin = null,
    double? BandMax = null)
{
    /// <summary>
    /// What a search's form says — read through the query builder itself, so a cutout starts from
    /// exactly what the search that found the observation searched: its circle, its wavelengths.
    /// </summary>
    public static CutoutHints? From(Models.SearchFormState? search)
    {
        if (search is null) return null;
        var circle = ADQLBuilder.SpatialCircle(search);
        var band = ADQLBuilder.SpectralInterval(search);
        if (circle is null && band is null) return null;
        return new CutoutHints(circle?.Ra, circle?.Dec, circle?.Radius, band?.Min, band?.Max);
    }
}

/// <summary>
/// The cutout an editor opens on — so the person adjusts a sensible one instead of starting from
/// zeros.
///
/// <para>The search comes first: its target, at its radius, when the target falls on this file; its
/// wavelength range when the file can be cut by wavelength. Otherwise a circle at the middle of the
/// footprint, a quarter of the file across — small enough to be a cutout, large enough to see where it
/// is.</para>
///
/// <para>Always within the file: a radius past the file's own bounding circle only asks SODA for
/// nothing more, so it is held to it.</para>
/// </summary>
public static class CutoutPrefill
{
    /// <summary>Never smaller than this, so a pinpoint search radius still makes a cutout worth having.</summary>
    public const double MinRadius = 5.0 / 3600;

    public static CutoutSpec Suggest(ICutoutFile file, CutoutHints? hints = null)
    {
        var region = SuggestRegion(file, hints);
        var (bandMin, bandMax) = SuggestBand(file, hints);
        return new CutoutSpec { ArtifactId = file.ArtifactId, Region = region, BandMin = bandMin, BandMax = bandMax };
    }

    /// <summary>
    /// The cutout a search's "Spatial cutout" and "Spectral cutout" boxes ask for, for one of its
    /// results — as CADC's own search page means them: the search's circle, the search's wavelengths.
    /// Null when they ask for nothing this file can give (neither ticked, the circle not on it, no
    /// band on it): then the whole file is what was asked for.
    /// </summary>
    public static CutoutSpec? FromSearchFlags(ICutoutFile file, CutoutHints? hints, bool spatial, bool spectral)
    {
        SkyRegion? region = null;
        if (spatial && file.SupportsSky() && file.Footprint is { } footprint
            && hints is { Ra: { } ra, Dec: { } dec, RadiusDeg: { } r } && r > 0
            && SkyGeometry.Overlap(SkyRegion.Circle(ra, dec, r).Outline(), footprint.Outline()) != SkyOverlap.Outside)
            region = Circle(file, ra, dec, Math.Max(r, MinRadius));

        var (bandMin, bandMax) = spectral ? SuggestBand(file, hints) : (null, null);
        var spec = new CutoutSpec { ArtifactId = file.ArtifactId, Region = region, BandMin = bandMin, BandMax = bandMax };
        return spec.IsEmpty ? null : spec;
    }

    private static SkyRegion? SuggestRegion(ICutoutFile file, CutoutHints? hints)
    {
        if (!file.SupportsSky() || file.Footprint is not { } footprint) return null;

        var outline = footprint.Outline();
        var reach = file.BoundingCircle?.Radius ?? footprint.Reach;
        var max = Math.Max(MinRadius, reach);

        if (hints is { Ra: { } ra, Dec: { } dec }
            && SkyGeometry.Contains(outline, new SkyPoint(ra, dec)))
        {
            var r = Math.Clamp(hints.RadiusDeg is double given && given > 0 ? given : reach / 4, MinRadius, max);
            return Circle(file, ra, dec, r);
        }

        var centre = file.BoundingCircle?.Centre ?? footprint.Centre;
        return Circle(file, centre.Ra, centre.Dec, Math.Clamp(reach / 4, MinRadius, max));
    }

    /// <summary>A circle when the file takes one; otherwise the box around it, which it can take as a polygon.</summary>
    private static SkyRegion Circle(ICutoutFile file, double ra, double dec, double radius)
        => file.Supports("CIRCLE") ? SkyRegion.Circle(ra, dec, radius) : SkyRegion.Box(ra, dec, 2 * radius, 2 * radius);

    private static (double? Min, double? Max) SuggestBand(ICutoutFile file, CutoutHints? hints)
    {
        if (!file.Supports("BAND") || hints is null || (hints.BandMin is null && hints.BandMax is null))
            return (null, null);

        // Kept to the file's own range: the search's may be far wider than this one file.
        var min = hints.BandMin is { } a && file.BandMin is { } fa ? Math.Max(a, fa) : hints.BandMin;
        var max = hints.BandMax is { } b && file.BandMax is { } fb ? Math.Min(b, fb) : hints.BandMax;
        return min is { } lo && max is { } hi && lo >= hi ? (null, null) : (min, max);
    }
}
