namespace CanfarDesktop.Models.Cutouts;

/// <summary>
/// A file that can be cut, and what it can be cut by: which file it is, where it lies on the sky, and
/// the wavelengths, times and polarizations it covers — whoever does the cutting.
///
/// <para>CADC's DataLink describes this for its SODA service (<see cref="SodaDescriptor"/>); a file on
/// this computer describes it in its own header. What a cutout is judged against, and what an editor
/// draws and suggests from, is only this — so neither needs to know which of the two it has.</para>
/// </summary>
public interface ICutoutFile
{
    /// <summary>The file, as the archive names it (e.g. cadc:CFHTSG/….fits).</summary>
    string ArtifactId { get; }

    /// <summary>The file's own name.</summary>
    string FileName { get; }

    /// <summary>Where the file lies on the sky, as one outline; null when that is not known.</summary>
    SkyRegion? Footprint { get; }

    /// <summary>The smallest circle holding the whole file, when known.</summary>
    SkyRegion? BoundingCircle { get; }

    /// <summary>The wavelengths it covers, metres.</summary>
    double? BandMin { get; }
    double? BandMax { get; }

    /// <summary>The times it covers, MJD.</summary>
    double? TimeMin { get; }
    double? TimeMax { get; }

    /// <summary>The polarization states it can be cut to, when they are listed.</summary>
    IReadOnlyList<string> PolStates { get; }

    /// <summary>
    /// Upper-case names of the parameters it can be cut by — CIRCLE, POLYGON, BAND, TIME, POL. SODA's
    /// names, because they are the one vocabulary for what a cutout can ask, whoever answers it.
    /// </summary>
    IReadOnlySet<string> Parameters { get; }

    /// <summary>
    /// The parts it is made of, each with its own outline — the CCDs of a mosaic frame, the chips of an
    /// HST image — when there is more than one; empty otherwise. The footprint is their outline together.
    /// </summary>
    IReadOnlyList<SkyRegion> Parts => [];
}

/// <summary>What follows from what a file can be cut by.</summary>
public static class CutoutFileExtensions
{
    /// <summary>Whether it can be cut by this parameter (CIRCLE, BAND, …), whatever its case.</summary>
    public static bool Supports(this ICutoutFile file, string parameter)
        => file.Parameters.Contains(parameter.ToUpperInvariant());

    /// <summary>Whether it can be cut on the sky at all.</summary>
    public static bool SupportsSky(this ICutoutFile file) => file.Supports("CIRCLE") || file.Supports("POLYGON");
}
