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

        // The outline of a mosaic's CCDs holds its gaps too: a region can be inside it and on no CCD.
        return LocalCutPlan.For(file, spec).IsEmpty
            ? check.With([CutoutRules.T("Cutout_LocalOnNoImage", "That region falls on none of this file's images.")], [])
            : check;
    }

    /// <summary>Exact, not estimated: the plan knows every byte it will write.</summary>
    public long? EstimateBytes(CutoutSpec spec)
        => file.Problem is null && CutoutRules.Check(file, spec).IsValid && LocalCutPlan.For(file, spec) is { IsEmpty: false } plan
            ? plan.Bytes
            : null;
}
