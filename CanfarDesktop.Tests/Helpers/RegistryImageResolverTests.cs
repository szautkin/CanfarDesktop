using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>The registry repo/project setting expands a short image name to host/project/name; a full
/// path is left as-is. Shared by Image Discovery + AI compute.</summary>
public class RegistryImageResolverTests
{
    [Fact]
    public void ShortName_WithRepo_IsPrefixedHostRepoName()
        => Assert.Equal("images.canfar.net/skaha/verbinal-compute:1.0",
            RegistryImageResolver.Resolve("verbinal-compute:1.0", "images.canfar.net", "skaha"));

    [Fact]
    public void ShortName_NoRepo_IsPrefixedHostName()
        => Assert.Equal("images.canfar.net/verbinal-compute:1.0",
            RegistryImageResolver.Resolve("verbinal-compute:1.0", "images.canfar.net", ""));

    [Theory]
    [InlineData("skaha/terminal:1.1.2")]                       // already has a project segment
    [InlineData("images.canfar.net/skaha/terminal:1.1.2")]     // already fully qualified
    public void NameWithSlash_IsLeftUnchanged(string image)
        => Assert.Equal(image, RegistryImageResolver.Resolve(image, "images.canfar.net", "skaha"));

    [Fact]
    public void Empty_ReturnsEmpty()
        => Assert.Equal("", RegistryImageResolver.Resolve("", "images.canfar.net", "skaha"));

    [Fact]
    public void NoHost_ReturnsBareName()
        => Assert.Equal("myimg:1", RegistryImageResolver.Resolve("myimg:1", "", "skaha"));

    [Fact]
    public void TrimsHostSlashAndRepoSlashes()
        => Assert.Equal("h/r/img",
            RegistryImageResolver.Resolve("img", "h/", "/r/"));
}
