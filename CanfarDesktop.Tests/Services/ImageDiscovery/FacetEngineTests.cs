using Xunit;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Services.ImageDiscovery;

public class FacetEngineTests
{
    private static ImageManifest Manifest(
        string id, string osFamily, string osVersion,
        string[]? dpkg = null, string[]? apk = null, string[]? python = null,
        string[]? r = null, string[]? capabilities = null)
        => new()
        {
            ImageID = id,
            OsFamily = osFamily,
            OsVersion = osVersion,
            CapturedAt = DateTimeOffset.UnixEpoch,
            DpkgPackages = (dpkg ?? Array.Empty<string>()).Select(n => new ImagePackage(n, "1")).ToArray(),
            ApkPackages = (apk ?? Array.Empty<string>()).Select(n => new ImagePackage(n, "1")).ToArray(),
            PythonPackages = (python ?? Array.Empty<string>()).Select(n => new PythonPackage(n, "1", "pip", "system")).ToArray(),
            RPackages = (r ?? Array.Empty<string>()).Select(n => new ImagePackage(n, "1")).ToArray(),
            Capabilities = capabilities ?? Array.Empty<string>(),
        };

    // ── AvailableValues (faceting) ───────────────────────────────────────────

    [Fact]
    public void AvailableValues_EmptyQuery_UnionsAllValuesInCategory()
    {
        var images = new[]
        {
            Manifest("a", "ubuntu", "22.04", python: new[] { "numpy", "astropy" }),
            Manifest("b", "almalinux", "9", python: new[] { "numpy", "scipy" }),
        };

        var py = FacetEngine.AvailableValues(new PackageQuery(), PackageQuery.Category.Python, images);
        Assert.Equal(new[] { "astropy", "numpy", "scipy" }, py.OrderBy(x => x));

        var fam = FacetEngine.AvailableValues(new PackageQuery(), PackageQuery.Category.OsFamily, images);
        Assert.Equal(new[] { "almalinux", "ubuntu" }, fam.OrderBy(x => x));
    }

    [Fact]
    public void AvailableValues_NarrowsToValuesThatStillYieldResults()
    {
        var images = new[]
        {
            Manifest("a", "ubuntu", "22.04", python: new[] { "numpy", "astropy" }),
            Manifest("b", "almalinux", "9", python: new[] { "numpy", "scipy" }),
        };

        // With osFamily=ubuntu selected, the only Python values that survive are ubuntu's.
        var query = new PackageQuery { OsFamilies = { "ubuntu" } };
        var py = FacetEngine.AvailableValues(query, PackageQuery.Category.Python, images);

        Assert.Contains("astropy", py);
        Assert.Contains("numpy", py);
        Assert.DoesNotContain("scipy", py); // scipy only exists on almalinux → would yield zero
    }

    [Fact]
    public void AvailableValues_OsFamilyFacet_IgnoresOwnCategoryConstraint()
    {
        // Dropping(OsFamily) means an osFamily selection does NOT narrow the osFamily candidate list
        // itself — you can always pick a different family.
        var images = new[]
        {
            Manifest("a", "ubuntu", "22.04"),
            Manifest("b", "almalinux", "9"),
        };
        var query = new PackageQuery { OsFamilies = { "ubuntu" } };

        var fam = FacetEngine.AvailableValues(query, PackageQuery.Category.OsFamily, images);

        Assert.Equal(new[] { "almalinux", "ubuntu" }, fam.OrderBy(x => x));
    }

    [Fact]
    public void AvailableValues_SkipsUnknownOsValues()
    {
        var images = new[] { Manifest("a", "unknown", "unknown", python: new[] { "numpy" }) };

        Assert.Empty(FacetEngine.AvailableValues(new PackageQuery(), PackageQuery.Category.OsFamily, images));
        Assert.Empty(FacetEngine.AvailableValues(new PackageQuery(), PackageQuery.Category.OsVersion, images));
    }

    [Fact]
    public void AvailableValues_Capabilities_SourcedFromManifestsNotCatalogue()
    {
        var images = new[] { Manifest("a", "ubuntu", "22.04", capabilities: new[] { "fitsio.import", "gpu" }) };

        var caps = FacetEngine.AvailableValues(new PackageQuery(), PackageQuery.Category.Capabilities, images);

        Assert.Equal(new[] { "fitsio.import", "gpu" }, caps.OrderBy(x => x));
    }

    // ── OrderedTypes ─────────────────────────────────────────────────────────

    [Fact]
    public void OrderedTypes_CanonicalFirst_ThenExtrasAlphabetical()
    {
        var ordered = FacetEngine.OrderedTypes(new[] { "zeta", "headless", "notebook", "alpha", "desktop" });
        Assert.Equal(new[] { "notebook", "desktop", "headless", "alpha", "zeta" }, ordered);
    }

    [Fact]
    public void OrderedTypes_OnlyIncludesPresentTypes()
    {
        var ordered = FacetEngine.OrderedTypes(new[] { "carta", "notebook" });
        Assert.Equal(new[] { "notebook", "carta" }, ordered);
    }
}
