using Xunit;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Services.ImageDiscovery;

/// <summary>
/// The CANFAR images as someone choosing what to run sees them — the Portal card's rules, shared with Find
/// by package and the agents' listing, and those of Verbinal for Linux's card.
/// </summary>
public class ImageCatalogueTests
{
    private static ParsedImage Img(string id, params string[] types) => ImageParser.Parse(new RawImage { Id = id, Types = types });

    private static readonly IReadOnlySet<string> NothingAdded = new HashSet<string>();

    // ── What a launch can start ───────────────────────────────────────────────

    [Fact]
    public void ADesktopApp_IsNotSomethingToLaunch()
        => Assert.False(ImageCatalogue.Launchable(Img("images.canfar.net/casa/casa:6.6", "desktop-app"), NothingAdded));

    [Theory]
    [InlineData("notebook")]
    [InlineData("desktop")]
    [InlineData("carta")]
    [InlineData("contributed")]
    [InlineData("firefly")]
    [InlineData("headless")]
    public void EverythingALaunchTabOffers_Stays(string type)
        => Assert.True(ImageCatalogue.Launchable(Img("h/p/n:1", type), NothingAdded));

    [Fact]
    public void OneLaunchableType_IsEnough()
        => Assert.True(ImageCatalogue.Launchable(Img("h/p/n:1", "desktop-app", "headless"), NothingAdded));

    [Fact]
    public void AnImageThePersonAdded_IsKept_WhateverItsLabelsSay()
    {
        var mine = new HashSet<string> { "h/me/mine:1" };
        Assert.True(ImageCatalogue.Launchable(Img("h/me/mine:1"), mine));
        Assert.False(ImageCatalogue.Launchable(Img("h/other/x:1"), mine));
    }

    // ── Session types ─────────────────────────────────────────────────────────

    [Fact]
    public void Types_AreOffered_InTheLaunchFormsOrder_DesktopAppUnderDesktop()
    {
        var types = ImageCatalogue.Types([
            Img("h/a/x:1", "contributed"), Img("h/a/y:1", "desktop-app"), Img("h/a/z:1", "notebook"),
            Img("h/a/w:1", "carta"), Img("h/a/v:1", "newtype"), Img("h/a/u:1", "desktop")]);

        Assert.Equal(["notebook", "desktop", "carta", "contributed", "newtype"], types);
    }

    [Fact]
    public void TheDesktopFilter_ShowsDesktopAndDesktopAppAlike()
    {
        Assert.True(ImageCatalogue.OfType(Img("h/a/x:1", "desktop-app", "headless"), "desktop"));
        Assert.True(ImageCatalogue.OfType(Img("h/a/x:1", "desktop"), "desktop"));
        Assert.False(ImageCatalogue.OfType(Img("h/a/x:1", "notebook"), "desktop"));
    }

    [Fact]
    public void All_IsEveryType_AndEveryProject_EvenAnImageNamingNone()
    {
        var bare = Img("h/me/mine:1");
        Assert.True(ImageCatalogue.OfType(bare, ImageCatalogue.All));
        Assert.True(ImageCatalogue.InProject(bare, ImageCatalogue.All));
    }

    // ── Projects ──────────────────────────────────────────────────────────────

    private static readonly ParsedImage[] Catalogue =
    [
        Img("images.canfar.net/srcnet/notebook:1", "notebook"),
        Img("images.canfar.net/skaha/carta:4", "carta"),
        Img("images.canfar.net/skaha/astroml:1", "notebook"),
        Img("images.canfar.net/uvickbos/pipeline:2", "notebook", "headless"),
        Img("images.canfar.net/cadc/carta:5", "carta"),
    ];

    [Fact]
    public void Projects_AreThoseTheTypeLeaves_Alphabetically()
    {
        Assert.Equal(["cadc", "skaha", "srcnet", "uvickbos"], ImageCatalogue.Projects(Catalogue, ImageCatalogue.All));
        Assert.Equal(["cadc", "skaha"], ImageCatalogue.Projects(Catalogue, "carta"));
    }

    [Fact]
    public void AProjectGoneWithTheType_TakesTheChoiceBackToAll()
    {
        // uvickbos chosen, then CARTA: an empty list, its cause a button in a row just rebuilt without it.
        Assert.Equal(ImageCatalogue.All, ImageCatalogue.Surviving("uvickbos", ImageCatalogue.Projects(Catalogue, "carta")));
    }

    [Fact]
    public void AProjectStillThere_IsKept()
        => Assert.Equal("skaha", ImageCatalogue.Surviving("skaha", ImageCatalogue.Projects(Catalogue, "carta")));

    [Fact]
    public void All_SurvivesAnything()
        => Assert.Equal(ImageCatalogue.All, ImageCatalogue.Surviving(ImageCatalogue.All, []));

    // ── The rows ──────────────────────────────────────────────────────────────

    [Fact]
    public void Shown_IsTheTypeAndProject_InspectedThenFailedThenNever_ThenById()
    {
        var status = new Dictionary<string, ImageDiscoveryStatus>
        {
            ["images.canfar.net/uvickbos/pipeline:2"] = ImageDiscoveryStatus.Failed,
            ["images.canfar.net/srcnet/notebook:1"] = ImageDiscoveryStatus.Discovered,
        };

        var shown = ImageCatalogue.Shown(Catalogue, "notebook", ImageCatalogue.All,
            id => status.GetValueOrDefault(id, ImageDiscoveryStatus.Unknown));

        Assert.Equal(
            ["images.canfar.net/srcnet/notebook:1", "images.canfar.net/uvickbos/pipeline:2", "images.canfar.net/skaha/astroml:1"],
            shown.Select(i => i.Id));
    }

    [Fact]
    public void Shown_NarrowsToTheProject()
        => Assert.Equal(["images.canfar.net/skaha/carta:4"],
            ImageCatalogue.Shown(Catalogue, "carta", "skaha", _ => ImageDiscoveryStatus.Unknown).Select(i => i.Id));

    // ── The launch form's grouping, now beside the parser it uses ────────────

    [Fact]
    public void GroupByTypeAndProject_PutsEachProjectsNewestFirst()
    {
        var grouped = ImageParser.GroupByTypeAndProject([
            new RawImage { Id = "images.canfar.net/skaha/astroml:1.0", Types = ["notebook"] },
            new RawImage { Id = "images.canfar.net/skaha/astroml:2.0", Types = ["notebook", "headless"] },
        ]);

        Assert.Equal(["2.0", "1.0"], grouped["notebook"]["skaha"].Select(i => i.Version));
        Assert.Single(grouped["headless"]["skaha"]);
    }
}
