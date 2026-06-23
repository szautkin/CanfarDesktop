using Xunit;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Services.ImageDiscovery;

public class ManifestDetailBuilderTests
{
    private static ImageManifest Full() => new()
    {
        ImageID = "images.canfar.net/skaha/astroml:24.07",
        OsFamily = "ubuntu",
        OsVersion = "22.04",
        Kernel = "Linux 5.15 x86_64",
        CapturedAt = DateTimeOffset.UnixEpoch,
        Capabilities = new[] { "fitsio", "gpu" },
        PythonPackages = new[]
        {
            new PythonPackage("numpy", "1.26", "pip", "system"),
            new PythonPackage("astropy", "6.0", "pip", "system"),
            new PythonPackage("torch", "2.2", "conda", "ml"),
        },
        CondaEnvs = new[] { new CondaEnv("ml", "/opt/conda/envs/ml", new[] { new PythonPackage("torch", "2.2", "conda", "ml") }) },
        DpkgPackages = new[] { new ImagePackage("zlib1g", "1.2"), new ImagePackage("libc6", "2.35") },
        RPackages = new[] { new ImagePackage("ggplot2", "3.4") },
    };

    [Fact]
    public void Build_OrdersSections_PythonCondaRThenSystem()
    {
        var d = ManifestDetailBuilder.Build(Full());
        var titles = d.Sections.Select(s => s.Title).ToList();

        // Python (system) first, then the conda env, then R, then dpkg.
        Assert.Equal("Python", titles[0]);
        Assert.Equal("Python · ml", titles[1]);
        Assert.Equal("Conda · ml", titles[2]);
        Assert.Equal("R", titles[3]);
        Assert.Equal("System (apt / dpkg)", titles[4]);
    }

    [Fact]
    public void Build_SortsPackagesByName_AndCounts()
    {
        var d = ManifestDetailBuilder.Build(Full());
        var dpkg = d.Sections.First(s => s.Title == "System (apt / dpkg)");
        Assert.Equal(2, dpkg.Count);
        Assert.Equal(new[] { "libc6", "zlib1g" }, dpkg.Packages.Select(p => p.Name)); // sorted
    }

    [Fact]
    public void Build_SurfacesOsKernelCapabilities()
    {
        var d = ManifestDetailBuilder.Build(Full());
        Assert.Equal("ubuntu 22.04", d.OsLine);
        Assert.Equal("Linux 5.15 x86_64", d.KernelLine);
        Assert.Equal(new[] { "fitsio", "gpu" }, d.Capabilities);
    }

    [Fact]
    public void Build_OmitsEmptyEcosystems()
    {
        var d = ManifestDetailBuilder.Build(new ImageManifest { ImageID = "x", OsFamily = "alpine", OsVersion = "3.19" });
        Assert.Empty(d.Sections); // no packages at all
        Assert.Equal(string.Empty, d.KernelLine); // "unknown" kernel hidden
    }

    [Fact]
    public void ToJson_IsIndented()
    {
        var json = ManifestDetailBuilder.ToJson(Full());
        Assert.Contains("\n", json);
        Assert.Contains("astroml", json);
    }
}
