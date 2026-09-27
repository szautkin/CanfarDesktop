using System.Text;
using Xunit;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>
/// The FITS checksum convention, checked against a file astropy wrote and verified itself — another
/// implementation entirely — and against the convention's own definition: an HDU with its CHECKSUM sums
/// to negative zero.
/// </summary>
public class FitsChecksumTests
{
    private static byte[] Vector() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fits", "checksum-astropy.fits"));

    [Fact]
    public void TheDataSum_IsAstropys()
    {
        var bytes = Vector();
        using var stream = new MemoryStream(bytes);
        var hdu = FitsLayout.Read(stream).Single();

        var dataSum = FitsChecksum.Sum(bytes.AsSpan((int)hdu.DataStart, (int)FitsParser.AlignToBlock(hdu.DataBytes)));

        Assert.Equal(hdu.Header.GetString("DATASUM"), dataSum.ToString());
    }

    /// <summary>
    /// The CHECKSUM astropy wrote is the one this computes from astropy's own header bytes — its value
    /// set to zeros in place, nothing else touched.
    /// </summary>
    [Fact]
    public void TheChecksum_IsAstropys()
    {
        var bytes = Vector();
        using var stream = new MemoryStream(bytes);
        var hdu = FitsLayout.Read(stream).Single();
        var expected = hdu.Header.GetString("CHECKSUM")!;

        var header = bytes[..(int)hdu.DataStart];
        var text = Encoding.ASCII.GetString(header);
        var at = text.IndexOf("CHECKSUM= '", StringComparison.Ordinal) + "CHECKSUM= '".Length;
        Encoding.ASCII.GetBytes(FitsChecksum.Zero).CopyTo(header, at);
        var dataSum = FitsChecksum.Sum(bytes.AsSpan((int)hdu.DataStart));

        Assert.Equal(expected, FitsChecksum.Encode(~FitsChecksum.Sum(header, dataSum)));
    }

    [Fact]
    public void AstropysFile_SumsToNegativeZero()
        => Assert.Equal(0xFFFFFFFFu, FitsChecksum.Sum(Vector()));

    /// <summary>A header sealed here makes its HDU sum to negative zero, and says its data's sum.</summary>
    [Fact]
    public void ASealedHeader_MakesItsHduSumToNegativeZero()
    {
        var data = new byte[2880];
        new Random(7).NextBytes(data.AsSpan(0, 1999)); // the rest is the zero padding
        var cards = new FitsHeaderCards([
            "SIMPLE  =                    T".PadRight(80), "BITPIX  =                    8".PadRight(80),
            "NAXIS   =                    1".PadRight(80), "NAXIS1  =                 1999".PadRight(80)]);
        FitsChecksum.Reserve(cards);
        var reserved = cards.ToBytes().Length;

        var header = FitsChecksum.Seal(cards, FitsChecksum.Sum(data));

        Assert.Equal(reserved, header.Length);
        Assert.Equal(0xFFFFFFFFu, FitsChecksum.Add(FitsChecksum.Sum(header), FitsChecksum.Sum(data)));
        Assert.Equal(FitsChecksum.Sum(data).ToString(), cards.Parse().GetString("DATASUM"));
    }

    /// <summary>A cutout's rows are summed as they stream out, whatever their length: pieces sum as the whole does.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(2880)]
    public void SummedInPieces_IsSummedWhole(int piece)
    {
        var bytes = new byte[5000];
        new Random(11).NextBytes(bytes);
        var sum = new FitsChecksum();
        for (var i = 0; i < bytes.Length; i += piece) sum.Add(bytes.AsSpan(i, Math.Min(piece, bytes.Length - i)));

        var padded = new byte[5760];
        bytes.CopyTo(padded, 0);
        Assert.Equal(FitsChecksum.Sum(padded), sum.Value);
    }

    /// <summary>The encoding is printable, never punctuation, sixteen characters.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x3A3B5B60u)]
    public void TheEncoding_IsSixteenCharactersClearOfPunctuation(uint value)
        => Assert.All(FitsChecksum.Encode(value), c => Assert.True(char.IsAsciiLetterOrDigit(c), $"'{c}'"));
}
