using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The sky arithmetic a cutout needs. The observation view's footprint sketch worked in raw RA and
/// Dec, which squashes every field by cos(Dec) and breaks across RA 0°; these hold where that did not.
/// </summary>
public class SkyGeometryTests
{
    private static readonly IReadOnlyList<SkyPoint> MegaPipeFootprint =
    [
        new(11.36947807792, 40.741566852344725), new(9.984677722080006, 40.74156685234471),
        new(9.97337068479275, 41.79899479569447), new(11.380785115207203, 41.79899479569449),
    ];

    [Fact]
    public void Distance_IsTheAngleOnTheSphere()
    {
        Assert.Equal(1.0, SkyGeometry.Distance(new(0, 0), new(0, 1)), 9);
        Assert.Equal(0.5, SkyGeometry.Distance(new(0, 60), new(1, 60)), 3); // a degree of RA at Dec 60° is half a degree
    }

    /// <summary>
    /// Two points either side of RA 0° have their middle at 0°, not at 180°. (A hair north of Dec 10°:
    /// the great circle between two points of one Dec bows toward the pole.)
    /// </summary>
    [Fact]
    public void TheCentre_OfAFieldAcrossRaZero_IsAtZero()
    {
        var centre = SkyGeometry.Centroid([new(359.9, 10), new(0.1, 10)]);
        Assert.True(centre.Ra < 1e-9 || centre.Ra > 360 - 1e-9, $"RA {centre.Ra}");
        Assert.Equal(10, centre.Dec, 4);
    }

    [Fact]
    public void TheTangentPlane_TakesAPointThereAndBack()
    {
        var centre = new SkyPoint(10.68, 41.27);
        var p = new SkyPoint(11.1, 41.6);

        var (x, y) = SkyGeometry.Project(centre, p)!.Value;
        var back = SkyGeometry.Unproject(centre, x, y);

        Assert.True(x > 0 && y > 0); // east and north of the centre
        Assert.True(SkyGeometry.Distance(p, back) < 1e-10);
    }

    [Fact]
    public void ACircle_IsOutlinedAtItsRadius()
    {
        var centre = new SkyPoint(10.68, 41.27);
        Assert.All(SkyGeometry.CircleOutline(centre, 0.2), v => Assert.Equal(0.2, SkyGeometry.Distance(centre, v), 9));
    }

    /// <summary>CADC's own footprint winds negative in the plane, and is left as it is.</summary>
    [Fact]
    public void CadcsWinding_IsTheOneKept()
    {
        Assert.True(SkyGeometry.SignedArea(MegaPipeFootprint) < 0);
        Assert.Same(MegaPipeFootprint, SkyGeometry.SkyWise(MegaPipeFootprint));
        Assert.True(SkyGeometry.SignedArea(SkyGeometry.SkyWise(MegaPipeFootprint.Reverse().ToList())) < 0);
    }

    [Theory]
    [InlineData(10.68, 41.27, 0.05, SkyOverlap.Inside)]
    [InlineData(9.97, 41.8, 0.1, SkyOverlap.Partial)]
    [InlineData(20, 41, 0.1, SkyOverlap.Outside)]
    [InlineData(10.68, 41.27, 3, SkyOverlap.Partial)] // larger than the image, around it
    public void ACircle_AgainstTheFootprint(double ra, double dec, double r, SkyOverlap expected)
        => Assert.Equal(expected, SkyGeometry.Overlap(SkyGeometry.CircleOutline(new(ra, dec), r), MegaPipeFootprint));

    [Fact]
    public void Containment_IsOfThePointInTheShape()
    {
        Assert.True(SkyGeometry.Contains(MegaPipeFootprint, new(10.68, 41.27)));
        Assert.False(SkyGeometry.Contains(MegaPipeFootprint, new(12, 41.27)));
    }

    /// <summary>About 1.05° × 1.06°: RA's 1.4° span at Dec 41° is cos(41°) of that on the sky.</summary>
    [Fact]
    public void TheArea_IsOnTheSky_NotInRaAndDec()
        => Assert.InRange(SkyGeometry.Area(MegaPipeFootprint), 1.08, 1.14);
}
