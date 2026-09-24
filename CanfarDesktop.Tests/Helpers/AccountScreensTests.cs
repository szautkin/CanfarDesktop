using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services.Workflows;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>The screens locked until sign-in: on the landing page, and however else they are opened.</summary>
public class AccountScreensTests
{
    [Fact]
    public void PortalRemoteComputeAndStorageAreTheAccountScreens()
        => Assert.Equal(["portal", "remoteCompute", "storage"], AccountScreens.All.Order(StringComparer.Ordinal));

    /// <summary>
    /// A name navigate_to does not take would lock nothing, silently: the tile would still open and
    /// navigation would pass it by.
    /// </summary>
    [Fact]
    public void EachIsAScreenTheAppHas()
        => Assert.All(AccountScreens.All, s => Assert.Contains(s, WorkflowFormat.KnownViews));

    [Theory]
    [InlineData("search")]
    [InlineData("fitsViewer")]
    [InlineData("landing")]
    [InlineData("Portal")]   // navigate_to's names are exact
    [InlineData(null)]
    public void EverythingElseIsOpen(string? screen) => Assert.False(AccountScreens.Contains(screen));
}
