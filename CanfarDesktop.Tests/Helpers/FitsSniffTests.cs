using System.Text;
using CanfarDesktop.Helpers;
using Xunit;

namespace CanfarDesktop.Tests.Helpers;

public class FitsSniffTests
{
    private static byte[] Header(params string[] cards)
    {
        var sb = new StringBuilder();
        foreach (var c in cards) sb.Append(c.PadRight(80));
        sb.Append("END".PadRight(80));
        while (sb.Length % 2880 != 0) sb.Append(' ');
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static bool Sniff(params byte[][] blocks)
    {
        using var ms = new MemoryStream();
        foreach (var b in blocks) ms.Write(b);
        ms.Position = 0;
        return FitsSniff.IsLikelyCube(ms);
    }

    [Fact]
    public void TwoDImage_IsNotCube()
        => Assert.False(Sniff(Header(
            "SIMPLE  =                    T",
            "BITPIX  =                   16",
            "NAXIS   =                    2",
            "NAXIS1  =                 2048",
            "NAXIS2  =                 2048")));

    [Fact]
    public void RealCube_IsCube()
        => Assert.True(Sniff(Header(
            "SIMPLE  =                    T",
            "BITPIX  =                  -32",
            "NAXIS   =                    3",
            "NAXIS1  =                  512",
            "NAXIS2  =                  512",
            "NAXIS3  =                  118")));

    [Fact]
    public void DegenerateThirdAxis_IsNotCube()
        => Assert.False(Sniff(Header(
            "SIMPLE  =                    T",
            "NAXIS   =                    3",
            "NAXIS1  =                 2048",
            "NAXIS2  =                 2048",
            "NAXIS3  =                    1"))); // imagers often write NAXIS3=1 — still 2D

    [Fact]
    public void DatalessPrimary_FollowsToExtensionCube()
        => Assert.True(Sniff(
            Header("SIMPLE  =                    T", "NAXIS   =                    0", "EXTEND  =                    T"),
            Header("XTENSION= 'IMAGE   '", "NAXIS   =                    3",
                   "NAXIS1  =                  300", "NAXIS2  =                  300", "NAXIS3  =                   40")));

    [Fact]
    public void GarbageAndMissingFiles_AreSafelyNotCubes()
    {
        using var junk = new MemoryStream(Encoding.ASCII.GetBytes("this is not a FITS file"));
        Assert.False(FitsSniff.IsLikelyCube(junk));
        Assert.False(FitsSniff.IsLikelyCube(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".fits")));
    }
}
