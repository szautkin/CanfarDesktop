using System.Globalization;
using System.Text;
using System.Text.Json;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.Export;

/// <summary>
/// Pure builder for the Research module's export payload (observations.json + notes.json + notes.md),
/// 1-to-1 with the macOS ResearchExporter. Separated from the store-backed adapter so the markdown +
/// JSON rendering is unit-testable with fabricated data.
/// </summary>
public static class ResearchExportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ExportModuleOutput Build(
        IReadOnlyList<DownloadedObservation> observations,
        IReadOnlyList<ObservationNote> notes,
        ExportOptions options,
        DateTimeOffset now)
    {
        var output = new ExportModuleOutput();

        output.JsonFiles["observations.json"] = JsonSerializer.Serialize(observations, JsonOptions);
        output.ItemCounts["observations"] = observations.Count;

        if (options.IncludeNotes)
        {
            output.JsonFiles["notes.json"] = JsonSerializer.Serialize(notes, JsonOptions);
            output.ItemCounts["notes"] = notes.Count;
            output.MarkdownFiles["notes.md"] = RenderNotesMarkdown(observations, notes, now);
        }

        if (options.IncludeFileCopies)
            foreach (var obs in observations.Where(o => o.FileExists))
                output.AttachedFiles.Add(obs.LocalPath);

        return output;
    }

    /// <summary>One markdown document, one section per observation that has a note (in download order).</summary>
    private static string RenderNotesMarkdown(
        IReadOnlyList<DownloadedObservation> observations,
        IReadOnlyList<ObservationNote> notes,
        DateTimeOffset now)
    {
        var byId = new Dictionary<string, ObservationNote>(StringComparer.Ordinal);
        foreach (var n in notes)
            if (!string.IsNullOrEmpty(n.PublisherID)) byId[n.PublisherID] = n;

        var withNotes = observations.Where(o => byId.ContainsKey(o.PublisherID)).ToList();

        var md = new StringBuilder();
        md.Append("# Research Notes\n\n");
        md.Append($"Exported {Iso(now)}. ");
        md.Append($"{withNotes.Count} of {observations.Count} observations have notes.\n\n");
        md.Append("---\n\n");

        if (withNotes.Count == 0)
        {
            md.Append("_No notes have been written yet._\n");
            return md.ToString();
        }

        foreach (var obs in withNotes)
            md.Append(RenderObservationSection(obs, byId[obs.PublisherID]));

        return md.ToString();
    }

    private static string RenderObservationSection(DownloadedObservation obs, ObservationNote note)
    {
        var title = string.IsNullOrEmpty(obs.TargetName) ? obs.ObservationID : obs.TargetName;
        var md = new StringBuilder();
        md.Append($"## {title} — {obs.Collection} {obs.ObservationID}\n\n");

        md.Append($"- **Publisher ID:** `{obs.PublisherID}`\n");
        if (!string.IsNullOrEmpty(obs.TargetName)) md.Append($"- **Target:** {obs.TargetName}\n");
        md.Append($"- **Collection:** {obs.Collection}\n");
        md.Append($"- **Observation ID:** {obs.ObservationID}\n");
        if (!string.IsNullOrEmpty(obs.Instrument))
        {
            var instrument = string.IsNullOrEmpty(obs.Filter) ? obs.Instrument : $"{obs.Instrument} / {obs.Filter}";
            md.Append($"- **Instrument:** {instrument}\n");
        }
        if (!string.IsNullOrEmpty(obs.RA) || !string.IsNullOrEmpty(obs.Dec))
            md.Append($"- **Coordinates:** RA {obs.RA}, Dec {obs.Dec}\n");
        if (!string.IsNullOrEmpty(obs.StartDate))
            md.Append($"- **Start date:** {obs.StartDate}\n");
        md.Append($"- **Downloaded:** {Iso(obs.DownloadedAt)}\n");
        if (note.Rating > 0)
            md.Append($"- **Quality:** {Stars(note.Rating)} ({QualityLabel(note.Rating)})\n");
        if (note.Tags.Count > 0)
            md.Append($"- **Tags:** {string.Join(", ", note.Tags.Select(t => $"`{t}`"))}\n");
        md.Append($"- **Note modified:** {Iso(note.UpdatedUtc)}\n");

        var trimmed = note.Note.Trim();
        if (trimmed.Length > 0)
        {
            md.Append("\n### Notes\n\n");
            md.Append(trimmed);
            md.Append('\n');
        }

        md.Append("\n---\n\n");
        return md.ToString();
    }

    private static string Stars(int n)
    {
        var filled = Math.Clamp(n, 0, 5);
        return new string('★', filled) + new string('☆', 5 - filled);
    }

    private static string QualityLabel(int stars) => stars switch
    {
        1 => "Unusable",
        2 => "Poor",
        3 => "Fair",
        4 => "Good",
        5 => "Excellent",
        _ => string.Empty,
    };

    private static string Iso(DateTimeOffset dt) => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    private static string Iso(DateTime dt) => Iso(new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt));
}
