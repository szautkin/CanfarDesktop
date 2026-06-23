using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class ADQLSpatialTests
{
    [Fact]
    public void DecimalCoordinatePair_ProducesCircleCone()
    {
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "10.68 41.27" });
        Assert.Contains("INTERSECTS( CIRCLE('ICRS', 10.68, 41.27,", adql);
    }

    [Fact]
    public void SexagesimalCoordinatePair_ProducesCircleCone()
    {
        // 10:42:44 h = 160.683°, +41:16:09 = 41.269°
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "10:42:44 +41:16:09" });
        Assert.Contains("CIRCLE('ICRS', 160.68", adql);
        Assert.Contains("41.26", adql);
    }

    [Fact]
    public void CoordinatePair_WithArcminRadius_ConvertsToDegrees()
    {
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "10.0 20.0 30arcmin" });
        Assert.Contains("CIRCLE('ICRS', 10, 20, 0.5)", adql); // 30' = 0.5°
    }

    [Fact]
    public void CoordinatePair_WithArcsecRadius_ConvertsToDegrees()
    {
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "10 20 36arcsec" });
        Assert.Contains("CIRCLE('ICRS', 10, 20, 0.01)", adql); // 36\" = 0.01°
    }

    [Fact]
    public void CoordinateRange_ProducesRangeS2D()
    {
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "10..12 20..22" });
        Assert.Contains("RANGE_S2D(10, 12, 20, 22)", adql);
    }

    [Fact]
    public void PlainName_StillUsesLike()
    {
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "M31" });
        Assert.Contains("lower(Observation.target_name) LIKE '%m31%'", adql);
    }

    [Fact]
    public void ResolvedCoords_WithNameTarget_UsesResolvedCircle()
    {
        var adql = ADQLBuilder.Build(new SearchFormState
        {
            Target = "M31",
            ResolvedRA = 10.684,
            ResolvedDec = 41.27,
            SearchRadius = 0.1,
        });
        Assert.Contains("CIRCLE('ICRS', 10.684, 41.27, 0.1)", adql);
    }

    [Fact]
    public void OutOfRangeNumbers_FallThroughToName()
    {
        // Dec 200 is out of [-90, 90] → not a coordinate pair → name match.
        var adql = ADQLBuilder.Build(new SearchFormState { Target = "500 200" });
        Assert.Contains("lower(Observation.target_name) LIKE", adql);
        Assert.DoesNotContain("CIRCLE", adql);
    }

    [Fact]
    public void TryParseCoordinatePair_DecimalAndSexagesimal()
    {
        Assert.True(ADQLBuilder.TryParseCoordinatePair("10.5 20.5", 0.01, out var ra, out var dec, out var r));
        Assert.Equal(10.5, ra, 6);
        Assert.Equal(20.5, dec, 6);
        Assert.Equal(0.01, r, 6); // default radius preserved

        Assert.True(ADQLBuilder.TryParseCoordinatePair("00:00:00 +00:00:00", 0.01, out ra, out dec, out _));
        Assert.Equal(0, ra, 6);
        Assert.Equal(0, dec, 6);

        Assert.False(ADQLBuilder.TryParseCoordinatePair("M31", 0.01, out _, out _, out _));
    }
}
