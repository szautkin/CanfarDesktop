using Xunit;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Tests.Services;

public class EmbeddedProbeScriptsTests
{
    private static EmbeddedProbeScripts Load()
        => EmbeddedProbeScripts.FromAssembly(typeof(EmbeddedProbeScriptsTests).Assembly);

    [Fact]
    public void ProbeBody_EmbedsContractMarkers()
    {
        var s = Load();
        Assert.StartsWith("#!/usr/bin/env bash", s.ProbeBody);
        Assert.Contains("IMAGE_ID", s.ProbeBody);
        Assert.Contains(".verbinal/manifests", s.ProbeBody);
        Assert.Contains("mv \"$TMP\" \"$OUT\"", s.ProbeBody);     // atomic publish step
        Assert.Contains("\"schemaVersion\": 3,", s.ProbeBody);    // matches parser max schema
    }

    [Fact]
    public void InspectorBody_EmbedsContractMarkers()
    {
        var s = Load();
        Assert.StartsWith("#!/usr/bin/env bash", s.InspectorBody);
        Assert.Contains("TARGET_IMAGE", s.InspectorBody);
        Assert.Contains("syft", s.InspectorBody);
        Assert.Contains(".verbinal/manifests", s.InspectorBody);
    }

    [Fact]
    public void UploadFileNames_AreContentHashed_AndStable()
    {
        var s = Load();
        Assert.Matches(@"^probe-[0-9a-f]{12}\.sh$", s.ProbeUploadFileName);
        Assert.Matches(@"^inspector-[0-9a-f]{12}\.sh$", s.InspectorUploadFileName);
        Assert.Equal(".verbinal", s.HomeSubdirectory);
        Assert.Equal(s.ProbeUploadFileName, Load().ProbeUploadFileName); // deterministic across loads
    }
}
