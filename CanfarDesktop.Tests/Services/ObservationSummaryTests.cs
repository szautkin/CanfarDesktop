using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

/// <summary>What "Copy details" puts on the clipboard — the same from Search, Research and the observation view.</summary>
public class ObservationSummaryTests
{
    private static DownloadedObservation M31() => new()
    {
        ObservationID = "G006.010.684+41.269.I",
        Collection = "CFHTMEGAPIPE",
        PublisherID = "ivo://cadc.nrc.ca/CFHTMEGAPIPE?G006.010.684+41.269/G006.010.684+41.269.I",
        TargetName = "M31",
        RA = "10.684708",
        Dec = "41.26875",
        Instrument = "MegaPrime",
        Filter = "i.MP9701",
        StartDate = "54000",
        CalLevel = "3",
        ProposalId = "MegaPipe",
        ProposalPi = "Gwyn",
        ProposalTitle = "MegaPipe stacks",
    };

    private static IReadOnlyList<string> Lines(DownloadedObservation o) => ObservationSummary.Text(o).Split(Environment.NewLine);

    [Fact]
    public void AnObservation_ReadsAsLabelledLines_InResearchsOrder()
    {
        var lines = Lines(M31());

        Assert.Equal("Observation ID: G006.010.684+41.269.I", lines[0]);
        Assert.Equal("Collection: CFHTMEGAPIPE", lines[1]);
        Assert.StartsWith("Publisher ID: ivo://cadc.nrc.ca/CFHTMEGAPIPE?", lines[2]);
        Assert.Equal("Target: M31", lines[3]);
        Assert.StartsWith("Position: ", lines[4]);
        Assert.Contains("Instrument: MegaPrime", lines);
        Assert.Contains("Filter: i.MP9701", lines);
        Assert.Contains($"Start Date: {CellFormatter.Format("startdate", "54000")}", lines); // as the result table shows it
        Assert.Contains("Proposal: MegaPipe — Gwyn: MegaPipe stacks", lines);
    }

    /// <summary>
    /// The position pastes straight back into the app's own search box and lands where it was — the
    /// degrees beside it make a third token, so the pair is what is pasted, the part before "(".
    /// </summary>
    [Fact]
    public void ThePosition_PastesBackIntoSearch_WhereItWas()
    {
        var position = Lines(M31())[4]["Position: ".Length..];
        var pair = position[..position.IndexOf(" (", StringComparison.Ordinal)];

        Assert.True(ADQLBuilder.TryParseCoordinatePair(pair, 0.01, out var ra, out var dec, out _));
        Assert.Equal(10.684708, ra, 4);   // to the sexagesimal's own precision, 0.01 s of RA
        Assert.Equal(41.26875, dec, 4);
        Assert.EndsWith("(10.684708°, +41.268750°)", position);
    }

    /// <summary>A record whose position came in sexagesimal (a search grid's own format) reads to the same place.</summary>
    [Fact]
    public void APositionGivenInSexagesimal_ReadsToTheSamePlace()
    {
        var sexagesimal = M31();
        (sexagesimal.RA, sexagesimal.Dec) = ("00:42:44.33", "+41:16:07.5");

        Assert.StartsWith("Position: 00:42:44.33 +41:16:07.5 (10.684708°, +41.268750°)", Lines(sexagesimal)[4]);
    }

    [Fact]
    public void WhatIsNotKnown_IsLeftOut_AndAPositionThatWillNotRead_IsGivenAsItIs()
    {
        var text = ObservationSummary.Text(new DownloadedObservation { ObservationID = "x", RA = "somewhere", Dec = "" });

        Assert.Equal("Observation ID: x" + Environment.NewLine + "RA: somewhere", text);
    }

    [Fact]
    public void ACutout_SaysItIsOne_AndHowItWasCut()
    {
        var cut = M31();
        cut.Cutout = new CutoutSpec { Region = SkyRegion.Circle(10.68, 41.27, 0.05), CutBy = CutoutMethod.Local };
        cut.LocalPath = "C:\\data\\m31.cutout-1a2b3c4d.fits";

        var lines = Lines(cut);

        Assert.Contains(lines, l => l.StartsWith("Cutout: r ") && l.EndsWith(" (cut on this computer)"));
        Assert.Equal("Path: C:\\data\\m31.cutout-1a2b3c4d.fits", lines[^1]);
    }

    [Fact]
    public void Labels_GoThroughTheTranslation()
    {
        try
        {
            ObservationSummary.Translate = key => key == "Research_RowTarget" ? "Cible" : null;
            Assert.Contains("Cible: M31", Lines(M31()));
        }
        finally
        {
            ObservationSummary.Translate = null;
        }
    }
}
