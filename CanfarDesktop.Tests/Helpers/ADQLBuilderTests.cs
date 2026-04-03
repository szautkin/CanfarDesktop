using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class ADQLBuilderTests
{
    [Fact]
    public void TryParseDateToMJD_KnownDate_ReturnsCorrectValue()
    {
        // J2000.0 epoch: 2000-01-01T12:00:00 = MJD 51544.5
        Assert.True(ADQLBuilder.TryParseDateToMJD("2000-01-01T12:00:00", out var mjd));
        Assert.Equal(51544.5, mjd, 1);
    }

    [Fact]
    public void TryParseDateToMJD_UnixEpoch_ReturnsCorrectValue()
    {
        // 1970-01-01 = MJD 40587.0
        Assert.True(ADQLBuilder.TryParseDateToMJD("1970-01-01", out var mjd));
        Assert.Equal(40587.0, mjd, 1);
    }

    [Fact]
    public void TryParseDateToMJD_EmptyString_ReturnsFalse()
    {
        Assert.False(ADQLBuilder.TryParseDateToMJD("", out _));
    }

    [Fact]
    public void TryExpandDateToRange_Year_ExpandsToFullYear()
    {
        Assert.True(ADQLBuilder.TryExpandDateToRange("2020", out var lo, out var hi));
        Assert.True(hi > lo);
        // 2020 is a leap year, spans ~366 days
        Assert.Equal(366, hi - lo, 0);
    }

    [Fact]
    public void TryExpandDateToRange_YearMonth_ExpandsToFullMonth()
    {
        Assert.True(ADQLBuilder.TryExpandDateToRange("2020-02", out var lo, out var hi));
        Assert.True(hi > lo);
        // Feb 2020 has 29 days (leap year)
        Assert.Equal(29, hi - lo, 0);
    }

    [Fact]
    public void Build_EmptyState_IncludesQualityFilter()
    {
        var state = new SearchFormState();
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("quality_flag", adql);
        Assert.Contains("SELECT TOP 10000", adql);
    }

    [Fact]
    public void Build_WithTarget_GeneratesLikeClause()
    {
        var state = new SearchFormState { Target = "M31" };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("lower(Observation.target_name) LIKE '%m31%'", adql);
    }

    [Fact]
    public void Build_WithResolvedCoords_GeneratesCircle()
    {
        var state = new SearchFormState
        {
            Target = "M31",
            ResolvedRA = 10.684,
            ResolvedDec = 41.269,
            SearchRadius = 0.1
        };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("CIRCLE('ICRS'", adql);
        Assert.Contains("10.684", adql);
    }

    [Fact]
    public void Build_ObservationIdWildcard_GeneratesLikeWithPercent()
    {
        var state = new SearchFormState { ObservationId = "jw01837*" };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("LIKE 'jw01837%'", adql);
    }

    [Fact]
    public void Build_SpectralCoverageRange_GeneratesOverlapClause()
    {
        var state = new SearchFormState
        {
            SpectralCoverage = "400..700",
            SpectralCoverageUnit = "nm"
        };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("Plane.energy_bounds_lower", adql);
        Assert.Contains("Plane.energy_bounds_upper", adql);
    }

    [Fact]
    public void Build_DatePreset_GeneratesTemporalClause()
    {
        var state = new SearchFormState { DatePreset = "Last24h" };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("INTERSECTS", adql);
        Assert.Contains("Plane.time_bounds_samples", adql);
    }

    [Fact]
    public void Build_IntegrationTimeWithUnit_ConvertsToSeconds()
    {
        var state = new SearchFormState
        {
            IntegrationTimeMin = "5",
            IntegrationTimeUnit = "m" // 5 minutes = 300 seconds
        };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("Plane.time_exposure >= 300", adql);
    }

    [Fact]
    public void Build_DataTrain_GeneratesInClause()
    {
        var state = new SearchFormState
        {
            Collections = "JWST,HST"
        };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("Observation.collection IN ( 'JWST', 'HST' )", adql);
    }

    [Fact]
    public void Build_ResolvingPowerRange_GeneratesNumericClause()
    {
        var state = new SearchFormState { ResolvingPower = "1000..5000" };
        var adql = ADQLBuilder.Build(state);
        Assert.Contains("Plane.energy_resolvingPower >= 1000", adql);
        Assert.Contains("Plane.energy_resolvingPower <= 5000", adql);
    }
}
