using Xunit;
using CanfarDesktop.Models.Fits;
using static CanfarDesktop.Tests.Helpers.SyntheticFits;

namespace CanfarDesktop.Tests.Models.Fits;

/// <summary>A cube's spectral axis, read as the wavelengths a cutout's band is given in.</summary>
public class SpectralAxisTests
{
    private const double C = 299_792_458.0;

    private static FitsHeader Header(params string[] cards)
    {
        var header = new FitsHeader();
        foreach (var card in cards.Concat([Card("NAXIS", 3L), Card("NAXIS1", 10L), Card("NAXIS2", 10L), Card("NAXIS3", 100L)]))
        {
            var parsed = CanfarDesktop.Services.Fits.FitsParser.ParseCard(System.Text.Encoding.ASCII.GetBytes(card));
            header.Add(parsed);
        }
        return header;
    }

    [Fact]
    public void AWavelengthAxis_InMicrons_IsReadInMetres()
    {
        var axis = SpectralAxis.Find(Header(Text("CTYPE3", "WAVE"), Text("CUNIT3", "um"), Card("CRPIX3", 1.0), Card("CRVAL3", 2.0), Card("CDELT3", 0.01)))!;

        Assert.Equal(2.0e-6, axis.WavelengthAt(1)!.Value, 15);
        Assert.Equal(2.99e-6, axis.WavelengthAt(100)!.Value, 15);
        Assert.Equal((1.995e-6, 2.995e-6), (Math.Round(axis.Range!.Value.Min, 15), Math.Round(axis.Range.Value.Max, 15)));
    }

    /// <summary>A frequency axis runs the other way in wavelength: the band finds its planes all the same.</summary>
    [Fact]
    public void AFrequencyAxis_FindsThePlanesOfABandInWavelength()
    {
        var axis = SpectralAxis.Find(Header(Text("CTYPE3", "FREQ"), Card("CRPIX3", 1.0), Card("CRVAL3", 345.0e9), Card("CDELT3", 1.0e8)))!;

        // Planes 11–20 (1-based) are centred on 346.0–346.9 GHz, and together span 345.95–346.95 GHz:
        // exactly that band takes them and not their neighbours, whose edges it only touches.
        var planes = axis.PlanesWithin(C / 346.95e9, C / 345.95e9);

        Assert.Equal((10L, 10L), planes);
    }

    [Fact]
    public void ABandNarrowerThanAChannel_StillHasTheChannelItFallsIn()
    {
        var axis = SpectralAxis.Find(Header(Text("CTYPE3", "WAVE"), Card("CRPIX3", 1.0), Card("CRVAL3", 5e-7), Card("CDELT3", 1e-9)))!;

        Assert.Equal((4L, 1L), axis.PlanesWithin(5.041e-7, 5.042e-7)); // inside plane 5: 503.5–504.5 nm
        Assert.Equal((4L, 2L), axis.PlanesWithin(5.041e-7, 5.052e-7)); // into plane 6 too
        Assert.Null(axis.PlanesWithin(6e-7, 7e-7));                      // past the cube: it ends at 599.5 nm
    }

    /// <summary>A radio-velocity axis is a wavelength only through its line's rest frequency.</summary>
    [Fact]
    public void ARadioVelocityAxis_UsesItsRestFrequency_AndIsNotGuessedWithout()
    {
        string[] velocity = [Text("CTYPE3", "VRAD"), Text("CUNIT3", "km/s"), Card("CRPIX3", 51.0), Card("CRVAL3", 0.0), Card("CDELT3", 1.0)];

        var axis = SpectralAxis.Find(Header([.. velocity, Card("RESTFRQ", 345.79599e9)]))!;
        Assert.Equal(C / 345.79599e9, axis.WavelengthAt(51)!.Value, 15);
        Assert.True(axis.WavelengthAt(61) > axis.WavelengthAt(51)); // receding: redder

        Assert.Null(SpectralAxis.Find(Header(velocity)));
    }

    [Fact]
    public void ANonLinearAxis_IsNotTakenForALinearOne()
        => Assert.Null(SpectralAxis.Find(Header(Text("CTYPE3", "WAVE-F2W"), Card("CRPIX3", 1.0), Card("CRVAL3", 5e-7), Card("CDELT3", 1e-9))));

    [Fact]
    public void ALogarithmicAxis_IsReadAsOne()
    {
        var axis = SpectralAxis.Find(Header(Text("CTYPE3", "WAVE-LOG"), Card("CRPIX3", 1.0), Card("CRVAL3", 5e-7), Card("CDELT3", 5e-10)))!;

        Assert.Equal(5e-7 * Math.Exp(5e-10 * 99 / 5e-7), axis.WavelengthAt(100)!.Value, 15);
    }

    [Theory]
    [InlineData("FREQ", true)]
    [InlineData("VELO-LSR", true)]
    [InlineData("VELOCITY", true)]
    [InlineData("STOKES", false)]
    [InlineData("RA---TAN", false)]
    public void SpectralTypes_AreTheOnesPaperThreeNames(string ctype, bool spectral)
        => Assert.Equal(spectral, SpectralAxis.IsSpectralType(ctype));
}
