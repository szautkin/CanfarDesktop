using System.Globalization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services;

/// <summary>
/// A Research record for an observation, from what CAOM2 and DataLink say about it.
///
/// <para>This was written twice: the observation view filled in a handful of fields, an agent's
/// download filled in more (filter, start, calibration level, position), and the two records of one
/// observation differed depending on who fetched it. One builder now serves the observation view,
/// the agent, and cutouts.</para>
/// </summary>
public static class ResearchRecords
{
    /// <summary>A record for this observation — the complete one, or a cutout of it.</summary>
    public static DownloadedObservation ForObservation(
        string publisherId, CAOM2Observation? caom2, DataLinkResult? links,
        CutoutSpec? cutout = null, AgentAttribution? attribution = null)
    {
        var record = new DownloadedObservation
        {
            PublisherID = publisherId,
            Cutout = cutout,
            AgentAttribution = attribution,
            ThumbnailURL = links?.Thumbnails.FirstOrDefault(),
            PreviewURL = links?.Previews.FirstOrDefault(),
        };
        FillFromCaom2(record, caom2);
        return record;
    }

    /// <summary>
    /// The record of the complete observation — not a cutout of it — with its file or without; null
    /// when Research keeps only cutouts of it, or nothing. Research keeps at most one per observation.
    /// </summary>
    public static DownloadedObservation? Complete(IEnumerable<DownloadedObservation> records, string publisherId)
        => records.FirstOrDefault(r => r.PublisherID == publisherId && !r.IsCutout);

    /// <summary>
    /// The archive's id for the observation a record is of — what Search finds it by: as the record
    /// has it, else as its publisher ID says; empty when neither says.
    /// </summary>
    public static string ObservationIdOf(DownloadedObservation record)
        => record.ObservationID is { Length: > 0 } id ? id : Caom2Uri.Split(record.PublisherID).ObservationId;

    /// <summary>
    /// Delete a record's file from this computer — with a cutout's companions cut with it — and keep the
    /// record: its metadata, its notes, and for a cutout its region, so it can be fetched again exactly as
    /// it was. Null when done, otherwise why not; a file already gone is not a failure, only a record to tidy.
    /// </summary>
    public static string? RemoveLocalFile(ObservationStore store, DownloadedObservation record)
    {
        // Open elsewhere, or not ours to delete: the record keeps pointing at its file, which is still there.
        if (DeleteLocalFiles(record) is { } why) return why;

        record.LocalPath = string.Empty;
        record.FileSize = null;
        store.Save(record);
        return null;
    }

    /// <summary>
    /// Delete every file on this computer that is the record's (<see cref="DownloadedObservation.LocalFiles"/>):
    /// its companions' first and its own last, stopping at the first that cannot be deleted — so the
    /// record's own file, which it points at, goes only once the files that go with it have, and a
    /// record never points at a file that is gone. Null when done, otherwise why not.
    /// </summary>
    public static string? DeleteLocalFiles(DownloadedObservation record)
    {
        foreach (var path in record.LocalFiles.Reverse())
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return ex.Message;
            }
        }
        return null;
    }

    /// <summary>
    /// Fill the record's EMPTY fields from CAOM2. Only empty ones: a record saved from a search row
    /// already carries the grid's own values, and this tops it up rather than overwriting them.
    /// </summary>
    public static void FillFromCaom2(DownloadedObservation obs, CAOM2Observation? caom2)
    {
        if (caom2 is null) return;
        var inv = CultureInfo.InvariantCulture;

        static void Fill(Func<string> read, Action<string> write, string? value)
        {
            if (!string.IsNullOrWhiteSpace(read()) || string.IsNullOrWhiteSpace(value)) return;
            write(value!);
        }

        Fill(() => obs.Collection, v => obs.Collection = v, caom2.Collection);
        Fill(() => obs.ObservationID, v => obs.ObservationID = v, caom2.ObservationID);
        Fill(() => obs.TargetName, v => obs.TargetName = v, caom2.Target?.Name);
        Fill(() => obs.Instrument, v => obs.Instrument = v, caom2.Instrument?.Name);
        if (caom2.Proposal is { } prop)
        {
            Fill(() => obs.ProposalId, v => obs.ProposalId = v, prop.Id);
            Fill(() => obs.ProposalPi, v => obs.ProposalPi = v, prop.Pi);
            Fill(() => obs.ProposalTitle, v => obs.ProposalTitle = v, prop.Title);
        }

        var plane = caom2.Planes.FirstOrDefault();
        if (plane is null) return;
        Fill(() => obs.CalLevel, v => obs.CalLevel = v,
            plane.CalibrationLevel is int cl ? cl.ToString(inv) : null);
        Fill(() => obs.DataRelease, v => obs.DataRelease = v,
            plane.DataRelease is { } dr ? dr.ToString("yyyy-MM-dd", inv) : null);

        // The bandpass IS the filter, and the temporal lower bound is the start of the observation.
        Fill(() => obs.Filter, v => obs.Filter = v, plane.Energy?.BandpassName);
        Fill(() => obs.StartDate, v => obs.StartDate = v,
            plane.Time?.LowerMJD is { } mjd ? Caom2Format.MjdToDate(mjd) : null);

        // The footprint's centre on the sphere — a plain average of RA goes wrong for a field
        // across RA 0°, where it lands on the far side of the sky.
        if (plane.Position?.Polygon is { Count: > 0 } poly && string.IsNullOrWhiteSpace(obs.RA))
        {
            var centre = SkyGeometry.Centroid(poly.Select(v => new SkyPoint(v.Ra, v.Dec)));
            obs.RA = centre.Ra.ToString("F6", inv);
            obs.Dec = centre.Dec.ToString("F6", inv);
        }
    }
}
