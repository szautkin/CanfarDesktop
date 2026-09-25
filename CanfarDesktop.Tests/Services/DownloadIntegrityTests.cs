using System.Reflection;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// The three ways a download could report success while producing nothing usable: a fault response
/// read as an empty resolve, an index file downloaded in place of the image, and a 200 with no bytes
/// filed as a finished download.
/// </summary>
public class DownloadIntegrityTests
{
    private static DataLinkResult ParseVOTable(string xml)
    {
        var method = typeof(DataLinkService).GetMethod("ParseVOTable",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (DataLinkResult)method!.Invoke(null, [xml])!;
    }

    private static string Table(params string[] rows) => $"""
        <VOTABLE><RESOURCE><TABLE>
        <FIELD name="access_url"/>
        <FIELD name="semantics"/>
        <FIELD name="content_type"/>
        <FIELD name="error_message"/>
        <DATA><TABLEDATA>
        {string.Join("\n", rows)}
        </TABLEDATA></DATA>
        </TABLE></RESOURCE></VOTABLE>
        """;

    // ── Faults ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A response that is entirely faults used to parse to an empty file list, which reads as
    /// "resolved, nothing here" rather than the service refusing the id.
    /// </summary>
    [Fact]
    public void ParseVOTable_AllFaultRows_AreReportedNotSwallowed()
    {
        var result = ParseVOTable(Table(
            "<TR><TD></TD><TD></TD><TD></TD><TD>UsageFault: invalid ID</TD></TR>"));

        Assert.True(result.IsEntirelyFaults);
        Assert.Equal(["UsageFault: invalid ID"], result.Faults);
        Assert.Empty(result.DirectFiles);
    }

    /// <summary>A fault beside real rows is the service refusing PART of a request. Not fatal.</summary>
    [Fact]
    public void ParseVOTable_FaultBesideRealRows_IsNotFatal()
    {
        var result = ParseVOTable(Table(
            "<TR><TD></TD><TD></TD><TD></TD><TD>NotFoundFault: no such product</TD></TR>",
            "<TR><TD>https://example.com/data.fits</TD><TD>#this</TD><TD>application/fits</TD><TD></TD></TR>"));

        Assert.False(result.IsEntirelyFaults);
        Assert.Single(result.Faults);
        Assert.Single(result.DirectFiles);
    }

    /// <summary>A clean empty response is still a clean empty response — no fault, nothing to report.</summary>
    [Fact]
    public void ParseVOTable_NoRowsAndNoFaults_IsNotAFault()
    {
        var result = ParseVOTable(Table());

        Assert.False(result.IsEntirelyFaults);
        Assert.Empty(result.Faults);
    }

    // ── Which #this ──────────────────────────────────────────────────────────

    /// <summary>
    /// The JWST case: four of six planes sampled put the four-kilobyte association index ahead of the
    /// 46 MB image, so taking the first `#this` fetched the index and called it the observation.
    /// </summary>
    [Fact]
    public void PreferScienceFile_PicksTheImageOverTheAssociationIndex()
    {
        var files = new List<DataLinkFile>
        {
            new() { Url = "https://example.com/jw01234_asn.json", ContentType = "application/json" },
            new() { Url = "https://example.com/jw01234_i2d.fits", ContentType = "application/fits" },
        };

        Assert.Equal("https://example.com/jw01234_i2d.fits", DataLinkArtifactSelector.PreferScienceFile(files)?.Url);
    }

    /// <summary>fpack-compressed science data is science data.</summary>
    [Fact]
    public void PreferScienceFile_RecognisesFpack()
    {
        var files = new List<DataLinkFile>
        {
            new() { Url = "https://example.com/catalogue.csv", ContentType = "text/csv" },
            new() { Url = "https://example.com/image.fits.fz", ContentType = "application/octet-stream" },
        };

        Assert.Equal("https://example.com/image.fits.fz", DataLinkArtifactSelector.PreferScienceFile(files)?.Url);
    }

    /// <summary>
    /// The single-product case every other collection is, unchanged: one row in, that row out,
    /// whatever it looks like.
    /// </summary>
    [Fact]
    public void PreferScienceFile_SingleRow_IsReturnedWhateverItIs()
    {
        var files = new List<DataLinkFile> { new() { Url = "https://example.com/only.json" } };
        Assert.Equal("https://example.com/only.json", DataLinkArtifactSelector.PreferScienceFile(files)?.Url);
    }

    /// <summary>Ranked, not filtered: unclassifiable rows stay candidates and ties keep service order.</summary>
    [Fact]
    public void PreferScienceFile_UnrecognisedRows_KeepServiceOrder()
    {
        var files = new List<DataLinkFile>
        {
            new() { Url = "https://example.com/first" },
            new() { Url = "https://example.com/second" },
        };

        Assert.Equal("https://example.com/first", DataLinkArtifactSelector.PreferScienceFile(files)?.Url);
    }

    [Fact]
    public void PreferScienceFile_Empty_IsNull()
        => Assert.Null(DataLinkArtifactSelector.PreferScienceFile([]));

    /// <summary>The preference reaches the property every download path actually reads.</summary>
    [Fact]
    public void DirectFileUrl_UsesTheSciencePreference()
    {
        var result = ParseVOTable(Table(
            "<TR><TD>https://example.com/jw_asn.json</TD><TD>#this</TD><TD>application/json</TD><TD></TD></TR>",
            "<TR><TD>https://example.com/jw_i2d.fits</TD><TD>#this</TD><TD>application/fits</TD><TD></TD></TR>"));

        Assert.Equal("https://example.com/jw_i2d.fits", result.DirectFileUrl);
    }

    // ── Zero bytes ───────────────────────────────────────────────────────────

    /// <summary>
    /// The `pkg` endpoint is the fallback used when DataLink resolved nothing, so an empty response
    /// from it almost always means the id did not resolve — a different thing to tell the user than
    /// "this artifact is empty", and the only place that can tell them apart.
    /// </summary>
    [Theory]
    [InlineData("https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/pkg?ID=ivo%3A%2F%2Fx", true)]
    [InlineData("https://ws.cadc.example/data/pkg?ID=abc", true)]
    [InlineData("https://ws.cadc.example/data/sub/jw01234_i2d.fits", false)]
    public void IsPackageEndpoint_DistinguishesTheFallbackFromAResolvedArtifact(string url, bool expected)
        => Assert.Equal(expected, ObservationDownloadService.IsPackageEndpoint(url));

    /// <summary>An unresolved id is told what a real one looks like, because a wrong one is the cause.</summary>
    [Fact]
    public void EmptyDownloadException_FromUnresolvedId_ShowsTheIdShape()
    {
        var ex = new EmptyDownloadException("https://x/caom2ops/pkg?ID=nope", fromUnresolvedId: true);
        Assert.Contains(EmptyDownloadException.MirroredIdShape, ex.Message);
    }

    /// <summary>An empty artifact is an empty artifact, and says so rather than blaming the id.</summary>
    [Fact]
    public void EmptyDownloadException_FromEmptyArtifact_SaysSo()
    {
        var ex = new EmptyDownloadException("https://x/data.fits", fromUnresolvedId: false);
        Assert.Contains("0 bytes", ex.Message);
        Assert.DoesNotContain(EmptyDownloadException.MirroredIdShape, ex.Message);
    }

    [Fact]
    public void DataLinkFaultException_CarriesTheServiceText()
    {
        var ex = new DataLinkFaultException("ivo://cadc.nrc.ca/x", ["UsageFault: invalid ID"]);
        Assert.Contains("UsageFault: invalid ID", ex.Message);
        Assert.Contains("ivo://cadc.nrc.ca/x", ex.Message);
    }
}
