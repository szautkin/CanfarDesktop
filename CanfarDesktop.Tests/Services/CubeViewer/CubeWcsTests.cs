using Xunit;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// Golden-correctness tier for the cube spectral axis (SCI-3). These lock the "trust the number"
/// conversions a kinematics user depends on: the channel→world mapping, the display-unit conversion
/// that must honor CUNIT3 (never double-convert a cube already stored in GHz/km/s/µm), and the
/// SCI-3 convention parsing (rest frequency, spectral frame, beam). All pure + deterministic.
/// </summary>
public class CubeWcsTests
{
    private static FitsHeader Header(params (string key, string val)[] cards)
    {
        var h = new FitsHeader();
        foreach (var (key, val) in cards) h.Add(new FitsCard(key, val, ""));
        return h;
    }

    // ── Channel → world value (CRVAL3 + (chan+1 − CRPIX3)·CDELT3) ──

    [Fact]
    public void SpectralValue_LinearAxis_MatchesFitsConvention()
    {
        // CRPIX3=1 (1-based ref pixel), CRVAL3=1.4e9 Hz, CDELT3=1e6 Hz/chan.
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", "FREQ"), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1400000000"), ("CDELT3", "1000000")), 4, 4, 10);

        Assert.True(w.HasSpectral);
        Assert.Equal(1.400e9, w.SpectralValue(0), 1);   // channel 0 = ref pixel
        Assert.Equal(1.401e9, w.SpectralValue(1), 1);   // +1 channel = +CDELT3
        Assert.Equal(1.409e9, w.SpectralValue(9), 1);
    }

    // ── Display-unit conversion honoring CUNIT3 (the never-double-convert guard) ──

    [Theory]
    // FREQ: Hz → GHz, but a cube already in GHz/MHz must scale correctly (not be re-divided by 1e9).
    [InlineData("FREQ", "Hz", "1400000000", "1.4")]
    [InlineData("FREQ", "GHz", "1.4", "1.4")]
    [InlineData("FREQ", "MHz", "1400", "1.4")]
    // VELOCITY: m/s → km/s, but a km/s cube passes through.
    [InlineData("VRAD", "m/s", "200000", "200")]
    [InlineData("VRAD", "km/s", "200", "200")]
    // WAVELENGTH: nm/Å/m → µm, µm passes through.
    [InlineData("WAVE", "nm", "500", "0.5")]
    [InlineData("WAVE", "Angstrom", "5000", "0.5")]
    [InlineData("WAVE", "um", "0.5", "0.5")]
    public void SpecText_ConvertsToDisplayUnit_HonoringCunit3(string ctype, string cunit, string crval, string expected)
    {
        // Channel 0 with CRPIX3=1 ⇒ the displayed value is exactly CRVAL3 in display units.
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", ctype), ("CUNIT3", cunit), ("CRPIX3", "1"),
                   ("CRVAL3", crval), ("CDELT3", "1")), 4, 4, 8);

        Assert.Equal(expected, w.SpecText(0));
    }

    [Theory]
    [InlineData("FREQ", "FREQUENCY", "GHz")]
    [InlineData("VRAD", "VELOCITY", "km/s")]
    [InlineData("VOPT", "VELOCITY", "km/s")]
    [InlineData("WAVE", "WAVELENGTH", "µm")]
    public void SpecAxisName_AndUnit_FollowCtype3(string ctype, string axis, string unit)
    {
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", ctype), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1000000000"), ("CDELT3", "1000000")), 4, 4, 8);

        Assert.Equal(axis, w.SpecAxisName());
        Assert.Equal(unit, w.SpecUnitDisplay());
    }

    // ── SCI-3 convention parsing (surfaced, not converted) ──

    [Fact]
    public void FromHeader_ParsesSpectralConventionsAndBeam()
    {
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", "FREQ"), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1420405750"), ("CDELT3", "1000000"),
                   ("RESTFRQ", "1420405751.786"), ("SPECSYS", "LSRK"), ("SSYSOBS", "TOPOCENT"),
                   ("BMAJ", "0.001"), ("BMIN", "0.0008"), ("BPA", "30")), 4, 4, 10);

        Assert.NotNull(w.RestFrequencyHz);
        Assert.Equal(1.420405751786, w.RestFrequencyGHz!.Value, 6);
        Assert.Equal("LSRK", w.SpectralFrame);
        Assert.Equal("TOPOCENT", w.ObserverFrame);
        Assert.Equal(0.001, w.BeamMajorDeg!.Value, 6);
        Assert.Equal(0.0008, w.BeamMinorDeg!.Value, 6);
        Assert.Equal(30.0, w.BeamPaDeg!.Value, 6);
    }

    [Fact]
    public void FromHeader_MissingConventions_AreNull()
    {
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", "FREQ"), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1000000000"), ("CDELT3", "1000000")), 4, 4, 10);

        Assert.Null(w.RestFrequencyHz);
        Assert.Null(w.RestFrequencyGHz);
        Assert.Null(w.BeamMajorDeg);
        Assert.Equal("", w.SpectralFrame);
    }

    [Fact]
    public void FromHeader_Cd3_3_FallsBackForCdelt3()
    {
        // Some cubes carry the spectral increment as CD3_3 rather than CDELT3.
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", "FREQ"), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1000000000"), ("CD3_3", "2000000")), 4, 4, 10);

        Assert.True(w.HasSpectral);
        Assert.Equal(1.002e9, w.SpectralValue(1), 1); // +1 channel ⇒ +CD3_3
    }

    [Fact]
    public void HasSpectral_False_WhenSingleChannel()
    {
        var w = CubeWcs.FromHeader(
            Header(("CTYPE3", "FREQ"), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1000000000"), ("CDELT3", "1000000")), 4, 4, 1);

        Assert.False(w.HasSpectral);
        Assert.Equal("CHANNEL", w.SpecAxisName());
        Assert.Equal("CH 0", w.SpecText(0));
    }

    [Fact]
    public void FromHeader_GalacticAxis_Detected()
    {
        var w = CubeWcs.FromHeader(
            Header(("CTYPE1", "GLON-CAR"), ("CTYPE3", "FREQ"), ("CUNIT3", "Hz"), ("CRPIX3", "1"),
                   ("CRVAL3", "1000000000"), ("CDELT3", "1000000")), 4, 4, 10);

        Assert.True(w.Galactic);
        Assert.Equal("GLON", w.LonName);
        Assert.Equal("GLAT", w.LatName);
    }

    // ── SkyTextAt (the slice hover readout's exact-pixel sky formatting) ──

    /// <summary>An equatorial TAN WCS with the reference pixel at FITS (1,1).</summary>
    private static CubeWcs EquatorialWcs(double crval1, double crval2) => new()
    {
        Nx = 100, Ny = 100, Nz = 10,
        Spatial = new WcsInfo
        {
            CrPix1 = 1, CrPix2 = 1, CrVal1 = crval1, CrVal2 = crval2,
            Cd1_1 = -0.001, Cd2_2 = 0.001,
            CType1 = "RA---TAN", CType2 = "DEC--TAN",
        },
    };

    [Fact]
    public void SkyTextAt_ReferencePixel_FormatsCrval()
    {
        // 0-based (0,0) is FITS pixel (1,1) = the reference pixel, so the readout is CRVAL exactly.
        var sky = EquatorialWcs(180.0, -30.0).SkyTextAt(0, 0);

        Assert.NotNull(sky);
        Assert.Equal("12:00:00", sky!.Value.Lon);   // 180° = 12h
        Assert.Equal("−30:00:00", sky.Value.Lat);
    }

    [Fact]
    public void SkyTextAt_OffsetPixel_FollowsTheCdMatrix()
    {
        // +10 px in X at Dec 0 is −0.01° of RA = −2.4s of time: 12:00:00 → 11:59:58 (rounded).
        var sky = EquatorialWcs(180.0, 0.0).SkyTextAt(10, 0);

        Assert.NotNull(sky);
        Assert.Equal("11:59:58", sky!.Value.Lon);
    }

    [Fact]
    public void SkyTextAt_GalacticLongitude_WrapsInto0To360()
    {
        // Linear (CAR) galactic WCS with a negative CRVAL1: the readout must fold into [0,360).
        var w = new CubeWcs
        {
            Nx = 100, Ny = 100, Nz = 10,
            Galactic = true,
            Spatial = new WcsInfo
            {
                CrPix1 = 1, CrPix2 = 1, CrVal1 = -10.0, CrVal2 = 5.0,
                Cd1_1 = 1.0, Cd2_2 = 1.0,
                CType1 = "GLON-CAR", CType2 = "GLAT-CAR",
            },
        };

        var sky = w.SkyTextAt(0, 0);

        Assert.NotNull(sky);
        Assert.Equal("350.000°", sky!.Value.Lon);
        Assert.Equal("5.000°", sky.Value.Lat);
    }

    [Fact]
    public void SkyTextAt_NoSpatialWcs_ReturnsNull()
    {
        var w = new CubeWcs { Nx = 4, Ny = 4, Nz = 4 }; // default WcsInfo is invalid (all-zero CD)

        Assert.Null(w.SkyTextAt(1, 1));
    }
}
