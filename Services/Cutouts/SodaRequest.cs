using System.Globalization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// The SODA request made from a cutout, and how large CADC's answer will be.
///
/// <para>The URL is only ever built from a cutout that passed <see cref="CutoutRules.Check"/> — the
/// judgement the editor shows and an agent is refused with — so no two surfaces can disagree about what
/// is allowed.</para>
/// </summary>
public static class SodaRequest
{
    /// <summary>
    /// The request for a cutout that passed <see cref="CutoutRules.Check"/>: SODA's sync endpoint with
    /// the file's ID and one value for each parameter the cutout sets. Throws for one that did not, with
    /// the first reason — building a URL for a refused cutout is a bug, not a request.
    /// </summary>
    public static string Url(SodaDescriptor file, CutoutSpec spec)
    {
        var check = CutoutRules.Check(file, spec);
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
