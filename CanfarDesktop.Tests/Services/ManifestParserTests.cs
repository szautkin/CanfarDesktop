using System.Text.Json;
using Xunit;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Tests.Services;

public class ManifestParserTests
{
    [Fact]
    public void ParsesUbuntuCondaManifest()
    {
        var json = """
            {
              "schemaVersion": 1,
              "imageID": "images.canfar.net/skaha/astroml:24.07",
              "contentHash": "sha256:abc123",
              "capturedAt": "2026-04-30T18:42:11Z",
              "osFamily": "ubuntu",
              "osVersion": "22.04",
              "kernel": "Linux 5.15.0-1062-aws x86_64",
              "dpkgPackages": [
                {"name":"libc6","version":"2.35-0ubuntu3.4"},
                {"name":"openssh-client","version":"1:8.9p1-3ubuntu0.6"}
              ],
              "pythonPackages": [
                {"name":"astropy","version":"6.0.1","source":"conda","env":"base"},
                {"name":"numpy","version":"1.26.4","source":"conda","env":"base"}
              ],
              "condaEnvs": [
                {"name":"base","prefix":"/opt/conda","packages":[
                  {"name":"astropy","version":"6.0.1","source":"conda","env":"base"}
                ]}
              ]
            }
            """;

        var m = ManifestParser.Parse(json);
        Assert.Equal(1, m.SchemaVersion);
        Assert.Equal("images.canfar.net/skaha/astroml:24.07", m.ImageID);
        Assert.Equal("sha256:abc123", m.ContentHash);
        Assert.Equal("ubuntu", m.OsFamily);
        Assert.Equal("22.04", m.OsVersion);
        Assert.Equal(2, m.DpkgPackages.Count);
        Assert.Equal("libc6", m.DpkgPackages[0].Name);
        Assert.Equal(2, m.PythonPackages.Count);
        Assert.Single(m.CondaEnvs);
        Assert.Single(m.CondaEnvs[0].Packages);
        Assert.Null(m.ProbeNotes);
    }

    [Fact]
    public void ParsesEmptyManifestWithNotes()
    {
        var json = """
            {"schemaVersion":1,"imageID":"images.canfar.net/skaha/scratch:edge",
             "capturedAt":"2026-04-30T20:00:00Z","osFamily":"unknown","osVersion":"unknown","kernel":"Linux",
             "probeNotes":"image lacks dpkg/rpm/apk and pip"}
            """;
        var m = ManifestParser.Parse(json);
        Assert.Equal("image lacks dpkg/rpm/apk and pip", m.ProbeNotes);
        Assert.Empty(m.DpkgPackages);
    }

    [Fact]
    public void ToleratesMissingOptionalFields()
    {
        var json = """
            {"schemaVersion":1,"imageID":"test:1","capturedAt":"2026-04-30T20:00:00Z",
             "osFamily":"ubuntu","osVersion":"22.04","kernel":"Linux"}
            """;
        var m = ManifestParser.Parse(json);
        Assert.Equal("sha256:none", m.ContentHash);  // default for missing
        Assert.Empty(m.DpkgPackages);
        Assert.Empty(m.PythonPackages);
        Assert.Empty(m.CondaEnvs);
    }

    [Fact]
    public void EmptyData_ThrowsEmpty()
    {
        var ex = Assert.Throws<ManifestParseException>(() => ManifestParser.Parse(""));
        Assert.Equal(ManifestParseKind.Empty, ex.Kind);
    }

    [Fact]
    public void MalformedJson_ThrowsMalformed()
    {
        var ex = Assert.Throws<ManifestParseException>(() => ManifestParser.Parse("{not json"));
        Assert.Equal(ManifestParseKind.Malformed, ex.Kind);
    }

    [Fact]
    public void TypeMismatch_ThrowsMalformed()
    {
        var json = """{"schemaVersion":1,"imageID":"test:1","capturedAt":"2026-04-30T20:00:00Z","dpkgPackages":"not-an-array"}""";
        var ex = Assert.Throws<ManifestParseException>(() => ManifestParser.Parse(json));
        Assert.Equal(ManifestParseKind.Malformed, ex.Kind);
    }

    [Fact]
    public void MissingImageId_ThrowsMalformed()
    {
        var json = """{"schemaVersion":1,"capturedAt":"2026-04-30T20:00:00Z"}""";
        var ex = Assert.Throws<ManifestParseException>(() => ManifestParser.Parse(json));
        Assert.Equal(ManifestParseKind.Malformed, ex.Kind);
    }

    [Fact]
    public void FutureSchemaVersion_Rejected()
    {
        var json = """{"schemaVersion":99,"imageID":"test:1","capturedAt":"2026-04-30T20:00:00Z"}""";
        var ex = Assert.Throws<ManifestParseException>(() => ManifestParser.Parse(json));
        Assert.Equal(ManifestParseKind.UnknownSchema, ex.Kind);
        Assert.Equal(99, ex.SchemaVersion);
    }

    [Theory]
    [InlineData("images.canfar.net/skaha/astroml:24.07", "images.canfar.net_skaha_astroml_24.07")]
    [InlineData("images.canfar.net/x/y:1?2*3", "images.canfar.net_x_y_1_2_3")]
    [InlineData("simple", "simple")]
    public void Sanitize_StripsUnsafeChars(string input, string expected)
        => Assert.Equal(expected, ImageManifest.Sanitize(input));

    [Fact]
    public void RoundTripsThroughJson()
    {
        var original = new ImageManifest
        {
            ImageID = "images.canfar.net/skaha/test:1.0",
            ContentHash = "sha256:roundtrip",
            CapturedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            OsFamily = "ubuntu",
            DpkgPackages = new[] { new ImagePackage("a", "1") },
            PythonPackages = new[] { new PythonPackage("astropy", "6", "pip", "base") },
        };

        var decoded = ManifestParser.Parse(JsonSerializer.Serialize(original));
        Assert.Equal(original.ImageID, decoded.ImageID);
        Assert.Equal(original.DpkgPackages, decoded.DpkgPackages);
        Assert.Equal(original.PythonPackages, decoded.PythonPackages);
    }
}
