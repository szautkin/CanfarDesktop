using Xunit;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

public class Caom2ParserTests
{
    [Fact]
    public void Parse_MinimalObservation()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <caom2:Observation xmlns:caom2="http://www.opencadc.org/caom2/xml/v2.4" xsi:type="caom2:SimpleObservation" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <caom2:collection>CFHT</caom2:collection>
              <caom2:observationID>22803</caom2:observationID>
              <caom2:type>OBJECT</caom2:type>
              <caom2:intent>calibration</caom2:intent>
              <caom2:algorithm><caom2:name>exposure</caom2:name></caom2:algorithm>
              <caom2:telescope><caom2:name>CFHT 3.6m</caom2:name></caom2:telescope>
              <caom2:instrument><caom2:name>1872 RETICON</caom2:name></caom2:instrument>
              <caom2:planes>
                <caom2:plane>
                  <caom2:productID>22803o</caom2:productID>
                  <caom2:dataProductType>spectrum</caom2:dataProductType>
                  <caom2:calibrationLevel>1</caom2:calibrationLevel>
                </caom2:plane>
              </caom2:planes>
            </caom2:Observation>
            """;

        var obs = CAOM2Parser.Parse(xml);

        Assert.Equal("CFHT", obs.Collection);
        Assert.Equal("22803", obs.ObservationID);
        Assert.Equal("OBJECT", obs.ObservationType);
        Assert.Equal("calibration", obs.Intent);
        Assert.Equal("exposure", obs.Algorithm);
        Assert.Equal("CFHT 3.6m", obs.Telescope?.Name);
        Assert.Equal("1872 RETICON", obs.Instrument?.Name);
        Assert.Single(obs.Planes);
        Assert.Equal("22803o", obs.Planes[0].ProductID);
        Assert.Equal("spectrum", obs.Planes[0].DataProductType);
        Assert.Equal(1, obs.Planes[0].CalibrationLevel);
    }

    [Fact]
    public void Parse_MissingCollection_Throws()
    {
        var xml = """
            <?xml version="1.0"?>
            <caom2:Observation xmlns:caom2="http://www.opencadc.org/caom2/xml/v2.4">
              <caom2:observationID>x</caom2:observationID>
            </caom2:Observation>
            """;
        var ex = Assert.Throws<Caom2ParseException>(() => CAOM2Parser.Parse(xml));
        Assert.Contains("collection", ex.Message);
    }

    [Fact]
    public void Parse_MissingObservationID_Throws()
    {
        var xml = """
            <?xml version="1.0"?>
            <caom2:Observation xmlns:caom2="http://www.opencadc.org/caom2/xml/v2.4">
              <caom2:collection>CFHT</caom2:collection>
            </caom2:Observation>
            """;
        var ex = Assert.Throws<Caom2ParseException>(() => CAOM2Parser.Parse(xml));
        Assert.Contains("observationID", ex.Message);
    }

    [Fact]
    public void Parse_MalformedXml_Throws()
    {
        var ex = Assert.Throws<Caom2ParseException>(() => CAOM2Parser.Parse("this is not xml"));
        Assert.Contains("Malformed", ex.Message);
    }

    [Fact]
    public void Parse_TolerantOfUnknownElements()
    {
        var xml = """
            <?xml version="1.0"?>
            <caom2:Observation xmlns:caom2="http://www.opencadc.org/caom2/xml/v2.4">
              <caom2:collection>X</caom2:collection>
              <caom2:observationID>y</caom2:observationID>
              <caom2:newFieldFromFutureSchema>ignored</caom2:newFieldFromFutureSchema>
            </caom2:Observation>
            """;
        var obs = CAOM2Parser.Parse(xml);
        Assert.Equal("X", obs.Collection);
        Assert.Equal("y", obs.ObservationID);
    }

    [Fact]
    public void Parse_RichObservation()
    {
        var xml = """
            <?xml version="1.0"?>
            <caom2:Observation xmlns:caom2="http://www.opencadc.org/caom2/xml/v2.4">
              <caom2:collection>JWST</caom2:collection>
              <caom2:observationID>jw01147</caom2:observationID>
              <caom2:metaRelease>2024-03-15T10:30:45.123</caom2:metaRelease>
              <caom2:proposal>
                <caom2:id>1147</caom2:id>
                <caom2:pi>Smith, J.</caom2:pi>
                <caom2:project>NIRCam Deep</caom2:project>
                <caom2:title>Deep imaging of M31</caom2:title>
                <caom2:keywords>
                  <caom2:keyword>galaxy</caom2:keyword>
                  <caom2:keyword>imaging</caom2:keyword>
                </caom2:keywords>
              </caom2:proposal>
              <caom2:target>
                <caom2:name>M31</caom2:name>
                <caom2:type>galaxy</caom2:type>
                <caom2:standard>false</caom2:standard>
                <caom2:redshift>-0.001</caom2:redshift>
                <caom2:moving>0</caom2:moving>
              </caom2:target>
              <caom2:telescope>
                <caom2:name>JWST</caom2:name>
                <caom2:geoLocationX>1.0</caom2:geoLocationX>
                <caom2:geoLocationY>2.0</caom2:geoLocationY>
                <caom2:geoLocationZ>3.0</caom2:geoLocationZ>
              </caom2:telescope>
              <caom2:instrument><caom2:name>NIRCam</caom2:name></caom2:instrument>
              <caom2:environment>
                <caom2:photometric>true</caom2:photometric>
                <caom2:ambientTemp>40.0</caom2:ambientTemp>
              </caom2:environment>
              <caom2:planes>
                <caom2:plane>
                  <caom2:productID>nircam_f200w</caom2:productID>
                  <caom2:dataProductType>image</caom2:dataProductType>
                  <caom2:calibrationLevel>2</caom2:calibrationLevel>
                  <caom2:dataRelease>2025-06-30T00:00:00</caom2:dataRelease>
                  <caom2:provenance>
                    <caom2:name>jwst_pipeline</caom2:name>
                    <caom2:version>1.13.0</caom2:version>
                    <caom2:producer>STScI</caom2:producer>
                    <caom2:reference>https://jwst-pipeline.readthedocs.io/</caom2:reference>
                  </caom2:provenance>
                  <caom2:metrics>
                    <caom2:sourceNumberDensity>1234.5</caom2:sourceNumberDensity>
                    <caom2:magLimit>26.5</caom2:magLimit>
                  </caom2:metrics>
                  <caom2:quality><caom2:flag>good</caom2:flag></caom2:quality>
                  <caom2:position>
                    <caom2:bounds xsi:type="caom2:Polygon" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                      <caom2:Polygon>
                        <caom2:points>
                          <caom2:vertex><caom2:cval1>10.5</caom2:cval1><caom2:cval2>41.0</caom2:cval2></caom2:vertex>
                          <caom2:vertex><caom2:cval1>10.6</caom2:cval1><caom2:cval2>41.0</caom2:cval2></caom2:vertex>
                          <caom2:vertex><caom2:cval1>10.6</caom2:cval1><caom2:cval2>41.1</caom2:cval2></caom2:vertex>
                          <caom2:vertex><caom2:cval1>10.5</caom2:cval1><caom2:cval2>41.1</caom2:cval2></caom2:vertex>
                        </caom2:points>
                      </caom2:Polygon>
                    </caom2:bounds>
                    <caom2:dimension>
                      <caom2:naxis1>2048</caom2:naxis1>
                      <caom2:naxis2>2048</caom2:naxis2>
                    </caom2:dimension>
                  </caom2:position>
                  <caom2:energy>
                    <caom2:bounds>
                      <caom2:lower>1.755e-6</caom2:lower>
                      <caom2:upper>2.227e-6</caom2:upper>
                    </caom2:bounds>
                    <caom2:bandpassName>F200W</caom2:bandpassName>
                    <caom2:emBand>Infrared</caom2:emBand>
                  </caom2:energy>
                  <caom2:time>
                    <caom2:bounds>
                      <caom2:lower>59000.5</caom2:lower>
                      <caom2:upper>59000.55</caom2:upper>
                    </caom2:bounds>
                    <caom2:exposure>4320.0</caom2:exposure>
                  </caom2:time>
                  <caom2:polarization>
                    <caom2:states>
                      <caom2:state>I</caom2:state>
                      <caom2:state>Q</caom2:state>
                    </caom2:states>
                  </caom2:polarization>
                  <caom2:artifacts>
                    <caom2:artifact>
                      <caom2:uri>cadc:JWST/jw01147_nircam_f200w_i2d.fits</caom2:uri>
                      <caom2:productType>science</caom2:productType>
                      <caom2:releaseType>data</caom2:releaseType>
                      <caom2:contentType>application/fits</caom2:contentType>
                      <caom2:contentLength>123456789</caom2:contentLength>
                      <caom2:contentChecksum>md5:abcdef</caom2:contentChecksum>
                    </caom2:artifact>
                    <caom2:artifact>
                      <caom2:uri>cadc:JWST/jw01147_nircam_f200w_preview.png</caom2:uri>
                      <caom2:productType>preview</caom2:productType>
                    </caom2:artifact>
                  </caom2:artifacts>
                </caom2:plane>
              </caom2:planes>
            </caom2:Observation>
            """;

        var obs = CAOM2Parser.Parse(xml);

        Assert.Equal("JWST", obs.Collection);
        Assert.Equal("jw01147", obs.ObservationID);
        Assert.NotNull(obs.MetaRelease);

        Assert.Equal("1147", obs.Proposal?.Id);
        Assert.Equal("Smith, J.", obs.Proposal?.Pi);
        Assert.Equal("Deep imaging of M31", obs.Proposal?.Title);
        Assert.Equal(new[] { "galaxy", "imaging" }, obs.Proposal?.Keywords);

        Assert.Equal("M31", obs.Target?.Name);
        Assert.Equal("galaxy", obs.Target?.Type);
        Assert.Equal(-0.001, obs.Target?.Redshift);
        Assert.False(obs.Target?.Moving);
        Assert.False(obs.Target?.Standard);

        Assert.Equal("JWST", obs.Telescope?.Name);
        Assert.Equal(1.0, obs.Telescope?.GeoLocation?.X);

        Assert.True(obs.Environment?.Photometric);
        Assert.Equal(40.0, obs.Environment?.AmbientTemp);

        var plane = Assert.Single(obs.Planes);
        Assert.Equal("nircam_f200w", plane.ProductID);
        Assert.Equal("image", plane.DataProductType);
        Assert.Equal(2, plane.CalibrationLevel);
        Assert.Equal("good", plane.Quality);

        Assert.Equal("jwst_pipeline", plane.Provenance?.Name);
        Assert.Equal("1.13.0", plane.Provenance?.Version);
        Assert.Equal("https://jwst-pipeline.readthedocs.io/", plane.Provenance?.Reference);

        Assert.Equal(26.5, plane.Metrics?.MagLimit);
        Assert.Equal(1234.5, plane.Metrics?.SourceNumberDensity);

        Assert.Equal(4, plane.Position?.Polygon.Count);
        Assert.Equal(10.5, plane.Position?.Polygon[0].Ra);
        Assert.Equal(41.0, plane.Position?.Polygon[0].Dec);
        Assert.Equal(2048, plane.Position?.DimensionPixels?.NAxis1);
        Assert.Equal(2048, plane.Position?.DimensionPixels?.NAxis2);

        Assert.Equal(1.755e-6, plane.Energy?.LowerMetres);
        Assert.Equal(2.227e-6, plane.Energy?.UpperMetres);
        Assert.Equal("F200W", plane.Energy?.BandpassName);
        Assert.Equal("Infrared", plane.Energy?.EmBand);

        Assert.Equal(59000.5, plane.Time?.LowerMJD);
        Assert.Equal(59000.55, plane.Time?.UpperMJD);
        Assert.Equal(4320.0, plane.Time?.ExposureSeconds);

        Assert.Equal(new[] { "I", "Q" }, plane.Polarization?.States);

        Assert.Equal(2, plane.Artifacts.Count);
        Assert.Equal("cadc:JWST/jw01147_nircam_f200w_i2d.fits", plane.Artifacts[0].Uri);
        Assert.Equal(123_456_789, plane.Artifacts[0].ContentLength);
        Assert.Equal("application/fits", plane.Artifacts[0].ContentType);
        Assert.Equal("preview", plane.Artifacts[1].ProductType);
    }

    [Fact]
    public async Task Service_InvalidPublisherId_ReturnsInvalidId_NoNetwork()
    {
        var service = new CAOM2Service(new HttpClient(), new CanfarDesktop.Helpers.ApiEndpoints());
        var result = await service.GetByPublisherIdAsync("not-a-publisher-id");
        Assert.Equal(Caom2Status.InvalidId, result.Status);
        Assert.Null(result.Observation);
    }
}
