using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Models.Cutouts;

/// <summary>
/// What a cutout cuts: which file, what part of the sky, and optionally what part of the spectrum,
/// time or polarization. Units are SODA's own — metres for wavelength, MJD for time — so what is
/// saved is what was sent.
///
/// <para>Held on a Research record as <see cref="DownloadedObservation.Cutout"/>: a record with one is
/// a cutout of its observation, and one without is the complete observation.</para>
/// </summary>
public sealed record CutoutSpec
{
    /// <summary>The file cut from, as SODA names it (e.g. cadc:CFHTSG/….fits) — one observation can have several.</summary>
    public string ArtifactId { get; init; } = string.Empty;

    public SkyRegion? Region { get; init; }

    public double? BandMin { get; init; }
    public double? BandMax { get; init; }

    public double? TimeMin { get; init; }
    public double? TimeMax { get; init; }

    public IReadOnlyList<string> Pol { get; init; } = [];

    /// <summary>
    /// Which of a multi-extension file's images to keep, by name ("SCI,1", "ccd07", or an index when
    /// they have none); empty for every image the region falls on — what a cut did before there was a
    /// choice, and what CADC's cut does.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>
    /// The observation's other files cut with it, box for box, by artifact id — the weight map of a
    /// MegaPipe tile. Not part of <see cref="Key"/>: the cutout is the same cutout with its weight map or
    /// without, and each companion's own cutout is named by the same key
    /// (<see cref="CompanionPath"/>), so the pair can be told as a pair.
    /// </summary>
    public IReadOnlyList<string> Companions { get; init; } = [];

    /// <summary>
    /// Who cuts it: CADC's SODA service (the default, and what every record saved before there was a
    /// choice was), or this computer, from the complete file already downloaded.
    /// </summary>
    public CutoutMethod CutBy { get; init; } = CutoutMethod.Soda;

    /// <summary>Nothing to cut: the file would come back whole.</summary>
    [JsonIgnore]
    public bool IsEmpty => Region is null && BandMin is null && BandMax is null
                           && TimeMin is null && TimeMax is null && Pol.Count == 0;

    /// <summary>
    /// Eight characters that are the same for the same cutout and differ for a different one — what
    /// tells two cutouts of one observation apart in Research, and names the file.
    ///
    /// <para>A local cut of a region is a different product from CADC's cut of it, so the method is part
    /// of the key — but only when it is local: a SODA cutout's key is what it was before there was a
    /// choice, so the records and files already saved under it keep their names.</para>
    /// </summary>
    [JsonIgnore]
    public string Key
    {
        get
        {
            var canonical = string.Join('|',
                ArtifactId,
                Region?.Shape.ToString() ?? "",
                Region is null ? "" : Region.ToSoda(),
                Num(BandMin), Num(BandMax), Num(TimeMin), Num(TimeMax),
                string.Join(',', Pol));
            if (CutBy != CutoutMethod.Soda) canonical += "|" + CutBy.ToString().ToLowerInvariant();
            if (Extensions.Count > 0) canonical += "|ext:" + string.Join(';', Extensions);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        }
    }

    /// <summary>
    /// What it covers, in a line: the region, then the band and time when they narrow it —
    /// "r 2.0′ @ 10.68000°, +41.27000° · 866–868 µm".
    /// </summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Region is not null) parts.Add(Region.Describe());
            if (BandMin is not null || BandMax is not null) parts.Add(Caom2Format.WavelengthRange(BandMin, BandMax));
            if (TimeMin is not null || TimeMax is not null)
                parts.Add($"{Caom2Format.MjdToDate(TimeMin)} – {Caom2Format.MjdToDate(TimeMax)}");
            if (Pol.Count > 0) parts.Add(string.Join(", ", Pol));
            if (Extensions.Count > 0) parts.Add(string.Join(" ", Extensions.Select(e => $"[{e}]")));
            if (Companions.Count > 0) parts.Add("+ " + string.Join(", ", Companions.Select(Caom2Format.ArtifactFileName)));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// What to call the cutout's file: the file it was cut from, marked as a cutout and by which one —
    /// "G006.010.684+41.269.R.cutout-1a2b3c4d.fits". Always .fits: SODA sends the cut as plain FITS,
    /// whatever compression the whole file had, and so does a local cut.
    /// </summary>
    [JsonIgnore]
    public string FileName => FileNameFor(Caom2Format.ArtifactFileName(ArtifactId));

    public string FileNameFor(string artifactFileName)
    {
        var stem = artifactFileName;
        foreach (var ext in new[] { ".fz", ".gz" })
            if (stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) stem = stem[..^ext.Length];
        if (stem.EndsWith(".fits", StringComparison.OrdinalIgnoreCase)) stem = stem[..^".fits".Length];
        return stem.Length == 0 ? $"cutout-{Key}.fits" : $"{stem}.cutout-{Key}.fits";
    }

    /// <summary>
    /// Where a companion's cutout goes: beside the cutout, named from the companion's own file and this
    /// cutout's key — "….R.weight.cutout-1a2b3c4d.fits" beside "….R.cutout-1a2b3c4d.fits" — so a record
    /// finds its companions' files again from itself alone.
    /// </summary>
    public string CompanionPath(string cutoutPath, string companionArtifactId)
        => Path.Combine(Path.GetDirectoryName(cutoutPath) ?? string.Empty,
                        FileNameFor(Caom2Format.ArtifactFileName(companionArtifactId)));

    /// <summary>Where every companion's cutout goes, beside the cutout at <paramref name="cutoutPath"/>.</summary>
    public IEnumerable<string> CompanionPaths(string cutoutPath)
        => Companions.Distinct().Select(id => CompanionPath(cutoutPath, id));

    private static string Num(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "";
}
