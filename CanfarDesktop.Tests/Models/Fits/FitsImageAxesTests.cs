using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Models.Fits;

/// <summary>
/// A tile-compressed HDU's shape, as opposed to the shape of the thing it is stored in.
///
/// <para>The FITS tile-compression convention keeps the image inside a BINARY TABLE, so NAXISn
/// describes the table and ZNAXISn the picture. The HDU list asked for NAXIS and showed every
/// extension of a .fits.fz as "8×4644" — 8 being the width of a table row in bytes. The header of the
/// file this was found on says so in as many words: NAXIS1 "width of table in bytes" = 8, ZNAXIS1
/// "length of original image axis" = 2112.</para>
/// </summary>
public class FitsImageAxesTests
{
    private static FitsHeader Header(params (string Key, string Value)[] cards)
    {
        var header = new FitsHeader();
        foreach (var (key, value) in cards) header.Add(new FitsCard(key, value, ""));
        return header;
    }

    /// <summary>The real card values from the MegaCam frame this was reported on.</summary>
    private static FitsHeader Compressed() => Header(
        ("XTENSION", "BINTABLE"), ("NAXIS", "2"), ("NAXIS1", "8"), ("NAXIS2", "4644"),
        ("ZIMAGE", "T"), ("ZNAXIS", "2"), ("ZNAXIS1", "2112"), ("ZNAXIS2", "4644"));

    private static FitsHeader Plain() => Header(
        ("NAXIS", "2"), ("NAXIS1", "1024"), ("NAXIS2", "512"));

    [Fact]
    public void ACompressedHduReportsThePicturesShapeNotTheTables()
    {
        var header = Compressed();

        Assert.True(header.IsTileCompressed);
        Assert.Equal(2112, header.ImageAxis(1));
        Assert.Equal(4644, header.ImageAxis(2));
        Assert.Equal(2, header.ImageAxes);

        // The table's own shape is still there, and still means what it meant.
        Assert.Equal(8, header.NAxis1);
    }

    [Fact]
    public void AnUncompressedHduIsUnaffected()
    {
        var header = Plain();

        Assert.False(header.IsTileCompressed);
        Assert.Equal(1024, header.ImageAxis(1));
        Assert.Equal(512, header.ImageAxis(2));
        Assert.Equal(header.NAxis1, header.ImageAxis(1));
    }

    /// <summary>A third axis follows the same rule — a compressed cube is stored the same way.</summary>
    [Fact]
    public void AThirdAxisComesFromTheSamePlace()
    {
        var cube = Header(
            ("ZIMAGE", "T"), ("ZNAXIS", "3"), ("ZNAXIS1", "64"), ("ZNAXIS2", "64"), ("ZNAXIS3", "128"),
            ("NAXIS", "2"), ("NAXIS1", "8"), ("NAXIS2", "8192"));

        Assert.Equal(128, cube.ImageAxis(3));
        Assert.Equal(3, cube.ImageAxes);
    }

    /// <summary>An axis the header does not have is zero, not a guess.</summary>
    [Fact]
    public void AnAbsentAxisIsZero()
    {
        Assert.Equal(0, Plain().ImageAxis(3));
        Assert.Equal(0, Compressed().ImageAxis(3));
    }

    /// <summary>
    /// ZIMAGE is a FITS logical, which real files write quoted and padded. Anything but a leading T
    /// means this is an ordinary table and its NAXIS is the answer.
    /// </summary>
    [Theory]
    [InlineData("T", true)]
    [InlineData("'T'", true)]
    [InlineData("T       ", true)]
    [InlineData("F", false)]
    [InlineData("", false)]
    public void TheCompressionFlagIsReadTheWayFilesWriteIt(string value, bool compressed)
        => Assert.Equal(compressed, Header(("ZIMAGE", value), ("ZNAXIS1", "2112"), ("NAXIS1", "8")).IsTileCompressed);

    /// <summary>
    /// And the HDU's own "is there a picture here" answers from the same place, so a compressed
    /// extension is judged on its image rather than on the table holding it.
    /// </summary>
    [Fact]
    public void AnHduKnowsItHasAnImageFromThePicturesAxes()
    {
        Assert.True(new FitsHdu { Index = 1, Header = Compressed() }.HasImage);
        Assert.True(new FitsHdu { Index = 0, Header = Plain() }.HasImage);
    }
}
