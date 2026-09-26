using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// A file on this computer, cut here: instant, offline, repeatable — and the only way for a file CADC
/// will not cut, such as its HST mirror's. The same editor, the same rules and the same Research record
/// as a cut on CADC's side; what is its own is whether its images can be placed on the sky at all, and
/// whether the region falls on one of them.
/// </summary>
public sealed class LocalCutoutSource(LocalFitsFile file) : ICutoutSource
{
    public CutoutMethod Method => CutoutMethod.Local;

    public ICutoutFile File => file;

    /// <summary>The file as its headers describe it.</summary>
    public LocalFitsFile LocalFile => file;

    public long? WholeFileBytes => file.FileBytes > 0 ? file.FileBytes : null;

    public string? Unavailable => file.Problem;

    public CutoutCheck Check(CutoutSpec spec)
    {
        if (file.Problem is { } problem) return new CutoutCheck([problem], []);

        var check = CutoutRules.Check(file, spec);
        if (!check.IsValid) return check;

        // The outline of a mosaic's CCDs holds its gaps too: a region can be inside it and on no CCD —
        // or on some, just not the ones chosen.
        if (!PlanFor(spec).IsEmpty) return check;
        return check.With([spec.Extensions.Count > 0 && !LocalCutPlan.For(file, spec with { Extensions = [] }).IsEmpty
            ? CutoutRules.T("Cutout_LocalOnNoChosenImage", "That region falls on none of the images chosen.")
            : CutoutRules.T("Cutout_LocalOnNoImage", "That region falls on none of this file's images.")], []);
    }

    /// <summary>Exact, not estimated: the plans know every byte they will write — the cutout's, and its companions'.</summary>
    public long? EstimateBytes(CutoutSpec spec)
        => file.Problem is null && CutoutRules.Check(file, spec).IsValid && PlanFor(spec) is { IsEmpty: false } plan
            ? plan.Bytes + plan.CompanionsOf(file, spec).Sum(c => c.Plan.Bytes)
            : null;

    /// <summary>
    /// The plan for a cutout, made once for as long as the cutout is the same one. The editor asks for
    /// its check, its size and its size alone on every keystroke and every step of a drag; each plan
    /// places the region on every image — 36 of them, through SIP, on a MegaPrime frame.
    /// </summary>
    private LocalCutPlan PlanFor(CutoutSpec spec)
    {
        var key = spec.Identity; // what decides the plan: the region, band, images — not the companions taken along
        if (_last is { } last && last.Key == key) return last.Plan;
        var plan = LocalCutPlan.For(file, spec);
        _last = new Planned(key, plan);
        return plan;
    }

    /// <summary>The last plan made, as one reference, so a reader on another thread sees a whole pair.</summary>
    private Planned? _last;

    private sealed record Planned(string Key, LocalCutPlan Plan);
}
