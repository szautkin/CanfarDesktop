using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The same marks as a DS9 region file.
///
/// JSON carries everything and nothing reads it. A .reg file carries less and every tool in the field
/// opens it — DS9, CARTA, pyregion, whatever script somebody already has. It is the difference
/// between marks that can be cited and marks that can be used.
/// </summary>
public class Ds9RegionsTests
{
    private const int Width = 2112, Height = 4644;

    private static WcsInfo Wcs() => new()
    {
        CrPix1 = Width / 2.0, CrPix2 = Height / 2.0,
        CrVal1 = 240.0, CrVal2 = 48.0,
        Cd1_1 = -0.5 / 3600, Cd1_2 = 0, Cd2_1 = 0, Cd2_2 = 0.5 / 3600,
        CType1 = "RA---TAN", CType2 = "DEC--TAN",
    };

    private static MarkExport.Source Source(WcsInfo? wcs) => new(
        @"C:\data\m51.fits", "m51.fits", 1, "ccd00", Width, Height, wcs);

    private static Annotation Mark(
        AnnotationKind kind, AnnotationAnchor anchor, double? half, string text = "") => new()
    {
        Id = "m1", Kind = kind, Anchor = anchor,
        Extent = half is { } h ? Extent.Square(h) : null,
        Text = text, Author = MarkAuthor.User,
    };

    private static string Write(WcsInfo? wcs, params Annotation[] marks)
        => Ds9Regions.Write(MarkExport.Build(marks, Source(wcs), null, "1.4.0", DateTime.UtcNow));

    // ── The file DS9 expects ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ItStartsWithTheHeaderDs9LooksFor()
        => Assert.StartsWith("# Region file format: DS9 version 4.1",
                             Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01)));

    /// <summary>
    /// fk5 when there is a WCS, so the regions land on ANY image of the same field rather than only
    /// on this one. That is most of the point of exporting them.
    /// </summary>
    [Fact]
    public void WithAWcsTheRegionsAreOnTheSky()
    {
        var text = Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01));

        Assert.Contains("fk5", text);
        Assert.DoesNotContain("\nimage\n", text);
    }

    /// <summary>Without one, image pixels — the honest fallback, and it still opens.</summary>
    [Fact]
    public void WithoutAWcsTheRegionsAreInPixels()
    {
        var text = Write(null, Mark(AnnotationKind.Circle, AnnotationAnchor.ImagePixel(100, 200), 10));

        Assert.Contains("image", text);
        Assert.DoesNotContain("fk5", text);
    }

    /// <summary>DS9 counts image pixels from one; the app counts display pixels from zero.</summary>
    [Fact]
    public void ImagePixelsAreWrittenOneBased()
    {
        var text = Write(null, Mark(AnnotationKind.Circle, AnnotationAnchor.ImagePixel(100, 200), 10));

        Assert.Contains("circle(101,201,10)", text);
    }

    // ── Shapes ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A sky circle carries its radius in arcseconds, which is DS9's own convention.</summary>
    [Fact]
    public void ASkyCircleCarriesItsRadiusInArcseconds()
    {
        var text = Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01));

        Assert.Contains("circle(240,48,36\")", text);
    }

    /// <summary>A box takes full width and height, not half — so the half-size is doubled.</summary>
    [Fact]
    public void ARectBecomesABoxOfFullWidthAndHeight()
    {
        var text = Write(Wcs(), Mark(AnnotationKind.Rect, AnnotationAnchor.Sky(240, 48), 0.01));

        Assert.Contains("box(240,48,72\",72\",0)", text);
    }

    /// <summary>
    /// A mark with no size becomes a point rather than being dropped. A position somebody
    /// deliberately marked is the most citable thing in the file.
    /// </summary>
    [Fact]
    public void AMarkWithNoSizeBecomesAPoint()
    {
        var text = Write(Wcs(), Mark(AnnotationKind.Text, AnnotationAnchor.Sky(240, 48), half: null, "here"));

        Assert.Contains("point(240,48)", text);
    }

    // ── Labels ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ALabelIsCarriedThrough()
        => Assert.Contains("text={a quasar}",
                           Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01, "a quasar")));

    /// <summary>
    /// The braces that delimit a label cannot appear inside it. DS9 has no escape there, so a brace
    /// in someone's words would end the label early and leave the rest as broken syntax.
    /// </summary>
    [Fact]
    public void BracesInALabelCannotBreakTheFile()
    {
        var text = Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01, "a {bright} one"));

        var label = text.Split("text={")[1].Split('}')[0];
        Assert.Equal("a (bright) one", label);
    }

    /// <summary>A newline would end the region line itself.</summary>
    [Fact]
    public void ANewlineInALabelCannotSplitTheLine()
    {
        var text = Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01, "two\nlines"));

        Assert.DoesNotContain("two\nlines", text);
        Assert.Contains("text={two lines}", text);
    }

    /// <summary>One line per mark, so a reader can count them.</summary>
    [Fact]
    public void EachMarkIsOneLine()
    {
        var text = Write(Wcs(),
            Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01),
            Mark(AnnotationKind.Rect, AnnotationAnchor.Sky(240.1, 48.1), 0.01),
            Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240.2, 48.2), 0.01));

        var shapes = text.Split('\n').Count(l => l.StartsWith("circle(") || l.StartsWith("box("));
        Assert.Equal(3, shapes);
    }

    /// <summary>
    /// Numbers are written plainly. An exponent is valid in most formats and not in this one, and a
    /// tiny region is exactly where one would appear.
    /// </summary>
    [Fact]
    public void NumbersNeverComeOutInExponentForm()
    {
        var text = Write(null, Mark(AnnotationKind.Circle, AnnotationAnchor.ImagePixel(0.0000001, 0.0000002), 0.0000003));

        Assert.DoesNotContain("E-", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The file says where it came from, so a region on a desk can be traced back.</summary>
    [Fact]
    public void TheFileNamesTheImageItCameFrom()
        => Assert.Contains("m51.fits", Write(Wcs(), Mark(AnnotationKind.Circle, AnnotationAnchor.Sky(240, 48), 0.01)));
}
