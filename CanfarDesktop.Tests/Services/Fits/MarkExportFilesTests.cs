using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services.Fits;
using static CanfarDesktop.Tests.Helpers.SyntheticFits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>
/// The image a mark export describes when its file is not the one on screen — read from the header, as
/// every other mark tool already took a file that is not open (QA D5b).
/// </summary>
public class MarkExportFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-markexport-" + Guid.NewGuid().ToString("N")[..8]);

    public MarkExportFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>An HST-shaped file: an empty primary, then SCI (40 × 30, with sky coordinates) and WHT (20 × 10).</summary>
    private string TwoExtensions()
    {
        var sci = ImageCards(-32, primary: false, 40, 30);
        sci.Add(Text("EXTNAME", "SCI"));
        sci.AddRange(TanWcs(20.5, 15.5, 10.68, 41.27, 1e-4));
        var wht = ImageCards(-32, primary: false, 20, 10);
        wht.Add(Text("EXTNAME", "WHT"));

        return Write(_dir, "frame.fits",
            Hdu(ImageCards(8, primary: true)),
            Hdu(sci, Pixels(-32, 40, 30, (_, _, _) => 1)),
            Hdu(wht, Pixels(-32, 20, 10, (_, _, _) => 1)));
    }

    [Fact]
    public void TheExtensionTheMarksAreOn_IsDescribed()
    {
        var path = TwoExtensions();

        var (source, why) = MarkExportFiles.Read(MarkTarget.Key(path, 2));

        Assert.Null(why);
        Assert.Equal((2, "WHT", 20, 10), (source!.HduIndex, source.HduName!.Trim(), source.Width, source.Height));
    }

    [Fact]
    public void ItsSkyCoordinates_AreTheHeaders()
    {
        var (source, _) = MarkExportFiles.Read(MarkTarget.Key(TwoExtensions(), 1));

        Assert.True(source!.Wcs!.IsValid);
        var (ra, dec) = source.Wcs.PixelToWorld(20.5, 15.5);
        Assert.Equal(10.68, ra, 9);
        Assert.Equal(41.27, dec, 9);
    }

    [Fact]
    public void ABarePath_IsTheFilesFirstImage_WhereItsMarksLand()
    {
        var (source, _) = MarkExportFiles.Read(TwoExtensions());

        Assert.Equal(1, source!.HduIndex);
        Assert.Equal(40, source.Width);
    }

    [Fact]
    public void AnExtensionItDoesNotHave_SaysSo()
    {
        var (source, why) = MarkExportFiles.Read(MarkTarget.Key(TwoExtensions(), 7));

        Assert.Null(source);
        Assert.Contains("extension 7", why);
    }

    [Fact]
    public void AFileThatIsNotThere_SaysSo()
    {
        var (source, why) = MarkExportFiles.Read(Path.Combine(_dir, "gone.fits"));

        Assert.Null(source);
        Assert.Contains("cannot be read", why);
    }
}
