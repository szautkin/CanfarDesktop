using System.Text;
using Xunit;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>A header rewritten by a cut: new values in FITS's fixed format, everything else as it came.</summary>
public class FitsHeaderCardsTests
{
    private static string Card(string text) => text.PadRight(80);

    [Fact]
    public void ANewNumber_IsRightJustifiedToColumnThirty()
    {
        var cards = new FitsHeaderCards([]);
        cards.Set("CRPIX1", 16.5);
        cards.Set("NAXIS1", 11L);
        cards.Set("EXTEND", true);

        Assert.Equal(Card("CRPIX1  =                 16.5"), cards.Cards[0]);
        Assert.Equal(Card("NAXIS1  =                   11"), cards.Cards[1]);
        Assert.Equal(Card("EXTEND  =                    T"), cards.Cards[2]);
    }

    /// <summary>A whole number stays a real number when read back: 100 is written 100.0; round-trip digits otherwise.</summary>
    [Theory]
    [InlineData(100.0, "100.0")]
    [InlineData(-45.75, "-45.75")]
    [InlineData(1e-5, "1E-05")]
    [InlineData(1024.123456789012, "1024.123456789012")]
    public void ARealNumber_ReadsBackAsTheSameRealNumber(double value, string written)
        => Assert.Equal(written, FitsHeaderCards.FormatReal(value));

    [Fact]
    public void AStringIsQuoted_PaddedToEight_AndItsQuotesDoubled()
    {
        var cards = new FitsHeaderCards([]);
        cards.SetString("OBJECT", "M31");
        cards.SetString("OBSERVER", "O'Neil");

        Assert.Equal(Card("OBJECT  = 'M31     '"), cards.Cards[0]);
        Assert.Equal(Card("OBSERVER= 'O''Neil '"), cards.Cards[1]);
    }

    /// <summary>
    /// A string with a comment is written in fixed format, as astropy writes it — the card astropy builds
    /// when it verifies a CHECKSUM, character for character.
    /// </summary>
    [Fact]
    public void AStringWithAComment_IsInFixedFormat_AsAstropyWritesIt()
    {
        var cards = new FitsHeaderCards([]);
        cards.SetString("CHECKSUM", "0000000000000000", "HDU checksum");

        Assert.Equal(Card("CHECKSUM= '0000000000000000'   / HDU checksum"), cards.Cards[0]);
    }

    /// <summary>Changing a value keeps the card where it was, and its comment.</summary>
    [Fact]
    public void ChangingAValue_KeepsItsPlaceAndItsComment()
    {
        var cards = new FitsHeaderCards([Card("SIMPLE  =                    T"), Card("CRPIX1  =               1024.5 / reference pixel"), Card("OBJECT  = 'M31     '")]);

        cards.Set("CRPIX1", 16.5);

        Assert.Equal(Card("CRPIX1  =                 16.5 / reference pixel"), cards.Cards[1]);
        Assert.Equal(Card("OBJECT  = 'M31     '"), cards.Cards[2]);
    }

    [Fact]
    public void History_IsWrappedToTheCard_AndKeptToPrintableAscii()
    {
        var cards = new FitsHeaderCards([]);
        cards.AddHistory("Cut out by Verbinal from a file whose name is long enough that it cannot fit on one card at all, café.fits");

        Assert.True(cards.Cards.Count >= 2);
        Assert.All(cards.Cards, c => { Assert.StartsWith("HISTORY ", c); Assert.Equal(80, c.Length); });
        Assert.Contains("caf?.fits", string.Concat(cards.Cards));
    }

    [Fact]
    public void TheHeader_EndsAndFillsWholeBlocks()
    {
        var cards = new FitsHeaderCards(Enumerable.Range(0, 40).Select(i => Card($"KEY{i,-5}= {i,20}")));

        var bytes = cards.ToBytes();

        Assert.Equal(2 * 2880, bytes.Length);
        Assert.Equal("END", Encoding.ASCII.GetString(bytes, 40 * 80, 3));
    }
}
