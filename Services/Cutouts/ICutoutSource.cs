using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// One way of cutting one file: the file, who cuts it, how a cutout of it is judged, and how large it
/// will be.
///
/// <para>What the cutout editor and an agent's cutout tools work with. CADC's SODA service is one
/// (<see cref="SodaCutoutSource"/>); the complete file on this computer is another. A file either can
/// be cut offers one source for each way; the editor shows whichever there are, and nothing else in it
/// knows which it has.</para>
/// </summary>
public interface ICutoutSource
{
    CutoutMethod Method { get; }

    /// <summary>The file, and what it can be cut by.</summary>
    ICutoutFile File { get; }

    /// <summary>The whole file's size, when known — what the estimate is read against.</summary>
    long? WholeFileBytes { get; }

    /// <summary>
    /// Why this way cannot cut this file at all, whatever the region — the downloaded copy has no sky
    /// coordinates, say — or null when it can. Offered greyed with this reason, never hidden.
    /// </summary>
    string? Unavailable => null;

    /// <summary>
    /// Whether this way can make this cutout: <see cref="CutoutRules.Check"/>, and whatever only this
    /// way knows. Never throws — the reasons are the answer.
    /// </summary>
    CutoutCheck Check(CutoutSpec spec);

    /// <summary>About how many bytes the cutout will be, with the companions it takes along; null when there is nothing to go on.</summary>
    long? EstimateBytes(CutoutSpec spec);
}

/// <summary>What follows from a way of cutting a file, whichever it is.</summary>
public static class CutoutSourceExtensions
{
    /// <summary>A cutout made this way: of this file, cut by this method, whatever it said before.</summary>
    public static CutoutSpec Bind(this ICutoutSource source, CutoutSpec spec)
        => spec with { ArtifactId = source.File.ArtifactId, CutBy = source.Method };

    /// <summary>The cutout an editor opens on — from the search, when it looked at this file.</summary>
    public static CutoutSpec Suggest(this ICutoutSource source, CutoutHints? hints)
        => source.Bind(CutoutPrefill.Suggest(source.File, hints));
}

/// <summary>A file CADC's SODA service can cut, as its DataLink answer describes it.</summary>
/// <param name="WholeFileBytes">The whole file's size, from CAOM2; the estimate is a share of it.</param>
public sealed record SodaCutoutSource(SodaDescriptor Descriptor, long? WholeFileBytes = null) : ICutoutSource
{
    public CutoutMethod Method => CutoutMethod.Soda;

    public ICutoutFile File => Descriptor;

    /// <summary>SODA's own rules are the common ones: it answers a refused cutout with a bare HTTP 400.</summary>
    public CutoutCheck Check(CutoutSpec spec) => CutoutRules.Check(Descriptor, spec);

    public long? EstimateBytes(CutoutSpec spec) => SodaRequest.EstimateBytes(Descriptor, spec, WholeFileBytes);
}
