using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class Caom2UriTests
{
    [Theory]
    [InlineData("ivo://cadc.nrc.ca/CFHT?22803/22803o", "caom:CFHT/22803")]   // strip productID
    [InlineData("ivo://cadc.nrc.ca/NEOSSAT?2026085000914", "caom:NEOSSAT/2026085000914")]
    [InlineData("ivo://cadc.nrc.ca/JWST/mirror?jw01147", "caom:JWST/jw01147")] // drop mirror segment
    [InlineData("caom:CFHT/22803", "caom:CFHT/22803")]                          // canonical round-trip
    [InlineData("caom:CFHT/22803/22803p", "caom:CFHT/22803")]                    // canonical strip productID
    [InlineData("  caom:JWST/jw01147  ", "caom:JWST/jw01147")]                   // whitespace tolerance
    public void ToObservationUri_Valid(string input, string expected)
        => Assert.Equal(expected, Caom2Uri.ToObservationUri(input));

    [Theory]
    [InlineData("https://example.com/x")] // foreign scheme
    [InlineData("")]
    [InlineData(null)]
    [InlineData("caom:")]                  // missing collection + obsID
    [InlineData("caom:CFHT/")]             // missing obsID
    public void ToObservationUri_Invalid_ReturnsNull(string? input)
        => Assert.Null(Caom2Uri.ToObservationUri(input));

    /// <summary>The collection and the archive's id, as Search is asked for them — or nothing, never a guess.</summary>
    [Theory]
    [InlineData("ivo://cadc.nrc.ca/CFHTMEGAPIPE?G006.010.684+41.269/G006.010.684+41.269.I", "CFHTMEGAPIPE", "G006.010.684+41.269")]
    [InlineData("ivo://cadc.nrc.ca/HST/mirror?j8pu0y010/calibrated", "HST", "j8pu0y010")]
    [InlineData("caom:CFHT/22803/22803p", "CFHT", "22803")]
    [InlineData("https://example.com/x", "", "")]
    [InlineData(null, "", "")]
    public void Split_GivesTheCollectionAndTheObservationId(string? input, string collection, string observationId)
        => Assert.Equal((collection, observationId), Caom2Uri.Split(input));
}
