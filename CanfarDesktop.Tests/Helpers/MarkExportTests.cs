using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Marks written for somebody else to use.
///
/// <para>Inside the app a position is enough, because the file is right there. Written to disk it is
/// a number nobody can check — so the export carries where the mark is, what it is of, and how to
/// fetch that thing again.</para>
///
/// <para>The property worth holding down is that a reader should never have to redo the WCS: every
/// mark comes out with BOTH a sky position and a pixel one, and its size in degrees, arcseconds and
/// pixels, whichever it happened to be drawn in.</para>
/// </summary>
public class MarkExportTests
{
    private const int Width = 2112, Height = 4644;

    /// <summary>A tangent plane at half an arcsecond per pixel, north up.</summary>
    private static WcsInfo Wcs() => new()
    {
        CrPix1 = Width / 2.0, CrPix2 = Height / 2.0,
        CrVal1 = 240.0, CrVal2 = 48.0,
        Cd1_1 = -0.5 / 3600, Cd1_2 = 0, Cd2_1 = 0, Cd2_2 = 0.5 / 3600,
        CType1 = "RA---TAN", CType2 = "DEC--TAN",
    };

    private static MarkExport.Source Source(WcsInfo? wcs) => new(
        LocalPath: @"C:\data\1832496o.fits.fz", FileName: "1832496o.fits.fz",
        HduIndex: 1, HduName: "ccd00", Width: Width, Height: Height, Wcs: wcs);

    private static Annotation Mark(AnnotationAnchor anchor, double? half, string text = "a source") => new()
    {
        Id = "m1",
        Kind = AnnotationKind.Circle,
        Anchor = anchor,
        Extent = half is { } h ? Extent.Square(h) : null,
        Text = text,
        Author = MarkAuthor.User,
        CreatedAt = "2026-09-20T12:00:00Z",
    };

