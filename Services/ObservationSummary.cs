using System.Globalization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services;

/// <summary>
/// An observation as text to copy: what it is, where it is, what took it — the same lines whichever
/// screen it is copied from.
///
/// <para>A search row, a Research record and CAOM2's answer all become a Research record already
/// (<see cref="DownloadedObservation.FromSearchResult"/>, <see cref="ResearchRecords.ForObservation"/>),
/// so this reads only that: three screens, one summary, and nothing mapped twice. The labels are
/// Research's own, the date and calibration level as the result table shows them, and the position in
/// the form the app's own search box, Simbad and DS9 all read, with the degrees beside it.</para>
///
/// <para>Takes its translations through <see cref="Translate"/>, as <c>CutoutRules</c> does, so it is
/// tested without a packaged app; unset, it answers in English.</para>
/// </summary>
public static class ObservationSummary
{
    public static Func<string, string?>? Translate { get; set; }

    private static string T(string key, string english) => Translate?.Invoke(key) ?? english;

    /// <summary>"Label: value" lines, one per thing known; nothing for what is not.</summary>
    public static string Text(DownloadedObservation o)
    {
        var lines = new List<string>();
        void Add(string key, string english, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{T(key, english)}: {value.Trim()}");
        }

        Add("Research_RowObservationId", "Observation ID", o.ObservationID);
        Add("Research_RowCollection", "Collection", o.Collection);
        Add("Summary_PublisherId", "Publisher ID", o.PublisherID);
        Add("Research_RowTarget", "Target", o.TargetName);
        if (Position(o.RA, o.Dec) is { } position) Add("Summary_Position", "Position", position);
        else
        {
            Add("Research_RowRa", "RA", o.RA);
            Add("Research_RowDec", "Dec", o.Dec);
        }
        Add("Research_RowInstrument", "Instrument", o.Instrument);
        Add("Research_RowFilter", "Filter", o.Filter);
        if (!string.IsNullOrWhiteSpace(o.StartDate)) Add("Research_RowStartDate", "Start Date", CellFormatter.Format("startdate", o.StartDate));
        if (!string.IsNullOrWhiteSpace(o.CalLevel)) Add("Research_RowCalLevel", "Cal. Level", CellFormatter.Format("callev", o.CalLevel));
        Add("Summary_Proposal", "Proposal", Proposal(o));
        Add("Summary_Released", "Released", o.DataRelease);
        if (o.Cutout is { } cutout)
            Add("Summary_Cutout", "Cutout", cutout.CutBy == CutoutMethod.Local
                ? $"{cutout.Summary} ({T("Summary_CutLocally", "cut on this computer")})"
                : cutout.Summary);
        Add("Research_RowPath", "Path", o.LocalPath);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>"00:42:44.33 +41:16:07.5 (10.684708°, 41.268750°)", from degrees or sexagesimal; null when either will not read.</summary>
    public static string? Position(string? ra, string? dec)
    {
        if (!Sexagesimal.TryParseAngle(ra, isRa: true, out var r) || !Sexagesimal.TryParseAngle(dec, isRa: false, out var d)) return null;
        return string.Create(CultureInfo.InvariantCulture, $"{MarkClipboard.Sky(r, d)} ({r:0.000000}°, {d:+0.000000;-0.000000}°)");
    }

    /// <summary>"12AQ01 — Smith: M31 halo", from as much of it as is known.</summary>
    private static string? Proposal(DownloadedObservation o)
    {
        var who = string.Join(" — ", new[] { o.ProposalId, o.ProposalPi }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        if (string.IsNullOrWhiteSpace(o.ProposalTitle)) return who.Length > 0 ? who : null;
        return who.Length > 0 ? $"{who}: {o.ProposalTitle.Trim()}" : o.ProposalTitle.Trim();
    }
}
