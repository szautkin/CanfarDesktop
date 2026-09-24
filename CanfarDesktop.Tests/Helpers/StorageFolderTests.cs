using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// A folder as the Storage screen addresses it. Agents meet VOSpace paths relative (from the screen and
/// the compute contract) and absolute (from list_vospace_path); both have to open the same folder, and
/// neither may open somebody else's.
/// </summary>
public class StorageFolderTests
{
    [Theory]
    [InlineData(".verbinal/exec", ".verbinal/exec")]
    [InlineData("/alice/.verbinal/exec", ".verbinal/exec")]
    [InlineData("data//run1/", "data/run1")]
    [InlineData(@"data\run1", "data/run1")]
    [InlineData("./data", "data")]
    public void BothSpellingsLandInTheSameFolder(string given, string expected)
        => Assert.Equal(expected, StorageFolder.RelativeToHome(given, "alice"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/alice")]
    [InlineData("/alice/")]
    public void TheHomeItselfIsEmpty(string? given)
        => Assert.Equal(string.Empty, StorageFolder.RelativeToHome(given, "alice"));

    [Theory]
    [InlineData("/bob/data")]
    [InlineData("/alicejones/data")]   // a longer name that starts the same is somebody else
    [InlineData("/projects/survey")]
    [InlineData("../bob")]
    [InlineData("data/../../bob")]
    public void NothingOutsideTheHomeIsShown(string given)
        => Assert.Null(StorageFolder.RelativeToHome(given, "alice"));

    [Fact]
    public void SignedOutThereIsNoHomeToBeIn()
        => Assert.Null(StorageFolder.RelativeToHome("/alice/data", string.Empty));
}
