using Xunit;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Models;

public class ImageDiscoverySettingsTests
{
    [Fact]
    public void BuildAuthHeader_IsBase64OfUserColonSecret()
    {
        var header = ImageDiscoverySettings.BuildAuthHeader("alice", "s3cr3t");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header));
        Assert.Equal("alice:s3cr3t", decoded);
    }

    [Fact]
    public void Defaults_AreCanfarTerminalAndImagesHost()
    {
        var s = new ImageDiscoverySettings();
        Assert.Equal("images.canfar.net/skaha/terminal:1.1.2", s.InspectorImage);
        Assert.Equal("images.canfar.net", s.RegistryHost);
        Assert.Equal(string.Empty, s.Username);
        Assert.False(s.HasSecret);
        Assert.True(s.IsAllDefaults);
    }

    [Theory]
    [InlineData("user", false, ImageDiscoverySettings.DefaultInspectorImage)]    // username set
    [InlineData("", true, ImageDiscoverySettings.DefaultInspectorImage)]          // secret stored
    [InlineData("", false, "images.canfar.net/custom/inspector:1")]              // custom inspector image
    public void IsAllDefaults_FalseWhenAnyOverrideSet(string user, bool hasSecret, string image)
        => Assert.False(new ImageDiscoverySettings { Username = user, HasSecret = hasSecret, InspectorImage = image }.IsAllDefaults);

    [Fact]
    public void IsAllDefaults_FalseWhenRepositorySet()
        => Assert.False(new ImageDiscoverySettings { RegistryRepository = "skaha" }.IsAllDefaults);
}
