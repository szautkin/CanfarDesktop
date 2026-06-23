using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Helpers.ImageDiscovery;

namespace CanfarDesktop.Tests.Services.ImageDiscovery;

public class ImageParserTests
{
    [Fact]
    public void Parse_ThreePart_SplitsRegistryProjectNameVersion()
    {
        var p = ImageParser.Parse(new RawImage { Id = "images.canfar.net/skaha/astroml:24.07", Types = new[] { "notebook" } });
        Assert.Equal("images.canfar.net", p.Registry);
        Assert.Equal("skaha", p.Project);
        Assert.Equal("astroml", p.Name);
        Assert.Equal("24.07", p.Version);
        Assert.Equal("astroml:24.07", p.Label);
        Assert.Equal(new[] { "notebook" }, p.Types);
    }

    [Fact]
    public void Parse_TwoPart_HasNoRegistry()
    {
        var p = ImageParser.Parse(new RawImage { Id = "skaha/carta:5.0.3" });
        Assert.Equal("", p.Registry);
        Assert.Equal("skaha", p.Project);
        Assert.Equal("carta", p.Name);
        Assert.Equal("5.0.3", p.Version);
    }

    [Fact]
    public void Parse_NoColon_DefaultsToLatest()
    {
        var p = ImageParser.Parse(new RawImage { Id = "images.canfar.net/skaha/terminal" });
        Assert.Equal("terminal", p.Name);
        Assert.Equal("latest", p.Version);
        Assert.Equal("terminal:latest", p.Label);
    }

    [Fact]
    public void Parse_MultiSlashName_KeepsRestAsName()
    {
        var p = ImageParser.Parse(new RawImage { Id = "reg/proj/group/sub:1" });
        Assert.Equal("reg", p.Registry);
        Assert.Equal("proj", p.Project);
        Assert.Equal("group/sub", p.Name);
        Assert.Equal("1", p.Version);
    }

    [Fact]
    public void Parse_BareName_NoRegistryNoProject()
    {
        var p = ImageParser.Parse(new RawImage { Id = "ubuntu:22.04" });
        Assert.Equal("", p.Registry);
        Assert.Equal("", p.Project);
        Assert.Equal("ubuntu", p.Name);
        Assert.Equal("22.04", p.Version);
    }

    [Fact]
    public void GroupByProject_GroupsSortedByProject_ImagesSortedByLabel()
    {
        var images = new[]
        {
            ImageParser.Parse(new RawImage { Id = "r/skaha/zoo:1" }),
            ImageParser.Parse(new RawImage { Id = "r/cadc/vos:3" }),
            ImageParser.Parse(new RawImage { Id = "r/skaha/astroml:24.07" }),
        };

        var groups = ImageParser.GroupByProject(images);

        Assert.Equal(new[] { "cadc", "skaha" }, groups.Select(g => g.Project)); // projects asc
        Assert.Equal(new[] { "astroml:24.07", "zoo:1" }, groups[1].Images.Select(i => i.Label)); // labels asc within skaha
    }
}
