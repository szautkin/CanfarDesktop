using System.Text.RegularExpressions;
using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>The Settings dialog's sections, as an agent names them.</summary>
public class SettingsSectionsTests
{
    /// <summary>
    /// The dialog's own list, in its order. A section added to the dialog and not here could not be
    /// opened by an agent; one here and not in the dialog would open nothing.
    /// </summary>
    [Fact]
    public void TheSectionsAreTheDialogsOwn()
    {
        var xaml = File.ReadAllText(RepoFiles.PathTo("Views/Dialogs/SettingsDialog.xaml"));
        var tags = Regex.Matches(xaml, @"<muxc:NavigationViewItem\b[^>]*\bTag=""(?<tag>\w+)""")
            .Select(m => m.Groups["tag"].Value);

        Assert.Equal(tags, SettingsSections.All.Select(s => s.Id));
    }

    [Theory]
    [InlineData("portal", "portal")]
    [InlineData("PORTAL", "portal")]
    [InlineData("AI compute", "compute")]
    [InlineData(" notebook ", "notebook")]
    public void ASectionIsFoundByIdOrTitle(string name, string id)
        => Assert.Equal(id, SettingsSections.Find(name)?.Id);

    [Theory]
    [InlineData("privacy")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsNoSection(string? name) => Assert.Null(SettingsSections.Find(name));
}
