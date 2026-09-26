namespace CanfarDesktop.Models.Cutouts;

/// <summary>
/// Another file of the observation that a cut can take along, box for box: a weight map beside its
/// image, on the same pixels, so the same box is the same sky in both.
/// </summary>
/// <param name="ArtifactId">The file, as the archive names it (e.g. cadc:CFHTSG/….weight.fits.fz).</param>
/// <param name="FileName">The file's own name.</param>
/// <param name="Unavailable">Why it cannot be cut with the file — not on the same pixels, not readable — or null when it can.</param>
public sealed record CutoutCompanion(string ArtifactId, string FileName, string? Unavailable = null);
