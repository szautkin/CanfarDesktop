using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// A copied position has to paste back into this app.
///
/// <para>These tests are deliberately not about how the string looks. They put what the clipboard
/// gets through the search form's own parser, because that is the only property worth holding: the
/// obvious thing to do with a copied coordinate is paste it into Search, and a format only a human
/// can read is a format that silently searches for an observation NAMED after the position.</para>
///
/// <para>That is exactly what shipped first, so the round trip is the regression guard.</para>
/// </summary>
public class MarkClipboardTests
{
    /// <summary>The search box, asked the way the ADQL builder asks it.</summary>
    private static (bool Ok, double Ra, double Dec, double Radius) Search(string pasted)
    {
        var ok = ADQLBuilder.TryParseCoordinatePair(
            pasted, defaultRadius: 0.1, out var ra, out var dec, out var radius);

        return (ok, ra, dec, radius);
    }

    // ── The round trip ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(240.0, 48.0)]
    [InlineData(10.684708, 41.268750)]   // M31
    [InlineData(0.0, 0.0)]
    [InlineData(359.9, -89.9)]
    [InlineData(180.0, -0.5)]
    [InlineData(23.5, 90.0)]             // the pole
    public void WhatWeCopyIsWhatOurSearchAccepts(double ra, double dec)
    {
        var parsed = Search(MarkClipboard.Sky(ra, dec));

        Assert.True(parsed.Ok);

        // A tenth of an arcsecond of Dec is the precision the string carries, so that is the
        // tolerance. Tighter would be testing the formatter's rounding, not the round trip.
        Assert.Equal(ra, parsed.Ra, 0.0002);
        Assert.Equal(dec, parsed.Dec, 0.0002);
    }

    /// <summary>
    /// Two tokens exactly. A third is read as a search radius, so appending the degrees "for
    /// convenience" would turn a position into a cone several hundred degrees wide.
    /// </summary>
    [Fact]
    public void ItIsTwoTokensSoNothingIsReadAsARadius()
    {
        var copied = MarkClipboard.Sky(240.0, 48.0);

        Assert.Equal(2, copied.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(0.1, Search(copied).Radius);
    }

    /// <summary>
    /// RA that rounds past the last centisecond of the day comes back as 00:00:00, not 24:00:00.
    /// The parser rejects 24 hours outright, so a mark near RA 360 would otherwise copy a string
    /// this app cannot read — and only marks near RA 360 would be affected, which is the kind of
    /// thing found in the field rather than at a desk.
    /// </summary>
    [Fact]
    public void AMarkJustShyOfThreeSixtyStillPastes()
    {
        var parsed = Search(MarkClipboard.Sky(359.99999999, 12.0));

        Assert.True(parsed.Ok);
        Assert.Equal(0.0, parsed.Ra, 0.001);
    }

    /// <summary>Negative declinations keep their sign through the trip.</summary>
    [Fact]
    public void TheSouthIsStillInTheSouth()
        => Assert.True(Search(MarkClipboard.Sky(150.0, -33.75)).Dec < 0);

    // ── The formats that are not sky positions ──────────────────────────────────────────────────

    /// <summary>
    /// A pixel says it is a pixel. Nothing parses it as a coordinate, and that is correct — but it
    /// must not parse as one either, or a pixel pair would silently become a sky position.
    /// </summary>
    [Fact]
    public void APixelIsLabelledAndIsNotMistakenForTheSky()
    {
        var copied = MarkClipboard.ImagePixel(1056.5, 2322.25);

        Assert.Contains("px", copied);
        Assert.False(Search(copied).Ok);
    }

    /// <summary>A voxel names all three of its parts, the channel included.</summary>
    [Fact]
    public void AVoxelNamesItsChannel()
    {
        var copied = MarkClipboard.Voxel(64.0, 64.0, 200.0);

        Assert.Contains("channel=200", copied);
        Assert.False(Search(copied).Ok);
    }

    /// <summary>
    /// Written invariantly. In a locale where the decimal separator is a comma, a pixel pair would
    /// otherwise come out as "1056,5, 2322,25" — which is four numbers or two, depending on who is
    /// reading.
    /// </summary>
    [Fact]
    public void NumbersDoNotChangeShapeWithTheLocale()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("fr-FR");

            Assert.Equal("1056.5, 2322.25 px", MarkClipboard.ImagePixel(1056.5, 2322.25));
            Assert.True(Search(MarkClipboard.Sky(240.0, 48.0)).Ok);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
