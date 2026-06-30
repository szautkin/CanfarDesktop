using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services.Export;

namespace CanfarDesktop.Tests.Services;

public class ResearchExportBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static List<DownloadedObservation> SampleObs() => new()
    {
        new DownloadedObservation
        {
            PublisherID = "caom:CFHT/1", Collection = "CFHT", ObservationID = "1", TargetName = "M31",
            Instrument = "MegaCam", Filter = "r", RA = "10.6", Dec = "41.2", StartDate = "2020-01-01",
            ProposalId = "20AC99", ProposalPi = "A. Smith", ProposalTitle = "M31 deep survey",
            DownloadedAt = DateTime.UtcNow,
        },
        new DownloadedObservation { PublisherID = "caom:CFHT/2", Collection = "CFHT", ObservationID = "2" },
    };

    private static List<ObservationNote> SampleNotes() => new()
    {
        new ObservationNote { PublisherID = "caom:CFHT/1", Note = "Great seeing.", Rating = 5, Tags = new[] { "calibration", "deep" }, UpdatedUtc = Now },
    };

    [Fact]
    public void Build_WritesObservationsNotesJson_AndCounts()
    {
        var output = ResearchExportBuilder.Build(SampleObs(), SampleNotes(), new ExportOptions(), Now);

        Assert.True(output.JsonFiles.ContainsKey("observations.json"));
        Assert.True(output.JsonFiles.ContainsKey("notes.json"));
        Assert.Equal(2, output.ItemCounts["observations"]);
        Assert.Equal(1, output.ItemCounts["notes"]);
    }

    [Fact]
    public void Build_NotesMarkdown_HasSectionRatingTagsAndText()
    {
        var md = ResearchExportBuilder.Build(SampleObs(), SampleNotes(), new ExportOptions(), Now).MarkdownFiles["notes.md"];

        Assert.Contains("1 of 2 observations have notes", md);
        Assert.Contains("## M31 — CFHT 1", md);
        Assert.Contains("**Publisher ID:** `caom:CFHT/1`", md);
        Assert.Contains("★★★★★", md);            // 5-star rating
        Assert.Contains("(Excellent)", md);
        Assert.Contains("`calibration`", md);
        Assert.Contains("Great seeing.", md);
    }

    [Fact]
    public void Build_NotesMarkdown_RendersProposalCitationHandle()
    {
        // SCI-9-2: CAOM2 has no per-observation DOI; the proposal is the citable handle we surface.
        var md = ResearchExportBuilder.Build(SampleObs(), SampleNotes(), new ExportOptions(), Now).MarkdownFiles["notes.md"];
        Assert.Contains("**Proposal (cite):** 20AC99 — M31 deep survey (PI A. Smith)", md);
    }

    [Fact]
    public void Build_NoNotes_ShowsEmptyMessage()
    {
        var md = ResearchExportBuilder.Build(SampleObs(), new List<ObservationNote>(), new ExportOptions(), Now).MarkdownFiles["notes.md"];
        Assert.Contains("No notes have been written yet", md);
    }

    [Fact]
    public void Build_IncludeNotesFalse_OmitsNotes()
    {
        var output = ResearchExportBuilder.Build(SampleObs(), SampleNotes(), new ExportOptions { IncludeNotes = false }, Now);
        Assert.False(output.JsonFiles.ContainsKey("notes.json"));
        Assert.False(output.MarkdownFiles.ContainsKey("notes.md"));
        Assert.False(output.ItemCounts.ContainsKey("notes"));
    }

    [Fact]
    public void Build_IncludeFileCopies_OnlyAttachesExistingFiles()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var obs = new List<DownloadedObservation>
            {
                new() { PublisherID = "x", LocalPath = tmp },
                new() { PublisherID = "y", LocalPath = Path.Combine(Path.GetTempPath(), "definitely-missing-" + Guid.NewGuid().ToString("N") + ".fits") },
            };
            var output = ResearchExportBuilder.Build(obs, new List<ObservationNote>(), new ExportOptions { IncludeFileCopies = true }, Now);

            Assert.Single(output.AttachedFiles);
            Assert.Contains(tmp, output.AttachedFiles);
        }
        finally { File.Delete(tmp); }
    }
}