    private static MarkExport.Document Build(params Annotation[] marks)
        => MarkExport.Build(marks, Source(Wcs()), null, "1.4.0", new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));

    // ── Both ways round ─────────────────────────────────────────────────────────────────────────

    /// <summary>A sky mark still comes out with the pixel it sits on.</summary>
    [Fact]
    public void ASkyMarkAlsoCarriesItsPixel()
    {
        var exported = Build(Mark(AnnotationAnchor.Sky(240.0, 48.0), 0.01)).Marks[0];

        Assert.Equal(240.0, exported.RaDeg!.Value, 6);
        Assert.NotNull(exported.PixelX);
        Assert.NotNull(exported.PixelY);
        Assert.Equal("sky", exported.AnchoredIn);
    }

    /// <summary>And a pixel mark comes out with the sky position it points at.</summary>
    [Fact]
    public void APixelMarkAlsoCarriesItsSkyPosition()
    {
        var exported = Build(Mark(AnnotationAnchor.ImagePixel(1056, 2322), 60)).Marks[0];

        Assert.Equal(1056, exported.PixelX!.Value, 6);
        Assert.NotNull(exported.RaDeg);
        Assert.NotNull(exported.DecDeg);
        Assert.Equal("imagepixel", exported.AnchoredIn);
    }

    /// <summary>The two directions agree: a pixel mark's sky position maps back to that pixel.</summary>
    [Fact]
    public void ThePositionsAgreeWithEachOther()
    {
        var fromPixel = Build(Mark(AnnotationAnchor.ImagePixel(700, 1900), 10)).Marks[0];
        var backAgain = Build(Mark(AnnotationAnchor.Sky(fromPixel.RaDeg!.Value, fromPixel.DecDeg!.Value), 0.01)).Marks[0];

        Assert.Equal(700, backAgain.PixelX!.Value, 3);
        Assert.Equal(1900, backAgain.PixelY!.Value, 3);
    }

    /// <summary>Sexagesimal too, because that is how a position gets read out loud.</summary>
    [Fact]
    public void ItWritesTheSexagesimalFormsAsWell()
    {
        var exported = Build(Mark(AnnotationAnchor.Sky(240.0, 48.0), 0.01)).Marks[0];

        Assert.False(string.IsNullOrWhiteSpace(exported.Ra));
        Assert.False(string.IsNullOrWhiteSpace(exported.Dec));
    }

    // ── Sizes in every unit ─────────────────────────────────────────────────────────────────────

    /// <summary>A pixel-sized mark: 60 px at half an arcsecond each is 30 arcseconds.</summary>
    [Fact]
    public void APixelSizeIsAlsoGivenInArcsecondsAndDegrees()
    {
        var exported = Build(Mark(AnnotationAnchor.ImagePixel(1056, 2322), 60)).Marks[0];

        Assert.Equal(60, exported.HalfWidthPixels!.Value, 6);
        Assert.Equal(30, exported.HalfWidthArcsec!.Value, 6);
        Assert.Equal(30.0 / 3600, exported.HalfWidthDeg!.Value, 9);
    }

    /// <summary>And a sky-sized one the other way: 0.01 degrees is 36 arcseconds is 72 pixels.</summary>
    [Fact]
    public void ASkySizeIsAlsoGivenInPixels()
    {
        var exported = Build(Mark(AnnotationAnchor.Sky(240.0, 48.0), 0.01)).Marks[0];

        Assert.Equal(0.01, exported.HalfWidthDeg!.Value, 9);
        Assert.Equal(36, exported.HalfWidthArcsec!.Value, 6);
        Assert.Equal(72, exported.HalfWidthPixels!.Value, 6);
    }

    /// <summary>A mark with no size says so rather than reporting zero.</summary>
    [Fact]
    public void AMarkWithNoSizeHasNoSizeFields()
    {
        var exported = Build(Mark(AnnotationAnchor.Sky(240.0, 48.0), half: null)).Marks[0];

        Assert.Null(exported.HalfWidthDeg);
        Assert.Null(exported.HalfWidthPixels);
    }

    // ── No WCS ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An image with no WCS has pixels and no sky. Inventing one would be worse than leaving the
    /// field out, so the sky fields are null and the pixel ones are still right.
    /// </summary>
    [Fact]
    public void WithoutAWcsThereIsNoSkyPositionAndThatIsSaid()
    {
        var document = MarkExport.Build(
            [Mark(AnnotationAnchor.ImagePixel(100, 200), 10)],
            Source(wcs: null), null, "1.4.0", DateTime.UtcNow);

        var exported = document.Marks[0];
        Assert.Null(exported.RaDeg);
        Assert.Null(exported.Ra);
        Assert.Equal(100, exported.PixelX!.Value, 6);
        Assert.Null(exported.HalfWidthArcsec);
    }

    // ── The document ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheDocumentSaysWhatItIsAndWhatItIsOf()
    {
        var document = Build(Mark(AnnotationAnchor.Sky(240.0, 48.0), 0.01));

        Assert.Equal(MarkExport.Schema, document.Schema);
        Assert.Equal("1832496o.fits.fz", document.Source.FileName);
        Assert.Equal("ccd00", document.Source.HduName);
        Assert.Equal(1, document.Source.HduIndex);
    }

    /// <summary>
    /// A file opened off disk has no observation behind it. That is stated rather than omitted:
    /// "we do not know where this came from" is information.
    /// </summary>
    [Fact]
    public void AFileWithNoObservationSaysSo()
        => Assert.Null(Build(Mark(AnnotationAnchor.Sky(240.0, 48.0), 0.01)).Observation);

    [Fact]
    public void AnObservationIsCarriedThroughWhenThereIsOne()
    {
        var provenance = new MarkExport.Provenance(
            "ivo://cadc.nrc.ca/CFHT?1832496/1832496o", "CFHT", "1832496", "P177D-22",
            "MegaPrime", "g.MP9402", "2015-09-12", "1", "2015-09-12",
            "15BQ97", "QSO Team", "A survey", "2026-09-19T00:00:00Z", null, null);

        var document = MarkExport.Build(
            [Mark(AnnotationAnchor.Sky(240.0, 48.0), 0.01)],
            Source(Wcs()), provenance, "1.4.0", DateTime.UtcNow);

        Assert.Equal("ivo://cadc.nrc.ca/CFHT?1832496/1832496o", document.Observation!.PublisherId);
        Assert.Equal("15BQ97", document.Observation.ProposalId);
    }

    /// <summary>A mark that cannot be placed is left out rather than written as nonsense.</summary>
    [Fact]
    public void AnUnplaceableMarkIsSkipped()
        => Assert.Empty(Build(Mark(AnnotationAnchor.ImagePixel(double.NaN, 0), 10)).Marks);
}
